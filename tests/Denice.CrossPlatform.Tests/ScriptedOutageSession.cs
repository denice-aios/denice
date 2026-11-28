// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// An application session that follows a fixed outage script after it starts: the page is ready, then the server is
/// unavailable, then ready again at the same address with a new revision, the way the supervisor publishes a restart.
/// </summary>
internal sealed class ScriptedOutageSession : IApplicationSession
{
    /// <summary>The status the scripted outage shows.</summary>
    internal const string OutageStatus = "The DENICE server stopped. Restarting it.";

    private readonly Uri page;
    private readonly TimeProvider clock;
    private readonly TimeSpan outageAt;
    private readonly TimeSpan restartAt;
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long started = -1;

    private ScriptedOutageSession(Uri page, TimeProvider clock, TimeSpan outageAt, TimeSpan restartAt)
    {
        this.page = page;
        this.clock = clock;
        this.outageAt = outageAt;
        this.restartAt = restartAt;
    }

    /// <inheritdoc />
    public Uri StartupPage { get; } = new("data:text/html,%3C!doctype%20html%3E%3Ctitle%3EDENICE%3C%2Ftitle%3E");

    /// <inheritdoc />
    public ApplicationPageState State
    {
        get
        {
            long start = Interlocked.Read(ref started);
            if (start < 0 || stopped.Task.IsCompleted)
            {
                return new ApplicationPageState(0, null, null);
            }

            TimeSpan elapsed = clock.GetElapsedTime(start);
            if (elapsed < outageAt)
            {
                return new ApplicationPageState(1, page, null);
            }

            return elapsed < restartAt ? new ApplicationPageState(2, null, OutageStatus) : new ApplicationPageState(3, page, null);
        }
    }

    /// <inheritdoc />
    public Task Stopped => stopped.Task;

    /// <inheritdoc />
    public void Start() => Interlocked.CompareExchange(ref started, clock.GetTimestamp(), -1);

    /// <inheritdoc />
    public void RequestStop() => stopped.TrySetResult();

    /// <summary>Creates the scripted session.</summary>
    /// <param name="page">The application page.</param>
    /// <param name="clock">The clock the script follows.</param>
    /// <param name="outageAt">When, after start, the server becomes unavailable.</param>
    /// <param name="restartAt">When, after start, it is ready again with a new revision.</param>
    /// <returns>The session.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Contract fakes are handed back through their interface (DN0004).")]
    internal static IApplicationSession Create(Uri page, TimeProvider clock, TimeSpan outageAt, TimeSpan restartAt) => new ScriptedOutageSession(page, clock, outageAt, restartAt);
}
