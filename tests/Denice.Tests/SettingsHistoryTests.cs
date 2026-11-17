// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using Denice.Updates;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// The last-known-good settings (pkg-d4e90c3ce176 broken-settings), through <see cref="SettingsFile"/> on the real file
/// system under a temporary home (never the real user folder), with a fake clock and a recorded console: every
/// successful load is saved once, the history keeps the newest 20, a broken file is kept as <c>settings.yaml.broken</c>
/// and replaced by the newest saved copy or by version 0, and any version can be restored by number.
/// </summary>
public sealed class SettingsHistoryTests : IDisposable
{
    private const string Broken = "updates.enabled: false\nupdates.channel: [stable]\n";

    private static readonly DateTimeOffset Start = new(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

    private readonly ISystemServices real;
    private readonly string home;
    private readonly FakeTimeProvider clock = new(Start);
    private readonly RecordingConsole console = new();
    private readonly SettingsFile settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsHistoryTests"/> class over a fresh temporary home, which is
    /// also the <c>LOCALAPPDATA</c> folder so the same test runs on Windows.
    /// </summary>
    public SettingsHistoryTests()
    {
        real = SystemServicesBuilder.Real().Build();
        home = real.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-settings-history-");
        ISystemServices services = SystemServicesBuilder.Real()
            .With(FakeEnvironment.Create(
                home: home,
                variables: new Dictionary<string, string>(StringComparer.Ordinal) { ["LOCALAPPDATA"] = home }))
            .With(clock)
            .With(console.Console)
            .Build();
        settings = new SettingsFile(services);
    }

    private string HistoryFolder => Path.Combine(Path.GetDirectoryName(settings.FilePath)!, "settings-history");

    [Fact]
    public void WithNoFile_NothingIsSaved_AndOnlyVersion0IsListed()
    {
        settings.List();

        real.FileSystem.GetDirectoryReader().DirectoryExists(HistoryFolder).Should().BeFalse();
        settings.History().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Number = 0, Saved = (DateTimeOffset?)null, SavedDescription = "the original", SettingsDescription = "every default" });
    }

    [Fact]
    public void ASuccessfulLoad_SavesTheFilesExactTextWithItsTime()
    {
        settings.Set("updates.enabled", "false");
        clock.Advance(TimeSpan.FromMinutes(5));

        settings.List();

        List<string> copies = Copies();
        copies.Should().ContainSingle().Which.Should().EndWith("000001-20261116T093500Z.yaml");
        real.FileSystem.GetFileReader().ReadAllText(copies[0]).Should().Be(Read());
        settings.History()[0].Should().BeEquivalentTo(new
        {
            Number = 1,
            Saved = (DateTimeOffset?)Start.AddMinutes(5),
            SavedDescription = "saved 2026-11-16 09:35:00 UTC",
            SettingsDescription = "updates.enabled: false",
            Description = "version 1 (saved 2026-11-16 09:35:00 UTC; updates.enabled: false)",
        });
    }

    [Fact]
    public void AnUnchangedFile_IsNotSavedAgain()
    {
        settings.Set("updates.enabled", "false");

        settings.List();
        settings.ReadUpdateSettings();
        UpdateSettings.Read(SystemServicesFor());
        settings.List();

        Copies().Should().ContainSingle();
    }

    [Fact]
    public void TheHistory_KeepsTheNewest20_DroppingTheOldest()
    {
        for (int hours = 1; hours <= 25; hours++)
        {
            settings.Set("updates.checkIntervalHours", hours.ToString(System.Globalization.CultureInfo.InvariantCulture));
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        IReadOnlyList<SettingsVersion> history = settings.History();

        Copies().Should().HaveCount(20);
        history.Select(version => version.Number).Should().Equal(Enumerable.Range(0, 21).Select(number => (number + 1) % 21));
        history[0].SettingsDescription.Should().Be("updates.checkIntervalHours: 25");
        history[19].SettingsDescription.Should().Be("updates.checkIntervalHours: 6");
        history[20].Number.Should().Be(SettingsVersion.Original);
    }

    [Fact]
    public void ABrokenFile_IsRestoredToTheNewestSavedCopy_KeptAsBroken_AndReported()
    {
        settings.Set("updates.enabled", "false");
        settings.List();
        string good = Read();
        Write(Broken);

        UpdateSettings read = settings.ReadUpdateSettings();

        read.Enabled.Should().BeFalse();
        Read().Should().Be(good);
        real.FileSystem.GetFileReader().ReadAllText(settings.BrokenFilePath).Should().Be(Broken);
        settings.BrokenFilePath.Should().Be(settings.FilePath + ".broken");
        console.Error.TrimEnd().Should().Be(
            $"The settings file was broken: {settings.FilePath}, line 2: flow style ('[...]', '{{...}}') is not supported. "
            + "Denice restored settings version 1 (saved 2026-11-16 09:30:00 UTC; updates.enabled: false); "
            + $"the broken file is kept at {settings.FilePath}.broken.");
        Copies().Should().ContainSingle("the restored text is already the newest copy");
    }

    [Fact]
    public void ABrokenFile_WithNoSavedCopy_IsRestoredToVersion0()
    {
        Write(Broken);

        settings.List().Should().OnlyContain(state => state.Value == state.DefaultValue);

        Read().Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().OnlyContain(line => line.StartsWith('#'));
        real.FileSystem.GetFileReader().ReadAllText(settings.BrokenFilePath).Should().Be(Broken);
        console.Error.Should().Contain("Denice restored settings version 0 (the original; every default);");
    }

    [Fact]
    public void ABrokenFile_IsReportedOnce()
    {
        Write(Broken);

        settings.List();
        settings.List();

        console.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle();
    }

    [Fact]
    public void ALaterBreakage_ReplacesTheKeptBrokenFile()
    {
        Write(Broken);
        settings.List();
        const string Later = "updates.enabled: maybe\n";
        Write(Later);

        settings.List();

        real.FileSystem.GetFileReader().ReadAllText(settings.BrokenFilePath).Should().Be(Later);
    }

    [Fact]
    public void SetAndReset_WorkAfterABreakage()
    {
        settings.Set("updates.channel", "stable");
        settings.List();
        Write(Broken);

        settings.Set("updates.checkIntervalHours", "12");
        settings.ReadUpdateSettings().Should().Be(new UpdateSettings(true, UpdateChannel.Stable, TimeSpan.FromHours(12)));

        Write(Broken);
        settings.Reset("updates.channel");
        settings.ReadUpdateSettings().Should().Be(new UpdateSettings(true, UpdateChannel.PreReleaseUntilStable, TimeSpan.FromHours(12)));
    }

    [Fact]
    public void Restore_OfASavedVersion_MakesItTheCurrentFile_AndSavesTheOneItReplaced()
    {
        settings.Set("updates.channel", "stable");
        clock.Advance(TimeSpan.FromMinutes(1));
        settings.Set("updates.channel", "pre-release");
        clock.Advance(TimeSpan.FromMinutes(1));
        string stable = real.FileSystem.GetFileReader().ReadAllText(Copies()[^1]);

        SettingsVersion restored = settings.Restore(2);

        restored.Description.Should().Be("version 2 (saved 2026-11-16 09:31:00 UTC; updates.channel: stable)");
        Read().Should().Be(stable);
        settings.History().Select(version => version.SettingsDescription).Should().Equal(
            "updates.channel: stable", "updates.channel: pre-release", "updates.channel: stable", "every default");
    }

    [Fact]
    public void Restore_OfVersion0_ReturnsEverySettingToItsDefault()
    {
        settings.Set("updates.enabled", "false");

        SettingsVersion restored = settings.Restore(0);

        restored.Number.Should().Be(0);
        settings.ReadUpdateSettings().Should().Be(UpdateSettings.Defaults);
        settings.History()[1].SettingsDescription.Should().Be("updates.enabled: false", "the replaced settings can be restored in turn");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void Restore_OfAVersionThatDoesNotExist_IsRefusedAndWritesNothing(int number)
    {
        settings.Set("updates.enabled", "false");
        string before = Read();

        Action act = () => settings.Restore(number);

        act.Should().Throw<InvalidSettingException>()
            .WithMessage($"There is no settings version {number}; the versions are 0 to 1 (see 'denice settings history').");
        Read().Should().Be(before);
    }

    [Fact]
    public void ASavedCopyThatNoLongerReads_IsNotOffered()
    {
        settings.Set("updates.enabled", "false");
        settings.List();
        real.FileSystem.GetFileWriter().WriteAllText(Copies()[0], Broken);
        real.FileSystem.GetFileWriter().DeleteFile(settings.FilePath);

        settings.History().Should().ContainSingle().Which.Number.Should().Be(0);
    }

    public void Dispose() => real.FileSystem.GetDirectoryWriter().DeleteDirectory(home, recursive: true);

    // A second container over the same temporary home, as the update check builds its own SettingsFile.
    private ISystemServices SystemServicesFor() =>
        SystemServicesBuilder.Real()
            .With(FakeEnvironment.Create(
                home: home,
                variables: new Dictionary<string, string>(StringComparer.Ordinal) { ["LOCALAPPDATA"] = home }))
            .With(clock)
            .Build();

    // The saved copies' paths, oldest first.
    private List<string> Copies() =>
        real.FileSystem.GetDirectoryReader().EnumerateChildren(HistoryFolder)
            .Select(child => child.FullPath)
            .Order(StringComparer.Ordinal)
            .ToList();

    private string Read() => real.FileSystem.GetFileReader().ReadAllText(settings.FilePath);

    private void Write(string text)
    {
        real.FileSystem.GetDirectoryWriter().CreateDirectory(Path.GetDirectoryName(settings.FilePath)!);
        real.FileSystem.GetFileWriter().WriteAllText(settings.FilePath, text);
    }
}
