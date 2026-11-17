// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Denice.Updates;

/// <summary>
/// One setting Denice knows: its name, its default, and which values it accepts. Settings are deliberately few and
/// temporary (they move to the database later), so there are exactly three and every value is a single plain word.
/// </summary>
internal sealed class SettingDefinition
{
    /// <summary>The name of the setting that turns update checks on or off.</summary>
    internal const string EnabledName = "updates.enabled";

    /// <summary>The name of the setting that picks the release channel.</summary>
    internal const string ChannelName = "updates.channel";

    /// <summary>The name of the setting that sets the hours between update checks.</summary>
    internal const string CheckIntervalHoursName = "updates.checkIntervalHours";

    /// <summary>The <c>updates.enabled</c> value that turns update checks on.</summary>
    internal const string True = "true";

    /// <summary>The <c>updates.channel</c> value for stable releases only.</summary>
    internal const string Stable = "stable";

    /// <summary>
    /// What <c>updates.channel</c> does when nothing sets it (<see cref="UpdateChannel.PreReleaseUntilStable"/>), as
    /// <c>denice settings</c> shows it. Tim: "default pre-release while no stable release exists". It describes a
    /// behaviour, not a value the setting accepts: the setting is either set to one of its values or left to this.
    /// </summary>
    internal const string ChannelDefault = "pre-release while no stable release exists, then stable";

    /// <summary>
    /// The most hours <c>updates.checkIntervalHours</c> accepts: 720 hours is 30 days. A longer wait is in practice
    /// "never check", which <c>updates.enabled: false</c> already says plainly, and a bound keeps the value far from
    /// any overflow when it becomes a <see cref="TimeSpan"/>.
    /// </summary>
    internal const int MaxCheckIntervalHours = 720;

    private const string False = "false";
    private const string PreRelease = "pre-release";

    private readonly Func<string, bool> _accepts;

    private SettingDefinition(string name, string defaultValue, string allowed, Func<string, bool> accepts)
    {
        Name = name;
        DefaultValue = defaultValue;
        Allowed = allowed;
        _accepts = accepts;
    }

    /// <summary>Gets every setting, in the order they are listed and written.</summary>
    internal static IReadOnlyList<SettingDefinition> All { get; } =
    [
        new(EnabledName, True, $"{True} or {False}", value => value is True or False),
        new(ChannelName, ChannelDefault, $"{PreRelease} or {Stable}", value => value is PreRelease or Stable),
        new(
            CheckIntervalHoursName,
            "6",
            $"a whole number of hours from 1 to {MaxCheckIntervalHours.ToString(CultureInfo.InvariantCulture)}",
            IsCheckIntervalHours),
    ];

    /// <summary>Gets the setting's name, as written in <c>settings.yaml</c> and on the command line.</summary>
    internal string Name { get; }

    /// <summary>Gets the value the setting has when nothing sets it, or for <c>updates.channel</c> what it does then.</summary>
    internal string DefaultValue { get; }

    /// <summary>Gets a description of the values the setting accepts, for messages.</summary>
    internal string Allowed { get; }

    /// <summary>Finds the setting with exactly this name (names are case-sensitive).</summary>
    /// <param name="name">The name to look up.</param>
    /// <returns>The setting, or <see langword="null"/> when no setting has that name.</returns>
    internal static SettingDefinition? Find(string name) =>
        All.FirstOrDefault(setting => string.Equals(setting.Name, name, StringComparison.Ordinal));

    /// <summary>Describes a name that is not a setting, listing the names that are.</summary>
    /// <param name="name">The unknown name.</param>
    /// <returns>The problem, for a message.</returns>
    internal static string UnknownNameProblem(string name) =>
        $"'{name}' is not a setting; the settings are {string.Join(", ", All.Select(setting => setting.Name))}";

    /// <summary>Gets a value indicating whether the setting accepts <paramref name="value"/> exactly as written.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> when the value is valid for this setting.</returns>
    internal bool Accepts(string value) => _accepts(value);

    /// <summary>Describes a value this setting does not accept, naming what it does accept.</summary>
    /// <param name="value">The refused value.</param>
    /// <returns>The problem, for a message.</returns>
    internal string InvalidValueProblem(string value) => $"'{value}' is not a valid value for {Name}; use {Allowed}";

    // A whole number written in plain decimal digits with no sign and no leading zero (so every YAML reader reads the
    // same integer: YAML 1.1 reads a leading-zero number as octal), from 1 to the bound.
    private static bool IsCheckIntervalHours(string value) =>
        value.Length is > 0 and <= 3
        && value[0] != '0'
        && value.All(char.IsAsciiDigit)
        && int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture) <= MaxCheckIntervalHours;
}
