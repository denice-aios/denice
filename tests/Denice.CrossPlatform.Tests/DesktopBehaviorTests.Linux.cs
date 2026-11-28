// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Linux.Desktop;
using Denice.TestHelpers;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>Linux desktop behavior that needs no display or graphics device, run on every Linux test leg.</summary>
public sealed class DesktopBehaviorTests
{
    [Fact]
    public void RecordingIsRefusedBeforeAnyNativeDesktopStarts_AndTheRunStartsWithNoAudioFailure()
    {
        TimeProvider clock = SystemServicesBuilder.Real().Build().Clock;
        IPlatformDesktop desktop = LinuxDesktop.Create();
        ((IPlatformBrowserAudioSink)desktop).OnAudioError("A failure from an earlier run.");

        int exit = desktop.Run(FixedApplicationSession.Create(new Uri("http://127.0.0.1/")), "capture.mp4", 1, clock, "cache", 0, TimeSpan.FromSeconds(10));

        Assert.Equal(1, exit);
        Assert.Equal("Linux recording requires the pending GPU encoder and approved audio capture integration.", desktop.LastRecordingError);
        Assert.Null(desktop.LastAudioError);
    }
}
