// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>Supervises the application server: one attempt at a time, retried with back-off until stopped.</summary>
public interface IApplicationSession
{
    /// <summary>Gets the bundled status page, a data URL that needs no server.</summary>
    Uri StartupPage { get; }

    /// <summary>Gets the current snapshot.</summary>
    ApplicationPageState State { get; }

    /// <summary>Gets a task that completes once the supervisor and server have released their resources.</summary>
    Task Stopped { get; }

    /// <summary>Starts supervision; thread-safe, nonblocking and idempotent; no effect after a stop.</summary>
    void Start();

    /// <summary>Cancels start-up, probes and retries at once; thread-safe, nonblocking and idempotent.</summary>
    void RequestStop();
}
