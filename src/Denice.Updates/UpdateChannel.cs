// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Updates;

/// <summary>
/// The release channel Denice checks for updates (<c>updates.channel</c> in <c>settings.yaml</c>).
/// </summary>
public enum UpdateChannel
{
    /// <summary>
    /// No channel chosen (the default). Tim: "default pre-release while no stable release exists": pre-release builds as
    /// well as stable ones while GitHub's list of releases has no stable (non-pre-release, non-draft) release, then
    /// stable releases only.
    /// </summary>
    PreReleaseUntilStable,

    /// <summary>Pre-release builds as well as stable ones (<c>pre-release</c>).</summary>
    PreRelease,

    /// <summary>Stable releases only (<c>stable</c>).</summary>
    Stable,
}
