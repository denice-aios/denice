// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Denice.Abstractions.Contracts;

namespace Denice.Updates;

/// <summary>
/// The update settings, typed, as the update checks read them: <c>updates.enabled</c>, <c>updates.channel</c>, and
/// <c>updates.checkIntervalHours</c> from the per-user <c>settings.yaml</c>, each falling back to its default when the
/// file does not set it.
/// </summary>
/// <param name="Enabled">Whether Denice checks for updates at all (<c>updates.enabled</c>, default <c>true</c>).</param>
/// <param name="Channel">The release channel checked (<c>updates.channel</c>; when nothing sets it,
/// <see cref="UpdateChannel.PreReleaseUntilStable"/>).</param>
/// <param name="CheckInterval">How long a running session waits between checks (<c>updates.checkIntervalHours</c>,
/// default 6 hours).</param>
public sealed record UpdateSettings(bool Enabled, UpdateChannel Channel, TimeSpan CheckInterval)
{
    /// <summary>Gets the settings when nothing sets any of them.</summary>
    public static UpdateSettings Defaults { get; } = From(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// Reads the update settings from the per-user <c>settings.yaml</c>; a missing file means every default, and a broken
    /// file is replaced by the last known good settings (<see cref="SettingsFile"/>).
    /// </summary>
    /// <param name="services">The owned services the settings folder and file are reached through.</param>
    /// <returns>The update settings.</returns>
    public static UpdateSettings Read(ISystemServices services) => new SettingsFile(services).ReadUpdateSettings();

    /// <summary>Builds the typed settings from the values the settings file sets, each already validated.</summary>
    /// <param name="set">The value of every setting the file sets, by name; a setting it does not set has its default.</param>
    /// <returns>The typed settings.</returns>
    internal static UpdateSettings From(IReadOnlyDictionary<string, string> set)
    {
        bool enabled = string.Equals(Value(set, SettingDefinition.EnabledName), SettingDefinition.True, StringComparison.Ordinal);
        UpdateChannel channel = set.GetValueOrDefault(SettingDefinition.ChannelName) switch
        {
            null => UpdateChannel.PreReleaseUntilStable,
            SettingDefinition.Stable => UpdateChannel.Stable,
            _ => UpdateChannel.PreRelease,
        };
        int hours = int.Parse(Value(set, SettingDefinition.CheckIntervalHoursName), NumberStyles.None, CultureInfo.InvariantCulture);
        return new UpdateSettings(enabled, channel, TimeSpan.FromHours(hours));
    }

    private static string Value(IReadOnlyDictionary<string, string> set, string name) =>
        set.TryGetValue(name, out string? value) ? value : SettingDefinition.Find(name)!.DefaultValue;
}
