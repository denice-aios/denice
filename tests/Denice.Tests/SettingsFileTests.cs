// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using Denice.Updates;
using FluentAssertions;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// The per-user <c>settings.yaml</c> through <see cref="SettingsFile"/> and the typed <see cref="UpdateSettings"/>, on
/// the real file system under a temporary home (never the real user folder): defaults with no file, validated changes,
/// resets, and a replace-in-one-step write that never leaves a temporary file behind. A broken file and the history of
/// settings that loaded are covered by <see cref="SettingsHistoryTests"/>.
/// </summary>
public sealed class SettingsFileTests : IDisposable
{
    private const string ChannelDefault = "pre-release while no stable release exists, then stable";

    private static readonly UpdateSettings Defaults = new(true, UpdateChannel.PreReleaseUntilStable, TimeSpan.FromHours(6));

    private readonly ISystemServices real;
    private readonly string home;
    private readonly ISystemServices services;
    private readonly SettingsFile settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsFileTests"/> class over a fresh temporary home, which is also
    /// the <c>LOCALAPPDATA</c> folder so the same test runs on Windows.
    /// </summary>
    public SettingsFileTests()
    {
        real = SystemServicesBuilder.Real().Build();
        home = real.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-settings-");
        services = SystemServicesBuilder.Real().With(TemporaryHome(home)).Build();
        settings = new SettingsFile(services);
    }

    [Fact]
    public void FilePath_IsSettingsYamlInsideTheTemporaryHome()
    {
        settings.FilePath.Should().StartWith(home);
        Path.GetFileName(settings.FilePath).Should().Be("settings.yaml");
    }

    [Fact]
    public void WithNoFile_EverySettingHasItsDefault()
    {
        settings.List().Should().Equal(
            new SettingState("browser.debugPort", "0", "0"),
            new SettingState("browser.profile", "default", "default"),
            new SettingState("updates.enabled", "true", "true"),
            new SettingState("updates.channel", ChannelDefault, ChannelDefault),
            new SettingState("updates.checkIntervalHours", "6", "6"));
        settings.ReadUpdateSettings().Should().Be(Defaults);
        UpdateSettings.Read(services).Should().Be(Defaults);
        UpdateSettings.Defaults.Should().Be(Defaults);
        Exists(settings.FilePath).Should().BeFalse("listing never creates the file");
    }

    [Fact]
    public void BrowserDebugging_IsDisabledByDefaultAndControlledThroughSettings()
    {
        settings.ReadBrowserDebugPort().Should().Be(0);
        settings.Set("browser.debugPort", "9223");
        settings.ReadBrowserDebugPort().Should().Be(9223);
        settings.Reset("browser.debugPort");
        settings.ReadBrowserDebugPort().Should().Be(0);
    }

    [Theory]
    [InlineData("80")]
    [InlineData("65536")]
    [InlineData("-1")]
    public void BrowserDebugging_RejectsInvalidPorts(string port)
    {
        Action change = () => settings.Set("browser.debugPort", port);
        change.Should().Throw<InvalidSettingException>();
    }

    [Fact]
    public void BrowserProfile_SelectsDirectoryInsideTheSettingsFolder()
    {
        settings.Set("browser.profile", "clean-profile");
        settings.ReadBrowserCachePath().Should().Be(Path.Combine(Path.GetDirectoryName(settings.FilePath)!, "Browser", "clean-profile"));
        settings.Reset("browser.profile");
        settings.ReadBrowserCachePath().Should().Be(Path.Combine(Path.GetDirectoryName(settings.FilePath)!, "Browser", "default"));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("profile/child")]
    [InlineData("")]
    [InlineData("space name")]
    public void BrowserProfile_RejectsPathsAndInvalidNames(string profile)
    {
        Action change = () => settings.Set("browser.profile", profile);
        change.Should().Throw<InvalidSettingException>();
    }

    [Theory]
    [InlineData("browser.profile", "fresh-profile_2")]
    [InlineData("updates.enabled", "false")]
    [InlineData("updates.enabled", "true")]
    [InlineData("updates.channel", "stable")]
    [InlineData("updates.channel", "pre-release")]
    [InlineData("updates.checkIntervalHours", "1")]
    [InlineData("updates.checkIntervalHours", "720")]
    public void Set_OfAValidValue_IsListedAndWritten(string name, string value)
    {
        SettingState state = settings.Set(name, value);

        state.Name.Should().Be(name);
        state.Value.Should().Be(value);
        settings.List().Single(listed => string.Equals(listed.Name, name, StringComparison.Ordinal)).Value.Should().Be(value);
        Read().Should().Contain($"\n{name}: {value}\n");
    }

    [Fact]
    public void Set_ReachesTheTypedSettings()
    {
        settings.Set("updates.enabled", "false");
        settings.Set("updates.channel", "stable");
        settings.Set("updates.checkIntervalHours", "24");

        UpdateSettings.Read(services).Should().Be(new UpdateSettings(false, UpdateChannel.Stable, TimeSpan.FromHours(24)));
    }

    [Theory]
    [InlineData("pre-release", UpdateChannel.PreRelease)]
    [InlineData("stable", UpdateChannel.Stable)]
    public void AChosenChannel_AlwaysWinsOverTheDefault(string value, UpdateChannel expected)
    {
        settings.Set("updates.channel", value);

        settings.ReadUpdateSettings().Channel.Should().Be(expected);
        settings.List().Single(state => string.Equals(state.Name, "updates.channel", StringComparison.Ordinal))
            .Should().Be(new SettingState("updates.channel", value, ChannelDefault));
    }

    [Fact]
    public void TheChannelDefault_IsNotAValueTheSettingAccepts()
    {
        Action act = () => settings.Set("updates.channel", ChannelDefault);

        act.Should().Throw<InvalidSettingException>();
    }

