// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Denice.Runtime.Tests;

/// <summary>Checks that desktop recording failures reach the invoking CI process.</summary>
public sealed class DesktopCliTests
{
    [Theory]
    [InlineData(0, null, null, true, 0)]
    [InlineData(0, "The encoder stopped accepting frames.", null, true, 1)]
    [InlineData(7, "The recorder could not finalize.", null, true, 7)]
    [InlineData(0, null, "A browser audio stream's render callback failed.", true, 1)]
    [InlineData(0, null, "A browser audio stream's render callback failed.", false, 1)]
    [InlineData(0, "The encoder stopped accepting frames.", "A browser audio stream's render callback failed.", true, 1)]
    [InlineData(7, null, "A browser audio stream's render callback failed.", false, 7)]
    public async Task DesktopExit_ReportsRecordingAndAudioFailures(int desktopExit, string? recordingError, string? audioError, bool record, int expectedExit)
    {
        var console = new RecordingConsole();
        IPlatformDesktop desktop = FakeDesktop.Create(desktopExit, recordingError, audioError);
        var builder = SystemServicesBuilder.Real().SimulateFileSystem();
        string home = builder.Build().FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-desktop-cli-");
        builder.With(FakeEnvironment.Create(
            home: home,
            variables: new Dictionary<string, string>(StringComparer.Ordinal) { ["LOCALAPPDATA"] = home }));
        builder.With(console.Console);
        builder.OnPlatform().With(desktop);
        ISystemServices services = builder.Build();

        string[] arguments = record ? ["gui", "--duration", "1", "--record", "capture.mp4"] : ["gui", "--duration", "1"];
        int exit = await new Program(arguments).Run(services);

        exit.Should().Be(expectedExit, console.Error);
        console.Output.Should().Contain(desktop.LastFrameStatistics);
        if (recordingError is not null)
        {
            console.Error.Should().Contain($"DENICE recording failed: {recordingError}");
        }

        if (audioError is not null)
        {
            console.Error.Should().Contain($"DENICE audio failed: {audioError}");
        }
        else
        {
            console.Error.Should().NotContain("DENICE audio failed");
        }
    }
}
