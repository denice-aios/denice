// Copyright (c) Denice.ai. All rights reserved.

using System.Collections.Concurrent;
using Denice.Engine;
using Microsoft.Extensions.Time.Testing;

namespace Denice.Tests;

/// <summary>
/// Starts real <see cref="ApplicationServer"/> attempts for supervisor integration tests and keeps each one, so a test
/// can stop a real server underneath the supervisor and check that every server it started was released.
/// </summary>
internal sealed class RealApplicationHost : IApplicationHost, IAsyncDisposable
{
    private readonly IApplicationHost _real;
    private readonly ConcurrentQueue<Attempt> _attempts = new();
    private int _started;

    private RealApplicationHost(IApplicationHost real)
    {
        _real = real;
    }

    /// <summary>Gets the number of start attempts so far.</summary>
    internal int Started => Volatile.Read(ref _started);

    /// <summary>Gets the servers started so far, in order.</summary>
    internal IReadOnlyList<Attempt> Servers => [.. _attempts];

    /// <inheritdoc />
    public async Task<IRunningApplication> StartAsync(CancellationToken cancellation)
    {
        Interlocked.Increment(ref _started);
        var attempt = Attempt.Wrap(await _real.StartAsync(cancellation).ConfigureAwait(false));
        _attempts.Enqueue(attempt);
        return attempt;
    }

    /// <summary>Releases any server a test left running.</summary>
    /// <returns>A task that completes once every server is released.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach (Attempt attempt in _attempts)
        {
            await attempt.StopUnderneathAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Creates a host of real servers whose health probes are bounded only by their callers.</summary>
    /// <returns>The host.</returns>
    internal static RealApplicationHost Create() => new(new ApplicationServer.Host(new FakeTimeProvider()));

    /// <summary>One real server attempt; disposal reaches the real server exactly once, whoever asks first.</summary>
    internal sealed class Attempt : IRunningApplication
    {
        private readonly IRunningApplication _server;
        private readonly Lazy<Task> _release;
        private int _supervisorDisposed;

        private Attempt(IRunningApplication server)
        {
            _server = server;

            // The real host's stopping token, read while its services exist; the real token fires when the host stops.
            Stopping = server.Stopping;
            _release = new Lazy<Task>(() => server.DisposeAsync().AsTask(), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc />
        public Uri Address => _server.Address;

        /// <inheritdoc />
        public CancellationToken Stopping { get; }

        /// <summary>Gets a value indicating whether the supervisor has disposed this attempt.</summary>
        internal bool DisposedBySupervisor => Volatile.Read(ref _supervisorDisposed) == 1;

        /// <inheritdoc />
        public Task<bool> ProbeAsync(TimeSpan timeout, CancellationToken cancellation) => _server.ProbeAsync(timeout, cancellation);

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _supervisorDisposed, 1);
            await _release.Value.ConfigureAwait(false);
        }

        /// <summary>Wraps a started real server.</summary>
        /// <param name="server">The real server.</param>
        /// <returns>The attempt.</returns>
        internal static Attempt Wrap(IRunningApplication server) => new(server);

        /// <summary>Stops the real server underneath the supervisor: its listener closes and its host reports stopping.</summary>
        /// <returns>A task that completes once the real server has stopped.</returns>
        internal Task StopUnderneathAsync() => _release.Value;
    }
}
