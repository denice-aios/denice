// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.IO;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Denice.Runtime.Tests;

/// <summary>
/// The three settings commands end to end through <c>Program.Run</c>, against a temporary home that stands in for the
/// user's (never the real user folder): <c>denice settings</c>, <c>denice settings set &lt;name&gt; &lt;value&gt;</c>, and
/// <c>denice settings reset &lt;name&gt;</c>.
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
            .And.Contain("updates.channel: pre-release (default: pre-release)")
            .And.Contain("updates.checkIntervalHours: 6 (default: 6)");
    }

    [Fact]
    public void SettingsSet_ChangesTheValueTheListShows()
    {
        CliResult set = CliRunner.RunInHome(home, "settings", "set", "updates.channel", "stable");
        CliResult list = CliRunner.RunInHome(home, "settings");

        set.ExitCode.Should().Be(0, set.StandardError);
        set.StandardOutput.Should().Contain("updates.channel: stable (saved to ");
        list.StandardOutput.Should().Contain("updates.channel: stable (default: pre-release)");
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
    public void Settings_WithAnInvalidFile_FailsNamingTheLine()
    {
        CliRunner.RunInHome(home, "settings", "set", "updates.enabled", "false");
        string file = SettingsFilePath();
        real.FileSystem.GetFileWriter().WriteAllText(file, "updates.enabled: false\nupdates.channel: [stable]\n");

        CliResult result = CliRunner.RunInHome(home, "settings");

        result.ExitCode.Should().Be(1);
        result.StandardError.Should().Contain($"{file}, line 2: flow style");
    }

    public void Dispose() => real.FileSystem.GetDirectoryWriter().DeleteDirectory(home, recursive: true);

    // The one settings.yaml under the temporary home, wherever this OS's per-user folder puts it (on Linux inside the
    // hidden ~/.config, so hidden folders are searched too).
    private string SettingsFilePath() =>
        real.FileSystem.GetDirectoryReader()
            .EnumerateFiles(home, "settings.yaml", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
            .Should().ContainSingle().Subject;
}
