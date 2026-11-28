// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Engine;

/// <summary>One running application server attempt.</summary>
internal interface IRunningApplication : IAsyncDisposable
{
    /// <summary>Gets the address the server listens on.</summary>
    Uri Address { get; }

    /// <summary>Gets a token cancelled when the server's host begins shutting down by itself.</summary>
    CancellationToken Stopping { get; }

    /// <summary>Asks the server's health endpoint whether it is ready.</summary>
    /// <param name="timeout">How long the probe may take.</param>
    /// <param name="cancellation">Cancels the probe.</param>
    /// <returns>Whether the server answered ready within the timeout.</returns>
    Task<bool> ProbeAsync(TimeSpan timeout, CancellationToken cancellation);
}
