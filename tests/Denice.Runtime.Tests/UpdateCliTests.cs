// Copyright (c) Denice.ai. All rights reserved.

using Denice.TestHelpers;
using Denice.Updates;
using FluentAssertions;
using Xunit;

namespace Denice.Runtime.Tests;

/// <summary>
/// The <c>update</c> command and its progress display. A run without the loader (a developer run, as in these tests) has
/// nothing to update, and the command says so instead of reaching the network; the update service itself is proven with a
/// fake GitHub in Denice.Tests (UpdateServiceTests).
/// </summary>
public sealed class UpdateCliTests
{
    [Fact]
    public void Update_WithoutTheLoader_SaysThereIsNothingToUpdate()
    {
        CliResult result = CliRunner.Run("update");

        result.ExitCode.Should().Be(1);
        result.StandardError.Should().Contain("not started by its loader, so there is nothing to update");
    }

    [Fact]
    public void AOneShotCommand_IsNotHeldUpByTheBackgroundCheck()
    {
        CliResult result = CliRunner.Run("version");

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("SemVer:");
    }

    [Fact]
    public void Progress_IsShownOncePerTenthOfTheFile()
    {
        var recorder = new RecordingConsole(string.Empty);
        var progress = new ConsoleDownloadProgress(recorder.Console);
        const long total = 40L * 1024 * 1024;

        for (long received = 0; received <= total; received += 1024 * 1024)
        {
            progress.Report(new DownloadProgress(received, total));
        }

        string[] lines = recorder.Output.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(11);
        lines[0].Should().StartWith("Downloading: 0.0 MB of 40.0 MB");
        lines[^1].Should().StartWith("Downloading: 40.0 MB of 40.0 MB");
    }

    [Fact]
    public void Progress_WithAnUnknownSize_ShowsTheStartOnly()
    {
        var recorder = new RecordingConsole(string.Empty);
        var progress = new ConsoleDownloadProgress(recorder.Console);

        progress.Report(new DownloadProgress(10, 0));
        progress.Report(new DownloadProgress(20, 0));

        recorder.Output.Split('\n', System.StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle();
    }
}
