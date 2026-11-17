// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.IO;
using System.Linq;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Denice.Runtime.Tests;

/// <summary>
/// The settings commands end to end through <c>Program.Run</c>, against a temporary home that stands in for the user's
/// (never the real user folder): <c>denice settings</c>, <c>denice settings set &lt;name&gt; &lt;value&gt;</c>,
/// <c>denice settings reset &lt;name&gt;</c>, <c>denice settings history</c>, and <c>denice settings restore
/// &lt;number&gt;</c>, and a broken file restored to the last known good (pkg-d4e90c3ce176 broken-settings).
/// </summary>
public sealed class SettingsCliTests : IDisposable
{
    private readonly ISystemServices real;
    private readonly string home;

    /// <summary>Initializes a new instance of the <see cref="SettingsCliTests"/> class over a fresh temporary home.</summary>
    public SettingsCliTests()
    {
        real = SystemServicesBuilder.Real().Build();
        home = real.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-settings-cli-");
    }

    [Fact]
    public void Settings_WithNoFile_ListsEverySettingWithItsDefault()
    {
        CliResult result = CliRunner.RunInHome(home, "settings");

        result.ExitCode.Should().Be(0, result.StandardError);
        result.StandardOutput.Should().Contain($"Settings file: {home}")
            .And.Contain("settings.yaml")
            .And.Contain("updates.enabled: true (default: true)")
            .And.Contain("updates.channel: pre-release while no stable release exists, then stable (default: pre-release while no stable release exists, then stable)")
            .And.Contain("updates.checkIntervalHours: 6 (default: 6)");
    }

    [Fact]
    public void SettingsSet_ChangesTheValueTheListShows()
    {
        CliResult set = CliRunner.RunInHome(home, "settings", "set", "updates.channel", "stable");
        CliResult list = CliRunner.RunInHome(home, "settings");

        set.ExitCode.Should().Be(0, set.StandardError);
        set.StandardOutput.Should().Contain("updates.channel: stable (saved to ");
        list.StandardOutput.Should().Contain("updates.channel: stable (default: pre-release while no stable release exists, then stable)");
    }

    [Fact]
    public void SettingsReset_ReturnsTheSettingToItsDefault()
    {
        CliRunner.RunInHome(home, "settings", "set", "updates.checkIntervalHours", "24");

        CliResult reset = CliRunner.RunInHome(home, "settings", "reset", "updates.checkIntervalHours");
        CliResult list = CliRunner.RunInHome(home, "settings");

        reset.ExitCode.Should().Be(0, reset.StandardError);
        reset.StandardOutput.Should().Contain("updates.checkIntervalHours: 6 (the default; saved to ");
        list.StandardOutput.Should().Contain("updates.checkIntervalHours: 6 (default: 6)");
    }

    [Fact]
    public void SettingsSet_OfAnInvalidValue_FailsWithTheReason()
    {
        CliResult result = CliRunner.RunInHome(home, "settings", "set", "updates.enabled", "maybe");

        result.ExitCode.Should().Be(1);
        result.StandardError.Should().Contain("'maybe' is not a valid value for updates.enabled; use true or false.");
    }

    [Fact]
    public void SettingsReset_OfAnUnknownName_FailsNamingTheSettings()
    {
        CliResult result = CliRunner.RunInHome(home, "settings", "reset", "updates.colour");

        result.ExitCode.Should().Be(1);
        result.StandardError.Should().Contain("'updates.colour' is not a setting; the settings are updates.enabled");
    }

    [Fact]
    public void Settings_WithABrokenFile_RestoresTheLastKnownGoodAndSaysSo()
    {
        CliRunner.RunInHome(home, "settings", "set", "updates.enabled", "false");
        CliRunner.RunInHome(home, "settings");
        string file = SettingsFilePath();
        real.FileSystem.GetFileWriter().WriteAllText(file, "updates.enabled: false\nupdates.channel: [stable]\n");

        CliResult result = CliRunner.RunInHome(home, "settings");

        result.ExitCode.Should().Be(0, result.StandardError);
        result.StandardError.Should().StartWith($"The settings file was broken: {file}, line 2: flow style")
            .And.Contain("Denice restored settings version 1 (saved ")
            .And.Contain($"UTC; updates.enabled: false); the broken file is kept at {file}.broken.");
        result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle();
        result.StandardOutput.Should().Contain("updates.enabled: false (default: true)");
        real.FileSystem.GetFileReader().ReadAllText(file + ".broken").Should().Contain("[stable]");
    }

