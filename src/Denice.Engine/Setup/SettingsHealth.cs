// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Engine.Setup;

/// <summary>
/// The health of the guard's hook entries in <c>.claude/settings.json</c>, as detected by <c>doctor</c>.
/// </summary>
internal enum SettingsHealth
{
    /// <summary>
    /// Every denice hook entry is present and points at the current absolute launcher path.
    /// </summary>
    Ok,

    /// <summary>
    /// One or more denice hook entries are absent.
    /// </summary>
    Missing,

    /// <summary>
    /// A denice hook entry is present but points at a stale path.
    /// </summary>
    Stale,

    /// <summary>
    /// The settings file could not be parsed or has a shape that cannot be evaluated.
    /// </summary>
    Malformed,
}
