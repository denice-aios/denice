// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Desktop;
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
}
