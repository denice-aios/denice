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
    [InlineData(0, null, 0)]
    [InlineData(0, "The encoder stopped accepting frames.", 1)]
    [InlineData(7, "The recorder could not finalize.", 7)]
    public async Task DesktopExit_ReportsRecordingFailure(int desktopExit, string? recordingError, int expectedExit)
    {
        var console = new RecordingConsole();
        IPlatformDesktop desktop = FakeDesktop.Create(desktopExit, recordingError);
        var builder = SystemServicesBuilder.Real().SimulateFileSystem();
        string home = builder.Build().FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-desktop-cli-");
        builder.With(FakeEnvironment.Create(
            home: home,
            variables: new Dictionary<string, string>(StringComparer.Ordinal) { ["LOCALAPPDATA"] = home }));
        builder.With(console.Console);
        builder.OnPlatform().With(desktop);
        ISystemServices services = builder.Build();

        int exit = await new Program(["gui", "--duration", "1", "--record", "capture.mp4"]).Run(services);

        exit.Should().Be(expectedExit, console.Error);
        console.Output.Should().Contain(desktop.LastFrameStatistics);
        if (recordingError is not null)
        {
            console.Error.Should().Contain($"DENICE recording failed: {recordingError}");
        }
    }
}
