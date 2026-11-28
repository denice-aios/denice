// Copyright (c) Denice.ai. All rights reserved.

using System.Globalization;
using System.Text;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Browser;
using Denice.CrossPlatform.Desktop;
using Denice.CrossPlatform.Linux.Browser;
using Denice.CrossPlatform.Linux.Desktop;
using Denice.CrossPlatform.Linux.Recording;
using Denice.CrossPlatform.Posix;
using Denice.TestHelpers;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Denice.CrossPlatform.Tests;

/// <summary>Explicitly selected Linux hardware tests preceding browser and recording integration.</summary>
public sealed class DesktopGpuTests
{
    private const string CefImportScenario = "cef-import";
    private const string ProcessInformationRoot = "/proc";

    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="DesktopGpuTests"/> class.</summary>
    /// <param name="output">The hardware evidence log.</param>
    public DesktopGpuTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "GpuHardware")]
    public void HardwareWindowRendersDistinctFramebuffersAndPresents()
    {
        using var graphics = LinuxGpuContext.Create(OrbScene.Width, OrbScene.Height);
        using (var scene = OrbScene.Create(graphics.Skia))
        {
            graphics.Window.Pump();
            using SKBitmap first = DrawFrame(graphics, scene, 0);
            using SKBitmap second = DrawFrame(graphics, scene, 5);
            Assert.False(first.Bytes.AsSpan().SequenceEqual(second.Bytes));
            _output.WriteLine("GL renderer: " + graphics.Renderer);
            _output.WriteLine("EGL extensions: " + graphics.Extensions);
            _output.WriteLine("EGL client version: " + LinuxGpuContext.EglClientVersion);
            _output.WriteLine("EGL client extensions: " + LinuxGpuContext.EglClientExtensions);
            _output.WriteLine("Distinct GPU framebuffers and successful EGL presentation calls; displayed scanout, CEF import, audio and encoding were not tested.");
        }

        graphics.Dispose();
        Assert.False(graphics.CleanupFailed);
    }

    [Fact]
    [Trait("Category", "GpuHardware")]
    public void RecordingDestinationCreatesExclusivelyAndPreservesExistingBytes()
    {
        ISystemServices services = SystemServicesBuilder.Real().Build();
        string directory = services.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-linux-recording-");
        try
        {
            string existing = Path.Combine(directory, "existing.mp4");
            services.FileSystem.GetFileWriter().WriteAllText(existing, "existing recording");
            Assert.Throws<IOException>(() => PosixFileSystem.CreateRecordingFile(existing));
            Assert.Equal("existing recording", services.FileSystem.GetFileReader().ReadAllText(existing));
            string created = Path.Combine(directory, "new.mp4");
            using (var handle = PosixFileSystem.CreateRecordingFile(created))
            {
                Assert.False(handle.IsInvalid);
                Assert.Throws<IOException>(() => PosixFileSystem.CreateRecordingFile(created));
            }

            Assert.Empty(services.FileSystem.GetFileReader().ReadAllBytes(created));
            string missingParent = Path.Combine(directory, "missing", "output.mp4");
            Assert.Throws<IOException>(() => PosixFileSystem.CreateRecordingFile(missingParent));
            Assert.False(services.FileSystem.GetFileReader().Exists(missingParent));
        }
        finally
        {
            services.FileSystem.GetDirectoryWriter().DeleteDirectory(directory, true);
        }
    }

    [Fact]
    [Trait("Category", "GpuHardware")]
    public void HardwareEncoderFinalizesGpuFramesWithOriginalTimestamps()
    {
        ISystemServices services = SystemServicesBuilder.Real().Build();
        string baseDirectory = services.Environment.GetBaseDirectory();
        string directory = Path.Combine(baseDirectory, "hardware-fixture-output");
        services.FileSystem.GetDirectoryWriter().CreateDirectory(directory);
        string path = Path.Combine(directory, "linux-hardware.mp4");
        _output.WriteLine("Retained hardware encoder artifact: " + path);
        using var graphics = LinuxGpuContext.Create(256, 256);
        using var texture = LinuxOwnedTexture.Create(graphics, 256, 256);
        using (var recorder = LinuxVideoRecorder.Create(graphics, baseDirectory, path, 256, 256))
        {
            long started = services.Clock.GetTimestamp();
            for (int frame = 0; frame < 60; frame++)
            {
                DrawRecordingPattern(texture.Surface.Canvas, frame >= 30);
                ulong timestamp = 2_000_000_000 + ((ulong)frame * 20_000_000)
                    + (frame >= 30 ? 400_000_000ul : 0);
                while (!recorder.TryAppend(texture, timestamp, 20_000_000))
                {
                    Assert.True(services.Clock.GetElapsedTime(started) < TimeSpan.FromSeconds(30), "GPU encoder did not accept its pending frame within 30 seconds.");
                    graphics.Window.Pump();
                    Thread.Yield();
                }
            }

            recorder.Finish();
            Assert.True(recorder.Finished);
            Assert.Equal(60ul, recorder.SubmittedFrames);
            Assert.False(string.IsNullOrEmpty(recorder.HardwareEncoder));
            _output.WriteLine("Hardware H.264 encoder: " + recorder.HardwareEncoder);
            _output.WriteLine("Submitted 60 GPU frames: PTS 2.00 through 2.58 seconds, then 3.00 through 3.58 seconds; duration 0.02 seconds each. Quadrants switch at frame 30. MP4 decode must verify the retained artifact separately.");
        }

        Assert.False(texture.IsRetained);
        Assert.NotEmpty(services.FileSystem.GetFileReader().ReadAllBytes(path));
    }

    [Fact]
    [Trait("Category", "GpuHardware")]
    public async Task CefDmaBufFramesImportThroughLinuxSharedFrameAndKeepTheirColors()
    {
        // CEF must run on the main application thread, so the scenario runs in a bounded child of this executable.
        ISystemServices services = SystemServicesBuilder.Real().Build();
        string directory = Path.Combine(services.Environment.GetBaseDirectory(), "hardware-fixture-output");
        int exitCode = await FixtureChildProcess.RunAsync(services, _output, CefImportScenario, directory, [], (processId, artifacts) => CaptureThreadStatesAsync(services, processId, artifacts));
        Assert.Equal(0, exitCode);
    }

    /// <summary>Runs only the selected CEF fixture, on the OS main thread.</summary>
    private static int Main(string[] args)
    {
        ISystemServices services = SystemServicesBuilder.Real().Build();
        FixtureChildProcess.Begin(services);
        if (args.Length != 2 || !string.Equals(args[0], CefImportScenario, StringComparison.Ordinal))
        {
            return 2;
        }

        FixtureChildProcess.Stage(services, "start " + args[0]);
        AssertCefImport(services, args[1]);
        services.Console.WriteLine("Native fixture completed: " + args[0]);
        return 0;
    }

    private static void AssertCefImport(ISystemServices services, string output)
    {
        // Real CEF offscreen with shared textures: every accelerated frame goes through the published LinuxSharedFrame
        // import (DMA-BUF to EGLImage to an owned GLES texture), and the imported pixels are read back and checked.
        const string html = "<html><body style='margin:0;background:#000'><button onclick=\"this.style.background='lime'\" style='border:0;background:red;width:150px;height:80px'>Click</button></body></html>";
        using var graphics = LinuxGpuContext.Create(OrbScene.Width, OrbScene.Height);
        using var importer = new LinuxSharedFrame(graphics);
        var stats = new ImportStats();
        FixtureChildProcess.Stage(services, "browser create");
        IPlatformBrowser browser = CefPlatformBrowser.Create();
        try
        {
            browser.Start(new Uri("data:text/html," + Uri.EscapeDataString(html)), OrbScene.Width, OrbScene.Height, ImportingFrameSink.Create(importer, stats), SilentAudioSink.Create(), Path.Combine(output, "cef-import-cache"), 0);
            FixtureChildProcess.Stage(services, "browser started");
            WaitForImportedColor(services, graphics, browser, importer, stats, red: true, Path.Combine(output, "cef-import-red.png"));
            FixtureChildProcess.Stage(services, "red button imported");
            Assert.True(browser.IsReady);
            browser.MouseMove(40, 40, false);
            browser.MouseClick(40, 40, 0, false, 1);
            browser.MouseClick(40, 40, 0, true, 1);
            WaitForImportedColor(services, graphics, browser, importer, stats, red: false, Path.Combine(output, "cef-import-green.png"));
            services.Console.WriteLine($"CEF DMA-BUF frames imported through LinuxSharedFrame: {stats.Imports}; last coded size {stats.LastWidth}x{stats.LastHeight}, CEF format {stats.LastFormat}, planes {stats.LastPlanes}, modifier 0x{stats.LastModifier:x}.");
            services.Console.WriteLine("Imported pixels read back: the page's red button, then green after a click. Displayed scanout, audio and encoding of browser frames were not tested.");
        }
        finally
        {
            CloseBrowser(services, graphics, browser);
        }

        Assert.Null(stats.Failure);
    }

    /// <summary>Records every thread of the timed-out child (its <c>/proc/&lt;pid&gt;/task/*/stat</c> line: name, state and
    /// CPU times) before it is killed. Best effort: it never throws, and a failure is written into the report.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Capturing thread states is best effort and must never replace the fixture's timeout failure or prevent its cleanup.")]
    private static async Task CaptureThreadStatesAsync(ISystemServices services, int processId, string directory)
    {
        var report = new StringBuilder();
        try
        {
            string tasks = Path.Combine(ProcessInformationRoot, processId.ToString(CultureInfo.InvariantCulture), "task");
            foreach (string task in services.FileSystem.GetDirectoryReader().EnumerateChildren(tasks).Select(child => child.FullPath).Order(StringComparer.Ordinal))
            {
                try
                {
                    _ = report.Append(await services.FileSystem.GetFileReader().ReadAllTextAsync(Path.Combine(task, "stat"), CancellationToken.None).ConfigureAwait(false));
                }
                catch (IOException exception)
                {
                    // A thread can exit between listing and reading; the others are still recorded.
                    _ = report.Append(task).Append(": ").Append(exception.Message).Append('\n');
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _ = report.Append("Reading the thread states failed: ").Append(exception.Message).Append('\n');
        }

        try
        {
            await services.FileSystem.GetFileWriter().WriteAllBytesAsync(Path.Combine(directory, "thread-states.txt"), Encoding.UTF8.GetBytes(report.ToString()), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The report is diagnostic only; the timeout failure and fixture cleanup proceed without it.
        }
    }

    private static void CloseBrowser(ISystemServices services, LinuxGpuContext graphics, IPlatformBrowser browser)
    {
        // CEF and the GLES context share this thread, so closure is pumped here rather than awaited.
        browser.RequestClose();
        long closing = services.Clock.GetTimestamp();
        while (!browser.IsClosed && services.Clock.GetElapsedTime(closing).TotalSeconds < 10)
        {
            graphics.Window.Pump();
            browser.Pump();
            Task.Delay(TimeSpan.FromMilliseconds(5), services.Clock).GetAwaiter().GetResult();
        }

        Assert.True(browser.IsClosed, "CEF must complete browser closure before shutdown.");
        browser.Shutdown();
    }

    private static void WaitForImportedColor(ISystemServices services, LinuxGpuContext graphics, IPlatformBrowser browser, LinuxSharedFrame importer, ImportStats stats, bool red, string evidence)
    {
        long start = services.Clock.GetTimestamp();
        int bestMatches = 0;
        SKBitmap? last = null;
        try
        {
            while (services.Clock.GetElapsedTime(start).TotalSeconds < 15)
            {
                graphics.Window.Pump();
                browser.Pump();
                if (stats.Failure is { } failure)
                {
                    throw new InvalidOperationException("Importing a CEF frame through LinuxSharedFrame failed.", failure);
                }

                if (stats.Imports > 0)
                {
                    last?.Dispose();
                    last = ReadImported(graphics, importer);
                    int matches = CountColor(last, red);
                    bestMatches = Math.Max(bestMatches, matches);
                    if (matches > 500)
                    {
                        SavePng(services, last, evidence);
                        return;
                    }
                }

                Task.Delay(TimeSpan.FromMilliseconds(5), services.Clock).GetAwaiter().GetResult();
            }

            if (last is not null)
            {
                SavePng(services, last, evidence);
            }

            Assert.Fail((red ? "The imported browser frame never showed the red button." : "After the click the imported browser frame never showed the green button.")
                + $" Frames imported: {stats.Imports} (last {stats.LastWidth}x{stats.LastHeight}, format {stats.LastFormat}, planes {stats.LastPlanes}); page ready: {browser.IsReady}; most matching pixels in one readback: {bestMatches}.");
        }
        finally
        {
            last?.Dispose();
        }
    }

    private static SKBitmap ReadImported(LinuxGpuContext graphics, LinuxSharedFrame importer)
    {
        SKSurface surface = importer.Surface;
        SKRectI bounds = surface.Canvas.DeviceClipBounds;
        graphics.Skia.Flush(true, true);
        var bitmap = new SKBitmap(new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (!surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
        {
            bitmap.Dispose();
            throw new InvalidOperationException("Reading back the imported browser frame failed.");
        }

        return bitmap;
    }

    private static int CountColor(SKBitmap bitmap, bool red)
    {
        int matches = 0;
        ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
        for (int index = 0; index + 3 < pixels.Length; index += 4)
        {
            // RGBA8888: red is (255, 0, 0); lime is (0, 255, 0).
            byte first = pixels[index];
            byte second = pixels[index + 1];
            if (pixels[index + 2] < 35 && (red ? first > 220 && second < 35 : first < 35 && second > 220))
            {
                matches++;
            }
        }

        return matches;
    }

    private static void SavePng(ISystemServices services, SKBitmap bitmap, string path)
    {
        using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        services.FileSystem.GetFileWriter().WriteAllBytesAsync(path, png.ToArray(), CancellationToken.None).GetAwaiter().GetResult();
    }

    private static void DrawRecordingPattern(SKCanvas canvas, bool reverse)
    {
        canvas.Clear(SKColors.Black);
        using var paint = new SKPaint { IsAntialias = false };
        paint.Color = reverse ? SKColors.Yellow : SKColors.Red;
        canvas.DrawRect(0, 0, 128, 128, paint);
        paint.Color = reverse ? SKColors.Blue : SKColors.Lime;
        canvas.DrawRect(128, 0, 128, 128, paint);
        paint.Color = reverse ? SKColors.Lime : SKColors.Blue;
        canvas.DrawRect(0, 128, 128, 128, paint);
        paint.Color = reverse ? SKColors.Red : SKColors.Yellow;
        canvas.DrawRect(128, 128, 128, 128, paint);
    }

    private static SKBitmap DrawFrame(LinuxGpuContext graphics, OrbScene scene, double seconds)
    {
        using SKSurface surface = graphics.CreateWindowTarget();
        SKSizeI size = graphics.Window.Size;
        surface.Canvas.Clear(SKColors.Black);
        float scale = Math.Min(size.Width / (float)OrbScene.Width, size.Height / (float)OrbScene.Height);
        surface.Canvas.Translate((size.Width - (OrbScene.Width * scale)) / 2, (size.Height - (OrbScene.Height * scale)) / 2);
        surface.Canvas.Scale(scale);
        scene.Draw(surface.Canvas, seconds);
        surface.Flush();
        graphics.Skia.Flush(true, true);
        var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (!surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
        {
            bitmap.Dispose();
            throw new InvalidOperationException("Linux GPU validation readback failed.");
        }

        graphics.Present();
        return bitmap;
    }

    /// <summary>What the importing frame sink saw.</summary>
    private sealed class ImportStats
    {
        internal int Imports { get; set; }

        internal int LastWidth { get; set; }

        internal int LastHeight { get; set; }

        internal int LastFormat { get; set; }

        internal int LastPlanes { get; set; }

        internal ulong LastModifier { get; set; }

        internal Exception? Failure { get; set; }
    }

    /// <summary>Imports every main-layer CEF frame through the published LinuxSharedFrame path, as the Linux desktop does.</summary>
    private sealed class ImportingFrameSink : IPlatformBrowserFrameSink
    {
        private readonly LinuxSharedFrame _importer;
        private readonly ImportStats _stats;

        private ImportingFrameSink(LinuxSharedFrame importer, ImportStats stats)
        {
            _importer = importer;
            _stats = stats;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A native Chromium callback cannot propagate a managed exception; the fixture rethrows it from its pump loop.")]
        public void OnAcceleratedFrame(scoped in BrowserGpuBuffer buffer, int format, int width, int height, int visibleX, int visibleY, int visibleWidth, int visibleHeight, bool popup)
        {
            if (popup)
            {
                return;
            }

            try
            {
                _importer.Copy(in buffer, format, width, height);
                _stats.Imports++;
                _stats.LastWidth = width;
                _stats.LastHeight = height;
                _stats.LastFormat = format;
                _stats.LastPlanes = buffer.Planes.Length;
                _stats.LastModifier = buffer.Modifier;
            }
            catch (Exception error)
            {
                _stats.Failure ??= error;
            }
        }

        public void OnPopup(bool visible, int x, int y, int width, int height)
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Contract fakes are created behind their interface (DN0003, DN0006).")]
        internal static IPlatformBrowserFrameSink Create(LinuxSharedFrame importer, ImportStats stats) => new ImportingFrameSink(importer, stats);
    }

    /// <summary>Linux CEF keeps page audio itself (no capture handler), so this sink is never called.</summary>
    private sealed class SilentAudioSink : IPlatformBrowserAudioSink
    {
        private SilentAudioSink()
        {
        }

        public void OnAudioStarted(int channels, int sampleRate)
        {
        }

        public void OnAudioPacket(nint planarFloatPointers, int frames, long pts)
        {
        }

        public void OnAudioStopped()
        {
        }

        public void OnAudioError(string message)
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Contract fakes are created behind their interface (DN0003, DN0006).")]
        internal static IPlatformBrowserAudioSink Create() => new SilentAudioSink();
    }
}
