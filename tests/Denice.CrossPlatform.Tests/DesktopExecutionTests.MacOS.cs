// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Browser;
using Denice.CrossPlatform.MacOS.Desktop;
using Denice.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace Denice.CrossPlatform.Tests;

/// <summary>Exercises Cocoa on the process main thread using the instrumented product assemblies.</summary>
public sealed class DesktopExecutionTests(ITestOutputHelper output)
{
    [SuppressMessage("Denice.Architecture", "DN0011", Justification = "This native integration fixture owns a bounded child process so Cocoa executes on its OS main thread; no product process interface exists. All other environment, file and clock operations use Systemservices.")]
    [Theory]
    [InlineData("compositor")]
    [InlineData("desktop")]
    public async Task NativeDesktopCompletesWithRenderedContent(string scenario)
    {
        ISystemServices services = SystemServicesBuilder.Real().Build();
        string executable = Path.Combine(services.Environment.GetBaseDirectory(), "Denice.CrossPlatform.Tests");
        string directory = Path.Combine(Path.GetDirectoryName(executable)!, "TestArtifacts", "desktop-" + scenario);
        services.FileSystem.GetDirectoryWriter().CreateDirectory(directory);
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(scenario);
        start.ArgumentList.Add(directory);
        _ = start.Environment.Remove("DYLD_LIBRARY_PATH");
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the native desktop fixture.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90), services.Clock);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10), services.Clock);
            throw;
        }
        finally
        {
            string log = await stdout + "\n" + await stderr;
            await services.FileSystem.GetFileWriter().WriteAllBytesAsync(Path.Combine(directory, "execution.log"), Encoding.UTF8.GetBytes(log), CancellationToken.None);
            output.WriteLine(log);
        }

        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>Runs only a selected native fixture, on the OS main thread.</summary>
    private static int Main(string[] args)
    {
        ISystemServices services = SystemServicesBuilder.Real().Build();
        if (args.Length != 2 || args[0] is not ("compositor" or "desktop"))
        {
            return 2;
        }

        if (string.Equals(args[0], "compositor", StringComparison.Ordinal))
        {
            AssertCompositor(services, args[1]);
        }
        else
        {
            AssertDesktop(services, args[1]);
        }

        services.Console.WriteLine("Native fixture completed: " + args[0]);
        return 0;
    }

    private static void AssertDesktop(ISystemServices services, string directory)
    {
        string recording = Path.Combine(directory, "desktop-" + services.Random.NewGuid().ToString("N") + ".mp4");
        string cache = Path.Combine(directory, "cache-" + services.Random.NewGuid().ToString("N"));
        const string html = "<html><body style='margin:0;background:transparent'><button style='background:red;width:150px;height:80px'>Native fixture</button></body></html>";
        var desktop = MacDesktop.Create();
        int exit = desktop.Run(new Uri("data:text/html," + Uri.EscapeDataString(html)), recording, 2, services.Clock, cache, 0);
        Assert.Equal(0, exit);
        Assert.Null(desktop.LastRecordingError);
        Assert.True(services.FileSystem.GetFileReader().ReadAllBytes(recording).Length > 1024, "The finalized recording must contain encoded frame data.");
        services.Console.WriteLine(desktop.LastFrameStatistics);
        services.Console.WriteLine("Recording artifact: " + recording);
    }

    private static void AssertCompositor(ISystemServices services, string directory)
    {
        nint pool = MacNative.Call(MacNative.Class("NSAutoreleasePool"), "new");
        try
        {
            _ = MacApplication.Create();
            using var window = MacGpuWindow.Create();
            byte[] first = ReadRenderedFrame(window, 0);
            byte[] second = ReadRenderedFrame(window, 1);
            Assert.True(
                first.Where((value, index) => value != second[index]).Take(101).Count() > 100,
                "The native orb must change rendered pixels as its animation advances.");
            int center = ((window.RecordingHeight / 2 * window.RecordingWidth) + (window.RecordingWidth / 2)) * 4;
            Assert.False(
                first.AsSpan(center, 3).SequenceEqual(first.AsSpan(0, 3)),
                "The center of the rendered orb must differ from the background.");
            AssertBrowserContent(services, window, directory);
            services.Console.WriteLine($"Native rendered content: {window.RecordingWidth}x{window.RecordingHeight}.");
        }
        finally
        {
            MacNative.Release(pool);
        }
    }

    private static void AssertBrowserContent(ISystemServices services, MacGpuWindow window, string directory)
    {
        const string html = "<html><body style='margin:0;background:transparent'><button onclick=\"this.style.background='lime'\" style='border:0;background:red;width:150px;height:80px'>Click</button></body></html>";
        IPlatformBrowser browser = CefPlatformBrowser.Create();
        IPlatformBrowserFrameSink frames = FrameSink.Create(window);
        string cache = Path.Combine(directory, "browser-" + services.Random.NewGuid().ToString("N"));
        try
        {
            browser.Start(new Uri("data:text/html," + Uri.EscapeDataString(html)), MacGpuWindow.Width, MacGpuWindow.Height, frames, (IPlatformBrowserAudioSink)MacDesktop.Create(), cache, 0);
            WaitForColor(services, browser, window, red: true);
            Assert.True(browser.IsReady);
            browser.MouseMove(40, 40, false);
            browser.MouseClick(40, 40, 0, false, 1);
            browser.MouseClick(40, 40, 0, true, 1);
            WaitForColor(services, browser, window, red: false);
        }
        finally
        {
            browser.RequestClose();
            long start = services.Clock.GetTimestamp();
            while (!browser.IsClosed && services.Clock.GetElapsedTime(start).TotalSeconds < 10)
            {
                browser.Pump();
                Task.Delay(TimeSpan.FromMilliseconds(5), services.Clock).GetAwaiter().GetResult();
            }

            Assert.True(browser.IsClosed, "CEF must complete browser closure before shutdown.");
            browser.Shutdown();
        }
    }

    private static void WaitForColor(ISystemServices services, IPlatformBrowser browser, MacGpuWindow window, bool red)
    {
        long start = services.Clock.GetTimestamp();
        while (services.Clock.GetElapsedTime(start).TotalSeconds < 15)
        {
            browser.Pump();
            byte[] pixels = ReadRenderedFrame(window, 1);
            int matches = 0;
            for (int index = 0; index < pixels.Length; index += 4)
            {
                if (pixels[index] < 35 && pixels[index + (red ? 2 : 1)] > 220 && pixels[index + (red ? 1 : 2)] < 35)
                {
                    matches++;
                }
            }

            if (matches > 500)
            {
                return;
            }

            Task.Delay(TimeSpan.FromMilliseconds(5), services.Clock).GetAwaiter().GetResult();
        }

        Assert.Fail(red ? "The composed browser button never became red." : "Clicking the browser button did not render green pixels.");
    }

    private static byte[] ReadRenderedFrame(MacGpuWindow window, double seconds)
    {
        int stride = checked(window.RecordingWidth * 4);
        byte[] pixels = new byte[checked(stride * window.RecordingHeight)];
        GCHandle storage = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        nint pool = MacNative.Call(MacNative.Class("NSAutoreleasePool"), "new");
        try
        {
            window.Render(seconds);
            window.ReadFrame(storage.AddrOfPinnedObject(), stride);
            return pixels;
        }
        finally
        {
            MacNative.Release(pool);
            storage.Free();
        }
    }

    private sealed class FrameSink : IPlatformBrowserFrameSink
    {
        private readonly MacGpuWindow _window;

        private FrameSink(MacGpuWindow window) => _window = window;

        public void OnAcceleratedFrame(scoped in BrowserGpuBuffer buffer, int format, int width, int height, int visibleX, int visibleY, int visibleWidth, int visibleHeight, bool popup)
        {
            _window.CopyBrowserSurface(buffer.SharedTexture, format, width, height, visibleX, visibleY, visibleWidth, visibleHeight, popup);
        }

        public void OnPopup(bool visible, int x, int y, int width, int height) => _window.Popup(visible, x, y, width, height);

        [SuppressMessage("Performance", "CA1859", Justification = "Contract factories return their interface under the project ownership rules.")]
        internal static IPlatformBrowserFrameSink Create(MacGpuWindow window) => new FrameSink(window);
    }
}
