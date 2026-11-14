// Copyright (c) Denice.ai. All rights reserved.

using FluentAssertions;
using Xunit;

namespace Denice.Cli.Tests;

/// <summary>
/// Drives the CLI through every command handler in <c>Program.cs</c> by way of the internal <c>Program.Run</c>
/// seam. Each test runs a command against a throwaway isolated machine and project whose environment and console
/// are injected fakes, so the production command wiring runs end to end without a real install and without touching
/// the developer's machine — on every OS.
/// </summary>
public sealed class ProgramCliTests
{
    [Fact]
    public void Version_PrintsTheThreeBuildVersions()
    {
        CliResult result = CliRunner.Run("version");

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("SemVer:");
        result.StandardOutput.Should().Contain("AssemblyVersion:");
        result.StandardOutput.Should().Contain("FileVersion:");
    }
}
