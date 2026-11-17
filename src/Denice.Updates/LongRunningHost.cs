// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Denice.Updates;

/// <summary>
/// Runs one long-running command alongside the update checks of a long-running session (D6: "A long-running Denice
/// session checks every interval while it runs and restarts into a ready newer version (exit 75)"). The command and the
/// checks share one cancellation: when a check installs a newer version the command is stopped and the session ends
/// with <see cref="NewerVersionReady.ExitCode"/>, so the loader starts the newer version; when the command ends first its
/// exit code wins and the checks stop. A one-shot command never runs here: it finishes once (D5).
/// </summary>
public sealed class LongRunningHost
{
    private readonly Func<CancellationToken, Task<NewerVersionReady?>> _newerVersion;

    /// <summary>
    /// Initializes a new instance of the <see cref="LongRunningHost"/> class over the update checks of
    /// <paramref name="updates"/>, which waits <c>updates.checkIntervalHours</c> and honours the off switch.
    /// </summary>
    /// <param name="updates">The update loop the session's checks run in.</param>
    public LongRunningHost(UpdateLoop updates)
        : this(updates is null ? throw new ArgumentNullException(nameof(updates)) : updates.RunUntilNewerVersionAsync)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="LongRunningHost"/> class.</summary>
    /// <param name="newerVersion">Watches for a newer version until one is ready, or returns <see langword="null"/>
    /// once its token is cancelled (the contract of <see cref="UpdateLoop.RunUntilNewerVersionAsync(Task{UpdateOutcome}, CancellationToken)"/>).</param>
    public LongRunningHost(Func<CancellationToken, Task<NewerVersionReady?>> newerVersion)
    {
        ArgumentNullException.ThrowIfNull(newerVersion);
        _newerVersion = newerVersion;
    }

    /// <summary>
    /// Runs <paramref name="command"/> until it ends, or until a newer version is ready.
    /// </summary>
    /// <param name="command">The long-running command. It must end when its token is cancelled: that is how it is
    /// stopped for a newer version.</param>
    /// <param name="ct">Ends the session (e.g. Ctrl-C): the command and the checks both see it.</param>
    /// <returns>The command's own exit code when it ends first; <see cref="NewerVersionReady.ExitCode"/> when a newer
    /// version is ready first.</returns>
    public async Task<int> RunAsync(Func<CancellationToken, Task<int>> command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<NewerVersionReady?> watching = StartAsync(_newerVersion, session.Token);
        Task<int> running = StartAsync(command, session.Token);
        try
        {
            NewerVersionReady? ready = await Task.WhenAny(running, watching).ConfigureAwait(false) == watching
                ? await watching.ConfigureAwait(false)
                : null;
            if (ready is null)
            {
                return await running.ConfigureAwait(false);
            }

            await session.CancelAsync().ConfigureAwait(false);
            try
            {
                _ = await running.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested)
            {
                // Stopped for the newer version.
            }

            return ready.ExitCode;
        }
        finally
        {
            // Neither outlives the session, whichever way it ended. Waiting on WhenAny of the pair never throws: an outcome
            // that matters was already awaited above.
            await session.CancelAsync().ConfigureAwait(false);
            await Task.WhenAny(Task.WhenAll(running, watching)).ConfigureAwait(false);
        }
    }

    // Runs the delegate as a task, so a delegate that throws before its first await fails the task instead of the host.
    private static async Task<T> StartAsync<T>(Func<CancellationToken, Task<T>> start, CancellationToken ct) =>
        await start(ct).ConfigureAwait(false);
}
