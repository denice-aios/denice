// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Denice.Abstractions.Contracts;

namespace Denice.Updates;

/// <summary>
/// The update checks of a long-running Denice session. Tim: "Check at every start in the background (never delaying the
/// start) and every 6 hours while running, with the interval and an off switch also in Settings (rule 7). A ready newer
/// version triggers the decided restart." The start check is <see cref="UpdateRequest.AtStart"/>, run at every launch;
/// this loop does the checks while running: it waits <c>updates.checkIntervalHours</c> (read again before every wait,
/// so a change in Settings takes effect at the next wait) on the injected clock, checks once
/// (<see cref="UpdateRequest.WhileRunning"/>, which honours the off switch), and repeats until a newer version is
/// installed. A one-shot command never runs it: it finishes once, and a newer version is used from the next launch.
/// </summary>
public sealed class UpdateLoop
{
    private readonly ISystemServices _services;
    private readonly UpdateService _updates;
    private readonly Action<TimeSpan>? _waiting;

    /// <summary>Initializes a new instance of the <see cref="UpdateLoop"/> class.</summary>
    /// <param name="services">The owned services the settings and the clock come from.</param>
    /// <param name="updates">The update service each check goes through; the caller owns and disposes it.</param>
    public UpdateLoop(ISystemServices services, UpdateService updates)
        : this(services, updates, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateLoop"/> class that reports each wait as it starts (tests use
    /// it to know the wait is on the clock before they move the clock on).
    /// </summary>
    /// <param name="services">The owned services.</param>
    /// <param name="updates">The update service each check goes through.</param>
    /// <param name="waiting">Called with the interval once each wait is on the clock; null for none.</param>
    internal UpdateLoop(ISystemServices services, UpdateService updates, Action<TimeSpan>? waiting)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(updates);
        _services = services;
        _updates = updates;
        _waiting = waiting;
    }

    /// <summary>
    /// Checks every <c>updates.checkIntervalHours</c> until a check installs a newer version. A failed check is reported
    /// on standard error with its reason and retried at the next interval; while updates are switched off nothing is
    /// checked.
    /// </summary>
    /// <param name="ct">Ends the loop, including a check under way.</param>
    /// <returns>The installed version and the exit code the session ends with, or <see langword="null"/> when
    /// <paramref name="ct"/> ended the loop.</returns>
    public Task<NewerVersionReady?> RunUntilNewerVersionAsync(CancellationToken ct) => RunUntilNewerVersionAsync(null, ct);

    /// <summary>
    /// Waits for the check at this start first, then checks every <c>updates.checkIntervalHours</c>, until a check installs
    /// (or finds installed) a newer version. The start check's outcome counts like any other: a newer version it installed
    /// ends the loop as soon as it finishes, and a failure is reported and the interval checks go on. A failed check is
    /// reported on standard error with its reason and retried at the next interval; while updates are switched off nothing
    /// is checked.
    /// </summary>
    /// <param name="startCheck">The update check at this start (<see cref="UpdateRequest.AtStart"/>), under way in the
    /// background; null when none runs. The loop only waits for it; its owner runs and cancels it.</param>
    /// <param name="ct">Ends the loop, including a check under way.</param>
    /// <returns>The installed version and the exit code the session ends with, or <see langword="null"/> when
    /// <paramref name="ct"/> ended the loop.</returns>
    public async Task<NewerVersionReady?> RunUntilNewerVersionAsync(Task<UpdateOutcome>? startCheck, CancellationToken ct)
    {
        try
        {
            if (startCheck is not null && Ready(await UntilCancelledAsync(startCheck, ct).ConfigureAwait(false)) is { } fromStart)
            {
                return fromStart;
            }

            while (true)
            {
                TimeSpan interval = Interval();
                Task wait = Task.Delay(interval, _services.Clock, ct);
                _waiting?.Invoke(interval);
                await wait.ConfigureAwait(false);
                UpdateOutcome outcome = await _updates.CheckAndInstallAsync(UpdateRequest.WhileRunning, null, ct).ConfigureAwait(false);
                if (Ready(outcome) is { } ready)
                {
                    return ready;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
    }

    // Waits for a task its owner runs and cancels, ending early when ct is cancelled.
    private static async Task<T> UntilCancelledAsync<T>(Task<T> task, CancellationToken ct)
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ct.Register(() => cancelled.TrySetCanceled(ct)))
        {
            if (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false) != task)
            {
                ct.ThrowIfCancellationRequested();
            }

            return await task.ConfigureAwait(false);
        }
    }

    // The one place a check's outcome becomes "a newer version is ready" (or a reported failure).
    private NewerVersionReady? Ready(UpdateOutcome outcome)
    {
        if (outcome is { Result: UpdateResult.Installed, Version: { } version })
        {
            return new NewerVersionReady(version);
        }

        if (outcome.Result == UpdateResult.Failed)
        {
            _services.Console.ErrorWriteLine($"The update check failed; Denice checks again at the next interval. {outcome.Reason}");
        }

        return null;
    }

    // The interval as Settings now say (a broken settings file is restored to the last known good as it is read); a
    // settings folder that cannot be read means the default, and the check itself then fails with the reason and is
    // retried at the next interval.
    private TimeSpan Interval()
    {
        try
        {
            return UpdateSettings.Read(_services).CheckInterval;
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return UpdateSettings.Defaults.CheckInterval;
        }
    }
}
