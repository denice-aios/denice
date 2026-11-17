// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Updates;

/// <summary>
/// What one update check did, with the version it installed or the reason it failed.
/// </summary>
/// <param name="Result">What the check did.</param>
/// <param name="Version">The installed version when <see cref="Result"/> is <see cref="UpdateResult.Installed"/>.</param>
/// <param name="Reason">Why the check failed when <see cref="Result"/> is <see cref="UpdateResult.Failed"/>.</param>
public sealed record UpdateOutcome(UpdateResult Result, string? Version, string? Reason)
{
    /// <summary>Gets the outcome of a check while updates are switched off.</summary>
    public static UpdateOutcome Disabled { get; } = new(UpdateResult.Disabled, null, null);

    /// <summary>Gets the outcome of a check in a version no loader started.</summary>
    public static UpdateOutcome NotInstalledByLoader { get; } = new(UpdateResult.NotInstalledByLoader, null, null);

    /// <summary>Gets the outcome of a check that found nothing newer.</summary>
    public static UpdateOutcome NoNewerVersion { get; } = new(UpdateResult.NoNewerVersion, null, null);

    /// <summary>The outcome of a check that installed a newer version.</summary>
    /// <param name="version">The installed version.</param>
    /// <returns>The outcome.</returns>
    public static UpdateOutcome Installed(string version) => new(UpdateResult.Installed, version, null);

    /// <summary>The outcome of a check that failed.</summary>
    /// <param name="reason">Why it failed.</param>
    /// <returns>The outcome.</returns>
    public static UpdateOutcome Failed(string reason) => new(UpdateResult.Failed, null, reason);
}
