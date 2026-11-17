// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Denice.Updates;

/// <summary>
/// One settings version <c>denice settings history</c> lists and <c>denice settings restore &lt;number&gt;</c> brings
/// back: numbers 1 and up are the saved copies of settings that loaded, newest first, and 0 is the original, every
/// setting at its default (Tim, pkg-d4e90c3ce176 broken-settings: "If last known good is original (reset) then that is
/// just version 0.").
/// </summary>
/// <param name="Number">The version's number: 1 is the newest saved copy, 0 is the original.</param>
/// <param name="Saved">When the copy was saved, in UTC; <see langword="null"/> for the original.</param>
/// <param name="Settings">The settings the version sets, in the order the settings are listed; empty means every
/// default.</param>
public sealed record SettingsVersion(int Number, DateTimeOffset? Saved, IReadOnlyList<SettingState> Settings)
{
    /// <summary>The number of the original version, every setting at its default; it is never stored as a file.</summary>
    public const int Original = 0;

    /// <summary>Gets when the version was saved, as shown: <c>saved 2026-11-17 09:30:00 UTC</c>, or <c>the original</c>.</summary>
    public string SavedDescription =>
        Saved is { } saved
            ? "saved " + saved.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
            : "the original";

    /// <summary>Gets the settings the version sets, as shown: <c>name: value, name: value</c>, or <c>every default</c>.</summary>
    public string SettingsDescription =>
        Settings.Count == 0
            ? "every default"
            : string.Join(", ", Settings.Select(setting => $"{setting.Name}: {setting.Value}"));

    /// <summary>
    /// Gets the version as one phrase: <c>version 1 (saved 2026-11-17 09:30:00 UTC; updates.channel: stable)</c>.
    /// </summary>
    public string Description => $"version {Number} ({SavedDescription}; {SettingsDescription})";
}
