// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// The platform desktop keeps the first browser audio failure of a run whether or not it records (IPlatformDesktop
/// LastAudioError), on every platform's desktop, through the contracts only.
/// </summary>
public sealed class DesktopAudioErrorTests
{
    [Fact]
    public void Desktop_KeepsTheFirstBrowserAudioFailure_WithoutARecording()
    {
        IPlatformDesktop desktop = SystemServicesBuilder.Real().Build().Platform.Desktop;
        IPlatformBrowserAudioSink sink = Assert.IsAssignableFrom<IPlatformBrowserAudioSink>(desktop);
        desktop.LastAudioError.Should().BeNull();

        sink.OnAudioError("The first browser audio stream failed.");
        sink.OnAudioError("A later browser audio stream failed.");

        desktop.LastAudioError.Should().Be("The first browser audio stream failed.");
    }
}
