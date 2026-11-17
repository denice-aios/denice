// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Updates;

/// <summary>
/// The release channel Denice checks for updates (<c>updates.channel</c> in <c>settings.yaml</c>).
/// </summary>
public enum UpdateChannel
{
    /// <summary>Pre-release builds as well as stable ones (<c>pre-release</c>, the default).</summary>
    PreRelease,

    /// <summary>Stable releases only (<c>stable</c>).</summary>
    Stable,
}
