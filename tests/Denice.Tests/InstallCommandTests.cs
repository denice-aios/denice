// Copyright (c) Denice.ai. All rights reserved.

using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Denice.Engine.Setup;
using FluentAssertions;
using Xunit;

namespace Denice.Tests;

public sealed class InstallCommandTests
{
    [Fact]
    public void Acceptance_3a_InstallIntoCleanHome_BuildsLayoutAndRecordsHash()
    {
        using var harness = new SetupHarness();

        CommandOutcome outcome = harness.Install("0.1.0-alpha");

        outcome.Success.Should().BeTrue();
        harness.Files.Exists(harness.VersionBinary("0.1.0-alpha")).Should().BeTrue();
        harness.Files.Exists(harness.CurrentBinary).Should().BeTrue();
        harness.Files.Exists(harness.BinGuard).Should().BeTrue();

        InstallState state = ReadInstallState(harness);
        state.Version.Should().Be("0.1.0-alpha");
        state.Sha256.Should().Be(HashOf(harness, harness.VersionBinary("0.1.0-alpha")));
        harness.Files.ReadAllText(harness.ShellProfilePath).Should().Contain(".denice/bin");
    }

    [Fact]
    public void Acceptance_3b_NewerInstall_FlipsCurrentWithoutBreakingPriorVersion()
    {
        using var harness = new SetupHarness();
        harness.Install("0.1.0").Success.Should().BeTrue();

        harness.Install("0.2.0").Success.Should().BeTrue();

        harness.Files.Exists(harness.VersionBinary("0.1.0")).Should().BeTrue();
        harness.Files.ReadAllText(harness.VersionBinary("0.1.0")).Should().Be("GUARD BINARY v0.1.0");
        harness.Files.ReadAllText(harness.CurrentBinary).Should().Be("GUARD BINARY v0.2.0");
    }

    [Fact]
    public async Task Acceptance_3b_SameVersionReinstall_IsNoOpAndDoesNotOverwriteRunningImage()
    {
        using var harness = new SetupHarness();
        harness.Install("0.2.0").Success.Should().BeTrue();
        string installed = harness.VersionBinary("0.2.0");
        var writtenAt = harness.Files.GetLastWriteTimeUtc(installed);

        // Re-run installing the already-installed binary itself: it must be skipped, never overwritten.
        CommandOutcome outcome = await SetupCommands.Install(harness.Context("0.2.0", installed), harness.Services, allowDowngrade: false);

        outcome.Success.Should().BeTrue();
        harness.Files.GetLastWriteTimeUtc(installed).Should().Be(writtenAt);
        harness.Files.ReadAllText(installed).Should().Be("GUARD BINARY v0.2.0");
    }

    [Fact]
    public void Acceptance_3b_OlderInstall_IsRefusedUnlessDowngradeAllowed()
    {
        using var harness = new SetupHarness();
        harness.Install("0.2.0").Success.Should().BeTrue();

        CommandOutcome refused = harness.Install("0.1.5");

        refused.Success.Should().BeFalse();
        harness.Files.Exists(harness.VersionBinary("0.1.5")).Should().BeFalse();
        harness.Files.ReadAllText(harness.CurrentBinary).Should().Be("GUARD BINARY v0.2.0");

        CommandOutcome allowed = harness.Install("0.1.5", allowDowngrade: true);

        allowed.Success.Should().BeTrue();
        harness.Files.Exists(harness.VersionBinary("0.1.5")).Should().BeTrue();
        harness.Files.ReadAllText(harness.CurrentBinary).Should().Be("GUARD BINARY v0.1.5");
    }

    [Fact]
    public void Acceptance_3k_Install_TouchesOnlyDeniceAndTheProfileLine()
    {
        using var harness = new SetupHarness();

        harness.Install("0.1.0-alpha").Success.Should().BeTrue();

        harness.Directories.EnumerateChildren(harness.Project).Should().BeEmpty();
        harness.Directories.EnumerateChildren(harness.Home)
            .Select(child => Path.GetFileName(child.FullPath))
            .Should()
            .BeEquivalentTo(".denice", ".zshrc");
    }

    private static InstallState ReadInstallState(SetupHarness harness) =>
        SetupJson.DeserializeInstallState(harness.Files.ReadAllText(harness.MachineStateFile))!;

    private static string HashOf(SetupHarness harness, string path) =>
        System.Convert.ToHexStringLower(SHA256.HashData(harness.Files.ReadAllBytes(path)));
}