    [Theory]
    [InlineData("updates.enabled", "on", "true or false")]
    [InlineData("updates.channel", "nightly", "pre-release or stable")]
    [InlineData("updates.checkIntervalHours", "0", "a whole number of hours from 1 to 720")]
    [InlineData("updates.checkIntervalHours", "721", "a whole number of hours from 1 to 720")]
    public void Set_OfAnInvalidValue_IsRefusedAndWritesNothing(string name, string value, string allowed)
    {
        Action act = () => settings.Set(name, value);

        act.Should().Throw<InvalidSettingException>()
            .WithMessage($"'{value}' is not a valid value for {name}; use {allowed}.");
        Exists(settings.FilePath).Should().BeFalse();
    }

    [Fact]
    public void Set_OfAnUnknownName_IsRefusedNamingTheSettings()
    {
        Action act = () => settings.Set("updates.colour", "red");

        act.Should().Throw<InvalidSettingException>().WithMessage("'updates.colour' is not a setting; the settings are *");
        Exists(settings.FilePath).Should().BeFalse();
    }

    [Fact]
    public void Reset_OfAnUnknownName_IsRefused()
    {
        Action act = () => settings.Reset("updates.colour");

        act.Should().Throw<InvalidSettingException>().WithMessage("'updates.colour' is not a setting*");
    }

    [Fact]
    public void Reset_ReturnsOneSettingToItsDefaultAndKeepsTheOthers()
    {
        settings.Set("updates.channel", "stable");
        settings.Set("updates.checkIntervalHours", "12");

        SettingState state = settings.Reset("updates.channel");

        state.Should().Be(new SettingState("updates.channel", ChannelDefault, ChannelDefault));
        settings.ReadUpdateSettings().Should().Be(Defaults with { CheckInterval = TimeSpan.FromHours(12) });
        Read().Should().NotContain("updates.channel:").And.Contain("\nupdates.checkIntervalHours: 12\n");
    }

    [Fact]
    public void Reset_OfEverySetting_LeavesOnlyComments()
    {
        settings.Set("updates.enabled", "false");

        settings.Reset("updates.enabled");

        Read().Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().OnlyContain(line => line.StartsWith('#'));
        settings.ReadUpdateSettings().Should().Be(Defaults);
    }

    [Fact]
    public void ASecondReader_SeesTheSameSettingsFromTheSameFile()
    {
        settings.Set("updates.channel", "stable");

        new SettingsFile(services).List().Single(state => string.Equals(state.Name, "updates.channel", StringComparison.Ordinal)).Value.Should().Be("stable");
    }

    [Fact]
    public void AHandWrittenFile_WithCommentsAndQuoting_IsRead()
    {
        Write("# mine\nupdates.enabled: 'false'  # off for now\n\nupdates.checkIntervalHours: \"48\"\n");

        settings.ReadUpdateSettings().Should().Be(Defaults with { Enabled = false, CheckInterval = TimeSpan.FromHours(48) });
    }

    [Fact]
    public void Write_LeavesOnlyTheSettingsFileAndItsHistoryInItsFolder()
    {
        settings.Set("updates.channel", "stable");
        settings.Set("updates.channel", "pre-release");

        string folder = Path.GetDirectoryName(settings.FilePath)!;
        real.FileSystem.GetDirectoryReader().EnumerateChildren(folder)
            .Select(child => Path.GetFileName(child.FullPath))
            .Should().BeEquivalentTo("settings.yaml", "settings-history");
        real.FileSystem.GetDirectoryReader().EnumerateChildren(Path.Combine(folder, "settings-history"))
            .Should().ContainSingle().Which.FullPath.Should().EndWith(".yaml");
    }

    [Fact]
    public void Write_ThatFailsBeforeTheReplace_KeepsTheOldFileAndRemovesTheTemporaryFile()
    {
        settings.Set("updates.channel", "stable");
        string before = Read();
        IRandomGenerator random = FixedGuidFactory.Create();
        string temporary = random.TemporarySiblingPath(settings.FilePath);
        SystemServicesBuilder builder = SystemServicesBuilder.Real()
            .SimulateFileSystem()
            .With(TemporaryHome(home))
            .With(random);
        builder.OnFileSystem().Handle(temporary, (operation, passThrough) => operation == FileSystemOperation.ReadAllBytes
            ? throw new IOException("The disk is full.")
            : passThrough());
        ISystemServices failing = builder.Build();
        var failingSettings = new SettingsFile(failing);

        Action act = () => failingSettings.Set("updates.channel", "pre-release");

        act.Should().Throw<IOException>().WithMessage("The disk is full.");
        failing.FileSystem.GetFileReader().Exists(temporary).Should().BeFalse();
        failing.FileSystem.GetFileReader().ReadAllText(settings.FilePath).Should().Be(before);
    }

    public void Dispose() => real.FileSystem.GetDirectoryWriter().DeleteDirectory(home, recursive: true);

    // A fake environment whose home is the temporary folder; LOCALAPPDATA points there too, so the per-user folder is
    // inside it on every OS.
    private static IEnvironment TemporaryHome(string root) =>
        FakeEnvironment.Create(
            home: root,
            variables: new Dictionary<string, string>(StringComparer.Ordinal) { ["LOCALAPPDATA"] = root });

    private bool Exists(string path) => real.FileSystem.GetFileReader().Exists(path);

    private string Read() => real.FileSystem.GetFileReader().ReadAllText(settings.FilePath);

    private void Write(string text)
    {
        real.FileSystem.GetDirectoryWriter().CreateDirectory(Path.GetDirectoryName(settings.FilePath)!);
        real.FileSystem.GetFileWriter().WriteAllText(settings.FilePath, text);
    }
}
