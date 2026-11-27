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
    private const string SamplerPath = "/usr/bin/sample";
    private const int SampleSeconds = 5;
    private static readonly TimeSpan FixtureTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SamplerTimeout = TimeSpan.FromSeconds(30);

    // Set once at the start of the fixture child's Main, so its stage markers carry elapsed time.
    private static long fixtureStart;

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
            await process.WaitForExitAsync().WaitAsync(FixtureTimeout, services.Clock);
        }
        catch (TimeoutException timeout)
        {
            Exception? cleanupFailure = await SampleAndStopAsync(services, process, directory);
            if (cleanupFailure is not null)
            {
                throw new AggregateException("The native fixture timed out and could not be stopped.", timeout, cleanupFailure);
            }

            throw;
        }
        finally
        {
            string log;
            try
            {
                log = await stdout.WaitAsync(CleanupTimeout, services.Clock) + "\n" + await stderr.WaitAsync(CleanupTimeout, services.Clock);
            }
            catch (TimeoutException)
            {
                log = "The native fixture's output did not close after it was stopped.";
            }

            await services.FileSystem.GetFileWriter().WriteAllBytesAsync(Path.Combine(directory, "execution.log"), Encoding.UTF8.GetBytes(log), CancellationToken.None);
            output.WriteLine(log);
        }

        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>Writes a fixture progress marker so a timed-out child shows the last stage it reached.</summary>
    private static void Stage(ISystemServices services, string stage) =>
        services.Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Fixture stage at {services.Clock.GetElapsedTime(fixtureStart).TotalSeconds:F2} s: {stage}"));

    /// <summary>Samples a timed-out fixture, then always stops it, whatever happened to the best-effort sample.</summary>
    /// <returns>The failure to stop the fixture, or null when it exited.</returns>
    [SuppressMessage("Denice.Architecture", "DN0011", Justification = "Stops this bounded native fixture's own child process; no product process interface exists.")]
    private static async Task<Exception?> SampleAndStopAsync(ISystemServices services, Process process, string directory)
    {
        Exception? stopFailure = null;
        try
        {
            await CaptureHangSampleAsync(services, process.Id, directory).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(CleanupTimeout, services.Clock).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                stopFailure = exception;
            }
        }

        return stopFailure;
    }

    /// <summary>Records the timed-out child's thread stacks with the system sampler before it is killed. Best effort: it never
    /// throws, it always stops and drains the sampler within its bound, and a failure is written beside the report.</summary>
    [SuppressMessage("Denice.Architecture", "DN0011", Justification = "Diagnostic for this bounded native fixture: samples its own timed-out child process with the system sampler; no product process interface exists.")]
    [SuppressMessage("Design", "CA1031", Justification = "Sampling is best effort and must never replace the fixture's timeout failure or prevent its cleanup.")]
    private static async Task CaptureHangSampleAsync(ISystemServices services, int processId, string directory)
    {
        string? failure = null;
        try
        {
            var start = new ProcessStartInfo(SamplerPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add(SampleSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("-file");
            start.ArgumentList.Add(Path.Combine(directory, "hang-sample.txt"));
            using Process sampler = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the system sampler.");
            Task<string> samplerOutput = sampler.StandardOutput.ReadToEndAsync();
            Task<string> samplerError = sampler.StandardError.ReadToEndAsync();
            try
            {
                await sampler.WaitForExitAsync().WaitAsync(SamplerTimeout, services.Clock).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                failure = $"The sampler did not finish within {SamplerTimeout.TotalSeconds} s and was stopped.";
            }
            finally
            {
                if (!sampler.HasExited)
                {
                    sampler.Kill(entireProcessTree: true);
                    await sampler.WaitForExitAsync().WaitAsync(CleanupTimeout, services.Clock).ConfigureAwait(false);
                }

                _ = await Task.WhenAll(samplerOutput, samplerError).WaitAsync(CleanupTimeout, services.Clock).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = "Sampling failed: " + exception.Message;
        }

        if (failure is null)
        {
            return;
        }

        try
        {
            await services.FileSystem.GetFileWriter().WriteAllBytesAsync(Path.Combine(directory, "hang-sample-status.txt"), Encoding.UTF8.GetBytes(failure), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The status note is diagnostic only; the timeout failure and fixture cleanup proceed without it.
        }
    }

    /// <summary>Runs only a selected native fixture, on the OS main thread.</summary>
    private static int Main(string[] args)
    {
        ISystemServices services = SystemServicesBuilder.Real().Build();
        fixtureStart = services.Clock.GetTimestamp();
        if (args.Length != 2 || args[0] is not ("compositor" or "desktop"))
        {
            return 2;
        }

        Stage(services, $"start {args[0]}, process architecture {services.Environment.GetProcessArchitecture()}");
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
        Stage(services, "desktop run");
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
            Stage(services, "application created");
            using var window = MacGpuWindow.Create();
            Stage(services, "GPU window created");
            byte[] first = ReadRenderedFrame(window, 0);
            byte[] second = ReadRenderedFrame(window, 1);
            Assert.True(
                first.Where((value, index) => value != second[index]).Take(101).Count() > 100,
                "The native orb must change rendered pixels as its animation advances.");
            int center = ((window.RecordingHeight / 2 * window.RecordingWidth) + (window.RecordingWidth / 2)) * 4;
            Assert.False(
                first.AsSpan(center, 3).SequenceEqual(first.AsSpan(0, 3)),
                "The center of the rendered orb must differ from the background.");
            Stage(services, "native frames rendered");
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
        Stage(services, "browser create");
        IPlatformBrowser browser = CefPlatformBrowser.Create();
        var frameStats = new FrameStats();
        IPlatformBrowserFrameSink frames = FrameSink.Create(window, frameStats);
        string cache = Path.Combine(directory, "browser-" + services.Random.NewGuid().ToString("N"));
        try
        {
            browser.Start(new Uri("data:text/html," + Uri.EscapeDataString(html)), MacGpuWindow.Width, MacGpuWindow.Height, frames, (IPlatformBrowserAudioSink)MacDesktop.Create(), cache, 0);
            Stage(services, "browser started");
            WaitForColor(services, browser, window, frameStats, red: true);
            Stage(services, "browser content rendered");
            Assert.True(browser.IsReady);
            browser.MouseMove(40, 40, false);
            browser.MouseClick(40, 40, 0, false, 1);
            browser.MouseClick(40, 40, 0, true, 1);
            WaitForColor(services, browser, window, frameStats, red: false);
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

    private static void WaitForColor(ISystemServices services, IPlatformBrowser browser, MacGpuWindow window, FrameStats frameStats, bool red)
    {
        long start = services.Clock.GetTimestamp();
        int bestMatches = 0;
        bool sawFrame = false;
        bool sawReady = false;
        while (services.Clock.GetElapsedTime(start).TotalSeconds < 15)
        {
            browser.Pump();
            if (!sawFrame && frameStats.Frames > 0)
            {
                sawFrame = true;
                Stage(services, $"{(red ? "red" : "green")} wait: accelerated frame seen, {frameStats.Frames} so far, last {frameStats.LastWidth}x{frameStats.LastHeight}");
            }

            if (!sawReady && browser.IsReady)
            {
                sawReady = true;
                Stage(services, $"{(red ? "red" : "green")} wait: page ready");
            }

            byte[] pixels = ReadRenderedFrame(window, 1);
            int matches = 0;
            for (int index = 0; index < pixels.Length; index += 4)
            {
                if (pixels[index] < 35 && pixels[index + (red ? 2 : 1)] > 220 && pixels[index + (red ? 1 : 2)] < 35)
                {
                    matches++;
                }
            }

            bestMatches = Math.Max(bestMatches, matches);
            if (matches > 500)
            {
                return;
            }

            Task.Delay(TimeSpan.FromMilliseconds(5), services.Clock).GetAwaiter().GetResult();
        }

        Assert.Fail((red ? "The composed browser button never became red." : "Clicking the browser button did not render green pixels.")
            + $" Accelerated frames received: {frameStats.Frames} (last {frameStats.LastWidth}x{frameStats.LastHeight}, format {frameStats.LastFormat}, popup {frameStats.LastPopup}); page ready: {browser.IsReady}; most matching pixels in one read: {bestMatches}.");
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
        private readonly FrameStats _stats;

        private FrameSink(MacGpuWindow window, FrameStats stats)
        {
            _window = window;
            _stats = stats;
        }

        public void OnAcceleratedFrame(scoped in BrowserGpuBuffer buffer, int format, int width, int height, int visibleX, int visibleY, int visibleWidth, int visibleHeight, bool popup)
        {
            _stats.Record(format, width, height, popup);
            _window.CopyBrowserSurface(buffer.SharedTexture, format, width, height, visibleX, visibleY, visibleWidth, visibleHeight, popup);
        }

        public void OnPopup(bool visible, int x, int y, int width, int height) => _window.Popup(visible, x, y, width, height);

        [SuppressMessage("Performance", "CA1859", Justification = "Contract factories return their interface under the project ownership rules.")]
        internal static IPlatformBrowserFrameSink Create(MacGpuWindow window, FrameStats stats) => new FrameSink(window, stats);
    }

    /// <summary>Counts the accelerated browser frames the fixture receives, so a missing-content failure says whether frames arrived.</summary>
    private sealed class FrameStats
    {
        internal int Frames { get; private set; }

        internal int LastWidth { get; private set; }

        internal int LastHeight { get; private set; }

        internal int LastFormat { get; private set; }

        internal bool LastPopup { get; private set; }

        internal void Record(int format, int width, int height, bool popup)
        {
            Frames++;
            (LastFormat, LastWidth, LastHeight, LastPopup) = (format, width, height, popup);
        }
    }
}
