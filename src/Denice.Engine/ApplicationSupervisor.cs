// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;

namespace Denice.Engine;

/// <summary>
/// Runs one application server attempt at a time and keeps it running: a failed start is retried after 250 ms, 500 ms,
/// 1 s, 2 s and then every 5 s; a running server is probed every second and replaced when it stops or stops answering.
/// Every start attempt and transition publishes a new <see cref="ApplicationPageState"/> revision.
/// </summary>
public sealed class ApplicationSupervisor : IApplicationSession, IDisposable
{
    private const int Created = 0;
    private const int Running = 1;
    private const int Stopping = 2;
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];
    private static readonly TimeSpan AttemptLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TeardownLimit = TimeSpan.FromSeconds(10);
    private readonly IApplicationHost _host;
    private readonly TimeProvider _clock;
    private readonly Action<Uri>? _ready;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _publish = new();
    private ApplicationPageState _state = new(0, null, null);
    private int _phase;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ApplicationSupervisor"/> class.</summary>
    /// <param name="host">Starts server attempts.</param>
    /// <param name="clock">The clock for limits, probes and back-off.</param>
    /// <param name="ready">Called with each newly ready address, on a worker thread.</param>
    private ApplicationSupervisor(IApplicationHost host, TimeProvider clock, Action<Uri>? ready)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _ready = ready;
    }

    /// <inheritdoc />
    public Uri StartupPage => Engine.StartupPage.Address;

    /// <inheritdoc />
    public ApplicationPageState State => Volatile.Read(ref _state);

    /// <inheritdoc />
    public Task Stopped => _stopped.Task;

    /// <summary>Creates a supervisor of the real application server.</summary>
    /// <param name="clock">The clock for limits, probes and back-off.</param>
    /// <param name="ready">Called with each newly ready address, on a worker thread.</param>
    /// <returns>A supervisor that has not started.</returns>
    public static IApplicationSession Create(TimeProvider clock, Action<Uri>? ready) => Create(new ApplicationServer.Host(clock), clock, ready);

    /// <inheritdoc />
    public void Start()
    {
        if (Interlocked.CompareExchange(ref _phase, Running, Created) != Created)
        {
            return;
        }

        _ = Task.Run(() => SuperviseAsync(_stop.Token), CancellationToken.None).ContinueWith(
            finished =>
            {
                if (finished.Exception is { } failure)
                {
                    _stopped.TrySetException(failure.InnerExceptions);
                }
                else
                {
                    _stopped.TrySetResult();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <inheritdoc />
    public void RequestStop()
    {
        int previous = Interlocked.Exchange(ref _phase, Stopping);
        lock (_publish)
        {
            if (!_disposed)
            {
                _stop.Cancel();
            }
        }

        if (previous == Created)
        {
            _stopped.TrySetResult();
        }
    }

    /// <summary>Stops the session if needed, and releases the stop signal once the supervisor has finished.</summary>
    public void Dispose()
    {
        RequestStop();
        if (_stopped.Task.IsCompleted)
        {
            lock (_publish)
            {
                _stop.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>Creates a supervisor of the given server host.</summary>
    /// <param name="host">Starts server attempts.</param>
    /// <param name="clock">The clock for limits, probes and back-off.</param>
    /// <param name="ready">Called with each newly ready address, on a worker thread.</param>
    /// <returns>A supervisor that has not started.</returns>
    internal static IApplicationSession Create(IApplicationHost host, TimeProvider clock, Action<Uri>? ready) => new ApplicationSupervisor(host, clock, ready);

    private async Task SuperviseAsync(CancellationToken stop)
    {
        int failures = 0;
        while (!stop.IsCancellationRequested)
        {
            Publish(null, State.Error, stop);
            await RunAttemptAsync(() => failures = 0, stop).ConfigureAwait(false);
            try
            {
                await Task.Delay(Backoff[Math.Min(failures, Backoff.Length - 1)], _clock, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            failures++;
        }
    }

    /// <summary>Runs one attempt until it fails or the session stops, publishes it unavailable, then tears it down.</summary>
    private async Task RunAttemptAsync(Action becameReady, CancellationToken stop)
    {
        IRunningApplication? server = null;
        string error;
        try
        {
            using (var limit = new CancellationTokenSource(AttemptLimit, _clock))
            using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(stop, limit.Token))
            {
                server = await _host.StartAsync(attempt.Token).ConfigureAwait(false);
            }

            error = await ServeAsync(server, becameReady, stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            error = "DENICE is closing.";
        }
        catch (OperationCanceledException)
        {
            error = "The DENICE server did not start within ten seconds.";
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            error = "The DENICE server could not start: " + failure.Message;
        }

        Publish(null, error, stop);
        if (server is not null)
        {
            await TearDownAsync(server, stop).ConfigureAwait(false);
        }
    }

    private async Task<string> ServeAsync(IRunningApplication server, Action becameReady, CancellationToken stop)
    {
        if (!await server.ProbeAsync(ProbeTimeout, stop).ConfigureAwait(false))
        {
            return "The DENICE server started but did not answer its health check.";
        }

        if (Publish(server.Address, null, stop))
        {
            becameReady();
            _ready?.Invoke(server.Address);
        }

        return await WatchAsync(server, stop).ConfigureAwait(false);
    }

    private async Task<string> WatchAsync(IRunningApplication server, CancellationToken stop)
    {
        while (true)
        {
            await Task.Delay(ProbeInterval, _clock, stop).ConfigureAwait(false);
            if (server.Stopping.IsCancellationRequested)
            {
                return "The DENICE server stopped. Restarting it.";
            }

            if (!await server.ProbeAsync(ProbeTimeout, stop).ConfigureAwait(false))
            {
                return "The DENICE server stopped answering. Restarting it.";
            }
        }
    }

    private async Task TearDownAsync(IRunningApplication server, CancellationToken stop)
    {
        // A server that cannot finish teardown must not be followed by an overlapping second server: the session ends.
        Task disposed = server.DisposeAsync().AsTask();
        if (await Task.WhenAny(disposed, Task.Delay(TeardownLimit, _clock, CancellationToken.None)).ConfigureAwait(false) != disposed)
        {
            const string Message = "The DENICE server did not stop within ten seconds.";
            Publish(null, Message, stop);
            throw new TimeoutException(Message);
        }

        await disposed.ConfigureAwait(false);
    }

    /// <summary>Publishes the next revision unless the session is stopping; a ready state is never published after a stop.</summary>
    private bool Publish(Uri? address, string? error, CancellationToken stop)
    {
        lock (_publish)
        {
            if (stop.IsCancellationRequested)
            {
                return false;
            }

            ApplicationPageState current = Volatile.Read(ref _state);
            Volatile.Write(ref _state, new ApplicationPageState(current.Revision + 1, address, error));
            return true;
        }
    }
}
