// Copyright (c) Denice.ai. All rights reserved.

using System.Collections.Concurrent;
using Denice.Engine;

namespace Denice.Tests;

/// <summary>
/// Starts scripted server attempts for supervisor tests: each attempt fails, hangs until cancelled, or returns a fake
/// server that this host owns and disposes with itself.
/// </summary>
internal sealed class FakeApplicationHost : IApplicationHost, IAsyncDisposable
{
    private readonly ConcurrentQueue<Func<CancellationToken, Task<IRunningApplication>>> _attempts = new();
    private readonly ConcurrentBag<FakeRunningApplication> _servers = [];
    private int _started;

    private FakeApplicationHost()
    {
    }

    /// <summary>Gets the number of start attempts so far.</summary>
    internal int Started => Volatile.Read(ref _started);

    /// <inheritdoc />
    public Task<IRunningApplication> StartAsync(CancellationToken cancellation)
    {
        Interlocked.Increment(ref _started);
        return _attempts.TryDequeue(out Func<CancellationToken, Task<IRunningApplication>>? attempt)
            ? attempt(cancellation)
            : Task.FromException<IRunningApplication>(new IOException("no scripted attempt"));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (FakeRunningApplication server in _servers)
        {
            server.ReleaseTeardown();
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Creates a host with no scripted attempts; unscripted attempts fail.</summary>
    /// <returns>The host.</returns>
    internal static FakeApplicationHost Create() => new();

    /// <summary>Scripts the next attempt to fail.</summary>
    /// <param name="message">The failure.</param>
    internal void Fails(string message) => _attempts.Enqueue(_ => Task.FromException<IRunningApplication>(new IOException(message)));

    /// <summary>Scripts the next attempt to return a healthy server.</summary>
    /// <param name="port">The loopback port in its address.</param>
    /// <param name="teardownHangs">Whether its disposal never finishes until the host is disposed.</param>
    /// <returns>The scripted server, owned by this host.</returns>
    internal FakeRunningApplication Serves(int port, bool teardownHangs = false)
    {
        FakeRunningApplication server = FakeRunningApplication.Create(port, teardownHangs);
        _servers.Add(server);
        _attempts.Enqueue(_ => Task.FromResult<IRunningApplication>(server));
        return server;
    }

    /// <summary>Scripts the next attempt to wait until it is cancelled.</summary>
    internal void Hangs() => _attempts.Enqueue(async cancellation =>
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (cancellation.Register(() => cancelled.TrySetCanceled(cancellation)).ConfigureAwait(false))
        {
            await cancelled.Task.ConfigureAwait(false);
        }

        throw new InvalidOperationException("A cancelled start cannot return a server.");
    });
}
