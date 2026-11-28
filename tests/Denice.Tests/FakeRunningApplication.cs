// Copyright (c) Denice.ai. All rights reserved.

using System.Globalization;
using Denice.Engine;

namespace Denice.Tests;

/// <summary>A scripted running server: it can report shutting down, stop answering, or never finish teardown.</summary>
internal sealed class FakeRunningApplication : IRunningApplication
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _teardown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _healthy = true;
    private int _disposed;

    private FakeRunningApplication(Uri address, bool teardownHangs)
    {
        Address = address;
        if (!teardownHangs)
        {
            _teardown.SetResult();
        }
    }

    /// <inheritdoc />
    public Uri Address { get; }

    /// <inheritdoc />
    public CancellationToken Stopping => _stopping.Token;

    /// <summary>Gets a value indicating whether the supervisor began disposing this server.</summary>
    internal bool Disposed => Volatile.Read(ref _disposed) == 1;

    /// <inheritdoc />
    public Task<bool> ProbeAsync(TimeSpan timeout, CancellationToken cancellation) => Task.FromResult(_healthy);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _teardown.Task.ConfigureAwait(false);
        _stopping.Dispose();
    }

    /// <summary>Creates a server.</summary>
    /// <param name="port">The loopback port in its address.</param>
    /// <param name="teardownHangs">Whether disposal waits for <see cref="ReleaseTeardown"/>.</param>
    /// <returns>The server.</returns>
    internal static FakeRunningApplication Create(int port, bool teardownHangs) =>
        new(new Uri(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}/")), teardownHangs);

    /// <summary>Makes the host report that it is shutting down.</summary>
    internal void Stop() => _stopping.Cancel();

    /// <summary>Makes later health probes fail.</summary>
    internal void StopAnswering() => _healthy = false;

    /// <summary>Lets a hanging teardown finish.</summary>
    internal void ReleaseTeardown() => _teardown.TrySetResult();
}
