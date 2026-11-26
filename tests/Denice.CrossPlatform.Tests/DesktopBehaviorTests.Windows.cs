// Copyright (c) Denice.ai. All rights reserved.

using Denice.CrossPlatform.Windows.Desktop;
using Denice.CrossPlatform.Windows.Recording;
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
}