    [Fact]
    public void SettingsSetAndReset_WorkAfterABreakage()
    {
        CliRunner.RunInHome(home, "settings", "set", "updates.enabled", "false");
        string file = SettingsFilePath();
        real.FileSystem.GetFileWriter().WriteAllText(file, "updates:\n  enabled: false\n");

        CliResult set = CliRunner.RunInHome(home, "settings", "set", "updates.channel", "stable");
        CliRunner.RunInHome(home, "settings");
        real.FileSystem.GetFileWriter().WriteAllText(file, "updates:\n  enabled: false\n");
        CliResult reset = CliRunner.RunInHome(home, "settings", "reset", "updates.channel");

        set.ExitCode.Should().Be(0, set.StandardError);
        set.StandardError.Should().Contain("Denice restored settings version 0 (the original; every default)");
        reset.ExitCode.Should().Be(0, reset.StandardError);
        reset.StandardError.Should().Contain("Denice restored settings version 1 (saved ");
        reset.StandardOutput.Should().Contain("updates.channel: pre-release while no stable release exists, then stable (the default; saved to ");
    }

    [Fact]
    public void SettingsHistory_ListsTheSavedVersionsNewestFirst_ThenVersion0()
    {
        CliRunner.RunInHome(home, "settings", "set", "updates.channel", "stable");
        CliRunner.RunInHome(home, "settings", "set", "updates.checkIntervalHours", "24");

        CliResult result = CliRunner.RunInHome(home, "settings", "history");

        result.ExitCode.Should().Be(0, result.StandardError);
        string[] lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();
        lines.Should().HaveCount(4);
        lines[0].Should().Be($"Settings history for {SettingsFilePath()}, newest first (restore one with: denice settings restore <number>):");
        lines[1].Should().MatchRegex(@"^1  saved \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} UTC  updates\.channel: stable, updates\.checkIntervalHours: 24$");
        lines[2].Should().MatchRegex(@"^2  saved .* UTC  updates\.channel: stable$");
        lines[3].Should().Be("0  the original  every default");
    }

    [Fact]
    public void SettingsRestore_OfASavedVersion_MakesItCurrent()
    {
        CliRunner.RunInHome(home, "settings", "set", "updates.channel", "stable");
        CliRunner.RunInHome(home, "settings", "set", "updates.channel", "pre-release");

        CliResult restore = CliRunner.RunInHome(home, "settings", "restore", "2");
        CliResult list = CliRunner.RunInHome(home, "settings");

        restore.ExitCode.Should().Be(0, restore.StandardError);
        restore.StandardOutput.Should().MatchRegex(@"^Restored settings version 2 \(saved .* UTC; updates\.channel: stable\) to .*settings\.yaml\r?\n$");
        list.StandardOutput.Should().Contain("updates.channel: stable (default: ");
    }

    [Fact]
    public void SettingsRestore0_ReturnsEverySettingToItsDefault()
    {
        CliRunner.RunInHome(home, "settings", "set", "updates.enabled", "false");

        CliResult restore = CliRunner.RunInHome(home, "settings", "restore", "0");
        CliResult list = CliRunner.RunInHome(home, "settings");

        restore.ExitCode.Should().Be(0, restore.StandardError);
        restore.StandardOutput.Should().StartWith("Restored settings version 0 (the original; every default) to ");
        list.StandardOutput.Should().Contain("updates.enabled: true (default: true)");
    }

    [Fact]
    public void SettingsRestore_OfAVersionThatDoesNotExist_FailsNamingTheVersions()
    {
        CliRunner.RunInHome(home, "settings", "set", "updates.enabled", "false");

        CliResult result = CliRunner.RunInHome(home, "settings", "restore", "5");

        result.ExitCode.Should().Be(1);
        result.StandardError.Should().Contain("There is no settings version 5; the versions are 0 to 1 (see 'denice settings history').");
    }

    public void Dispose() => real.FileSystem.GetDirectoryWriter().DeleteDirectory(home, recursive: true);

    // The one settings.yaml under the temporary home, wherever this OS's per-user folder puts it (on Linux inside the
    // hidden ~/.config, so hidden folders are searched too).
    private string SettingsFilePath() =>
        real.FileSystem.GetDirectoryReader()
            .EnumerateFiles(home, "settings.yaml", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.None })
            .Should().ContainSingle().Subject;
}
