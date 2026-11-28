// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Windows.Desktop;
using Denice.CrossPlatform.Windows.Recording;
using Denice.TestHelpers;
using SkiaSharp;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>Windows desktop behavior that needs no graphics adapter, run on every Windows test leg.</summary>
public sealed class DesktopBehaviorTests
{
    [Fact]
    public void WindowOpensWithAClientAreaPumpsAndDisposesCleanly()
    {
        WindowsWindow window = WindowsWindow.Create(320, 240);
        try
        {
            Assert.True(window.IsOpen);
            Assert.NotEqual(0, window.Handle);
            SKSizeI client = window.GetClientSize();
            Assert.True(client.Width > 0 && client.Height > 0);
            window.Pump();
            Assert.True(window.IsOpen);
        }
        finally
        {
            window.Dispose();
        }

        window.Dispose();
        Assert.False(window.IsOpen);
        Assert.False(window.CleanupFailed);
    }

    [Fact]
    public void MediaFoundationSessionClosesOnlyOnItsOpeningThread()
    {
        MfNative.Session session = MfNative.OpenSession();
        try
        {
            Exception? wrongThread = null;
            var other = new Thread(() => wrongThread = Record.Exception(session.Dispose)) { IsBackground = true };
            other.Start();
            Assert.True(other.Join(TimeSpan.FromSeconds(10)), "The wrong-thread close attempt did not return within 10 seconds.");

            InvalidOperationException refusal = Assert.IsType<InvalidOperationException>(wrongThread);
            Assert.Equal("Media Foundation must close on the thread that initialized COM.", refusal.Message);
        }
        finally
        {
            // The owning thread always releases Media Foundation and COM, even after a failed assertion.
            session.Dispose();
        }

        session.Dispose();
    }

    [Fact]
    public void GpuContextUsesAHardwareAdapterOrRefusesSoftwareRendering()
    {
        WindowsGpuContext? context = null;
        Exception? failure = Record.Exception(() => context = WindowsGpuContext.Create());
        using (context)
        {
            if (failure is null)
            {
                // A context exists only on a hardware adapter, never the Microsoft Basic Render Driver.
                Assert.DoesNotContain("vendor=0x1414; device=0x008C", context!.DescribeAdapter(), StringComparison.Ordinal);
            }
            else
            {
                InvalidOperationException refusal = Assert.IsType<InvalidOperationException>(failure);
                Assert.StartsWith("No hardware D3D11 adapter with video support is available; software rendering is not substituted.", refusal.Message, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void RecordingIsRefusedBeforeAnyNativeDesktopStarts()
    {
        TimeProvider clock = SystemServicesBuilder.Real().Build().Clock;
        IPlatformDesktop desktop = WindowsDesktop.Create(Termination());
        PlatformNotSupportedException refusal = Assert.Throws<PlatformNotSupportedException>(
            () => desktop.Run(FixedApplicationSession.Create(new Uri("http://127.0.0.1/")), "capture.mp4", 1, clock, "cache", 0, TimeSpan.FromSeconds(10)));
        Assert.Equal("Windows MP4 recording is not yet connected to the hardware compositor.", refusal.Message);
    }

    [Fact]
    public void AnUnsafeBrowserGpuCopyEndsTheProcessThroughTheOwnedEnvironment()
    {
        // The termination the Windows door hands the desktop calls IEnvironment.FailFast with the reason; the fake
        // environment throws where the real one ends the process.
        var termination = Termination();
        FakeFailFastException ended = Assert.Throws<FakeFailFastException>(() => termination.Terminate("copy still pending"));
        Assert.Equal("copy still pending", ended.Message);
    }

    [Fact]
    public void TheTerminationRequiresAnEnvironment()
    {
        Assert.Throws<ArgumentNullException>(() => new BrowserGpuCopyTermination(null!));
    }

    [Theory]
    [InlineData(0, 0, 0, (int)WindowsSharedFrame.CompletionStep.Done)]
    [InlineData(0, 0, 11, (int)WindowsSharedFrame.CompletionStep.Done)]
    [InlineData(1, 0, 0, (int)WindowsSharedFrame.CompletionStep.Wait)]
    [InlineData(1, 0, 10, (int)WindowsSharedFrame.CompletionStep.Wait)]
    [InlineData(1, 0, 11, (int)WindowsSharedFrame.CompletionStep.DeadlinePassed)]
    [InlineData(unchecked((int)0x887A0005), 0, 0, (int)WindowsSharedFrame.CompletionStep.DeviceLost)]
    [InlineData(unchecked((int)0x887A0007), 0, 0, (int)WindowsSharedFrame.CompletionStep.DeviceLost)]
    [InlineData(1, unchecked((int)0x887A0006), 11, (int)WindowsSharedFrame.CompletionStep.DeviceLost)]
    [InlineData(unchecked((int)0x80004005), unchecked((int)0x887A0020), 0, (int)WindowsSharedFrame.CompletionStep.DeviceLost)]
    [InlineData(unchecked((int)0x80004005), unchecked((int)0x887A0001), 0, (int)WindowsSharedFrame.CompletionStep.DeviceLost)]
    [InlineData(unchecked((int)0x80004005), 0, 0, (int)WindowsSharedFrame.CompletionStep.QueryFailed)]
    [InlineData(unchecked((int)0x80070057), 0, 0, (int)WindowsSharedFrame.CompletionStep.QueryFailed)]
    public void GpuCopyCompletionFollowsTheApprovedPolicy(int queryStatus, int removedReason, int elapsedSeconds, int expected)
    {
        // Deadline browser.gpuCopyTimeoutSeconds = 10 s: done returns, pending waits until the deadline then ends the process,
        // a removed or reset device is an ordinary error, and an unexplained failure on a live device ends the process.
        Assert.Equal((WindowsSharedFrame.CompletionStep)expected, WindowsSharedFrame.Classify(queryStatus, removedReason, TimeSpan.FromSeconds(elapsedSeconds), TimeSpan.FromSeconds(10)));
    }

    // The termination the Windows door builds, over a fake environment whose FailFast throws instead of ending the run.
    private static BrowserGpuCopyTermination Termination() =>
        new(FakeEnvironment.Create(SystemServicesBuilder.Real().Build().Environment.GetHomeDirectory()));
}
