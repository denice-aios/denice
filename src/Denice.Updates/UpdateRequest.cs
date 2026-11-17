// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Updates;

/// <summary>
/// Why an update check runs. Tim: "Check at every start in the background (never delaying the start) and every 6 hours
/// while running, with the interval and an off switch also in Settings (rule 7)." When a check runs is the caller's
/// decision; the kind decides only whether the off switch applies.
/// </summary>
public enum UpdateRequest
{
    /// <summary>
    /// The background check at every start, when updates are on. It never delays the command: a one-shot command does not
    /// wait for it, and when the command ends the check stops and the next launch tries again (pkg-79dc8dd1e15d,
    /// "retry-next-launch").
    /// </summary>
    AtStart,

    /// <summary>
    /// A check while a long-running session runs, once every <c>updates.checkIntervalHours</c>, when updates are on
    /// (<see cref="UpdateLoop"/> waits the interval; the check itself does not look at it).
    /// </summary>
    WhileRunning,

    /// <summary>
    /// <c>denice update</c>: checks now whatever the off switch says. Tim (pkg-79dc8dd1e15d): "We'll also add an "update"
    /// command to allow the user to manually override.".
    /// </summary>
    Manual,
}
