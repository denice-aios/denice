// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Updates;

/// <summary>
/// The result of installing a release.
/// </summary>
public enum InstallResult
{
    /// <summary>The version was checked and moved into <c>versions/&lt;version&gt;</c>.</summary>
    Installed,

    /// <summary>The version was already installed; nothing changed.</summary>
    AlreadyInstalled,
}
