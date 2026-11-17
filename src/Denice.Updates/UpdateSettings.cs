// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Denice.Abstractions.Contracts;

namespace Denice.Updates;

/// <summary>
/// The update settings, typed, as the update scheduler reads them: <c>updates.enabled</c>, <c>updates.channel</c>, and
/// <c>updates.checkIntervalHours</c> from the per-user <c>settings.yaml</c>, each falling back to its default when the
/// file does not set it.
/// </summary>
/// <param name="Enabled">Whether Denice checks for updates at all (<c>updates.enabled</c>, default <c>true</c>).</param>
/// <param name="Channel">The release channel checked (<c>updates.channel</c>, default <c>pre-release</c>).</param>
/// <param name="CheckInterval">How long to wait between checks (<c>updates.checkIntervalHours</c>, default 6 hours).</param>
public sealed record UpdateSettings(bool Enabled, UpdateChannel Channel, TimeSpan CheckInterval)
{
    /// <summary>
    /// Reads the update settings from the per-user <c>settings.yaml</c>; a missing file means every default.
    /// </summary>
    /// <param name="services">The owned services the settings folder and file are reached through.</param>
    /// <returns>The update settings.</returns>
    /// <exception cref="System.IO.InvalidDataException">The settings file is not valid; the message names the line.</exception>
    public static UpdateSettings Read(ISystemServices services) => new SettingsFile(services).ReadUpdateSettings();

    /// <summary>Builds the typed settings from one already-validated value per setting.</summary>
    /// <param name="values">The effective value of every setting, by name.</param>
    /// <returns>The typed settings.</returns>
    internal static UpdateSettings From(IReadOnlyDictionary<string, string> values)
    {
        bool enabled = string.Equals(values[SettingDefinition.EnabledName], SettingDefinition.True, StringComparison.Ordinal);
        UpdateChannel channel = string.Equals(values[SettingDefinition.ChannelName], SettingDefinition.Stable, StringComparison.Ordinal)
            ? UpdateChannel.Stable
            : UpdateChannel.PreRelease;
        int hours = int.Parse(values[SettingDefinition.CheckIntervalHoursName], NumberStyles.None, CultureInfo.InvariantCulture);
        return new UpdateSettings(enabled, channel, TimeSpan.FromHours(hours));
    }
}
