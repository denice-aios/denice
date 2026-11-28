// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Engine;

/// <summary>Starts one application server attempt; the supervisor's only way to a server.</summary>
internal interface IApplicationHost
{
    /// <summary>Starts a server and returns once it listens.</summary>
    /// <param name="cancellation">Cancels the attempt.</param>
    /// <returns>The running server.</returns>
    Task<IRunningApplication> StartAsync(CancellationToken cancellation);
}
