// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Updates;

/// <summary>
/// What one update check did.
/// </summary>
public enum UpdateResult
{
    /// <summary>Updates are switched off in Settings (<c>updates.enabled</c>); nothing was checked.</summary>
    Disabled,

    /// <summary>This version was not started by a loader (a developer run), so there is nothing to update.</summary>
    NotInstalledByLoader,

    /// <summary>The channel has no version newer than the running one.</summary>
    NoNewerVersion,

    /// <summary>A newer version is installed and ready for the next start.</summary>
    Installed,

    /// <summary>The check or the download failed; the next check tries again.</summary>
    Failed,
}
