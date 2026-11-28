// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Desktop;
using Denice.CrossPlatform.Windows.Desktop;
using Denice.CrossPlatform.Windows.Recording;
using Denice.TestHelpers;
using SkiaSharp;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using Xunit;
using Xunit.Abstractions;

namespace Denice.CrossPlatform.Tests;

/// <summary>Windows callback lifetime tests and opt-in hardware composition and encoding fixtures.</summary>
public sealed class DesktopGpuTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="DesktopGpuTests"/> class.</summary>
    /// <param name="output">The test evidence output.</param>
    public DesktopGpuTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AllocatorCleanupWaitsForNativeReleaseAfterInvoke()
    {
        TimeProvider clock = SystemServicesBuilder.Real().Build().Clock;
        using var slots = new SemaphoreSlim(0, 1);
        using var callback = new WindowsVideoRecorder.SampleRelease(slots, clock);
        Assert.Equal(2u, callback.AddRef());
        Task cleanup = callback.DisposeWhenReleasedAsync();
        Assert.False(cleanup.IsCompleted);
        Assert.True(callback.Invoke(null!).Success);
        Assert.Equal(1, slots.CurrentCount);
        Assert.False(cleanup.IsCompleted);
        Assert.Equal(1u, callback.Release());
        await cleanup.WaitAsync(TimeSpan.FromSeconds(5), clock);
        await cleanup;
        Assert.Throws<ObjectDisposedException>(() => slots.Wait(0));
    }

    [Fact]
    [SuppressMessage("CodeQuality", "S1215", Justification = "This regression must force collection to detect a native callback retained only by a weak GCHandle.")]
    [SuppressMessage("Reliability", "CA2000", Justification = "Semaphore ownership transfers to the callback cleanup task, which this test awaits.")]
    public async Task AllocatorCleanupRootsCallbackUntilNativeOwnershipEnds()
    {
        TimeProvider clock = SystemServicesBuilder.Real().Build().Clock;
        var slots = new SemaphoreSlim(0, 1);
        Task cleanup = BeginDetachedCleanup(slots, clock, out WeakReference<WindowsVideoRecorder.SampleRelease> reference);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.True(reference.TryGetTarget(out WindowsVideoRecorder.SampleRelease? callback));
        Assert.True(callback.Invoke(null!).Success);
        Assert.Equal(1u, callback.Release());
        await cleanup.WaitAsync(TimeSpan.FromSeconds(5), clock);
        await cleanup;
        Assert.Throws<ObjectDisposedException>(() => slots.Wait(0));
    }

    [Fact]
    [Trait("Category", "GpuHardware")]
    public void HardwareEncoderDecodesHeldFramesWithCorrectOrientationAndTimes()
    {
        ISystemServices services = SystemServicesBuilder.Real().Build();
        string directory = Path.Combine(services.Environment.GetBaseDirectory(), "hardware-fixture-output");
        services.FileSystem.GetDirectoryWriter().CreateDirectory(directory);
        string path = Path.Combine(directory, "hardware.mp4");
        _output.WriteLine($"Retained hardware encoder artifact: {path}");
        using (var context = WindowsGpuContext.Create())
        using (var recorder = WindowsVideoRecorder.Create(path, context.Device, 256, 256, services.Clock))
        {
            DrawRecordingPattern(context, bottomRight: false);
            recorder.AppendVideo(context.Presentation, 0);
            recorder.AppendVideo(context.Presentation, 29);
            DrawRecordingPattern(context, bottomRight: true);
            recorder.AppendVideo(context.Presentation, 30);
            recorder.AppendVideo(context.Presentation, 59);
            recorder.Finish();
            Assert.Equal(60, recorder.FramesWritten);
            Assert.False(string.IsNullOrEmpty(recorder.HardwareEncoder));
            _output.WriteLine($"Hardware H.264 encoder: {recorder.HardwareEncoder}. Encoded 60 samples from two distinct patterns, including held frames.");
        }

        AssertDecodedRecording(path);
    }

    [Theory]
    [InlineData(Format.R8G8B8A8_UNorm, SKColorType.Rgba8888)]
    [InlineData(Format.B8G8R8A8_UNorm, SKColorType.Bgra8888)]
    [Trait("Category", "GpuHardware")]
    public void ImportedTexturePreservesColorAndOrientation(Format format, SKColorType colorType)
    {
        using var window = WindowsWindow.Create(64, 64);
        using var context = WindowsGpuContext.Create();
        context.AttachWindow(window.Handle);
        using var pattern = new SKBitmap(new SKImageInfo(8, 8, colorType, SKAlphaType.Premul));
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                SKColor left = y < 4 ? SKColors.Red : SKColors.Blue;
                SKColor right = y < 4 ? SKColors.Lime : SKColors.Yellow;
                pattern.SetPixel(x, y, x < 4 ? left : right);
            }
        }

        var description = new Texture2DDescription
        {
            Width = 8,
            Height = 8,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
        };
        using ID3D11Texture2D texture = context.Device.CreateTexture2D(description, new SubresourceData(pattern.GetPixels(), (uint)pattern.RowBytes, 0));
        using (SKSurface target = context.CreatePresentationTarget(64, 64))
        {
            context.DrawTexture(texture, target.Canvas, new SKRect(0, 0, 8, 8), new SKRect(0, 0, 64, 64));
            AssertCornerColors(target, 64, 64);
        }

        SKSizeI size = window.GetClientSize();
        using (SKSurface target = context.CreateWindowTarget(size.Width, size.Height))
        {
            context.DrawTexture(context.Presentation, target.Canvas, new SKRect(0, 0, 64, 64), new SKRect(0, 0, size.Width, size.Height));
            AssertCornerColors(target, size.Width, size.Height);
            context.Present();
        }

        _output.WriteLine($"{format} imported with red/green/blue/yellow corners preserved through owned composition and window presentation.");
    }

    [Fact]
    [Trait("Category", "GpuHardware")]
    public void HardwareWindowSubmitsDistinctGpuFramesAndCloses()
    {
        using var window = WindowsWindow.Create(OrbScene.Width, OrbScene.Height);
        var context = WindowsGpuContext.Create();
        using (context)
        {
            context.AttachWindow(window.Handle);
            using var scene = OrbScene.Create(context.Skia);
            window.Pump();
            SKSizeI size = window.GetClientSize();
            Assert.True(size.Width > 0 && size.Height > 0);
            using SKBitmap first = DrawFrame(context, scene, size, 0);
            using SKBitmap second = DrawFrame(context, scene, size, 5);
            Assert.False(first.Bytes.AsSpan().SequenceEqual(second.Bytes));
            _output.WriteLine($"Hardware D3D11/ANGLE/Skia framebuffer contains distinct frames with successful Present calls at {size.Width}x{size.Height}; backend={context.Skia.Backend}.");
            _output.WriteLine(context.DescribeAdapter());
            _output.WriteLine("CEF texture import, audio and encoding were not exercised by this test.");
        }

        Assert.False(context.CleanupFailed);
        window.Dispose();
        Assert.False(window.IsOpen);
        Assert.False(window.CleanupFailed);
    }

    [SuppressMessage("Reliability", "CA2000", Justification = "The cleanup task owns and disposes the callback after the simulated native reference is released.")]
    private static Task BeginDetachedCleanup(SemaphoreSlim slots, TimeProvider clock, out WeakReference<WindowsVideoRecorder.SampleRelease> reference)
    {
        var callback = new WindowsVideoRecorder.SampleRelease(slots, clock);
        _ = callback.AddRef();
        reference = new WeakReference<WindowsVideoRecorder.SampleRelease>(callback);
        return callback.DisposeWhenReleasedAsync();
    }

    private static void DrawRecordingPattern(WindowsGpuContext context, bool bottomRight)
    {
        using SKSurface target = context.CreatePresentationTarget(256, 256);
        target.Canvas.Clear(SKColors.Black);
        using var paint = new SKPaint { Color = SKColors.White };
        int offset = bottomRight ? 128 : 0;
        target.Canvas.DrawRect(offset, offset, 128, 128, paint);
        target.Flush();
        context.Skia.Flush(true, true);
    }

    private static void AssertDecodedRecording(string path)
    {
        using var session = MfNative.OpenSession();
        using IMFSourceReader reader = MediaFactory.MFCreateSourceReaderFromURL(path, null!);
        using IMFMediaType type = MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12).CheckError();
        reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, type);
        using IMFMediaType actual = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
        Assert.Equal(MediaFactory.PackSize(256, 256), actual.GetUInt64(MediaTypeAttributeKeys.FrameSize));
        int samples = 0;
        bool ended = false;
        for (int reads = 0; reads < 120; reads++)
        {
            using IMFSample? sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out SourceReaderFlag flags, out long timestamp);
            if (sample is not null)
            {
                Assert.True(samples < 60, "Decoder returned more than the scheduled sixty samples.");
                long expected = samples * 10_000_000L / 60;
                Assert.InRange(timestamp, expected - 1, expected + 1);
                AssertDecodedPattern(sample, samples >= 30);
                samples++;
            }

            if ((flags & SourceReaderFlag.EndOfStream) != SourceReaderFlag.None)
            {
                ended = true;
                break;
            }
        }

        Assert.True(ended, "Decoder did not reach end of stream.");
        Assert.Equal(60, samples);
    }

    private static void AssertDecodedPattern(IMFSample sample, bool bottomRight)
    {
        using IMFMediaBuffer buffer = sample.GetBufferByIndex(0);
        using var layout = buffer.QueryInterface<IMF2DBuffer>();
        byte[] pixels = new byte[layout.ContiguousLength];
        layout.ContiguousCopyTo(pixels, pixels.Length);
        byte topLeft = pixels[(32 * 256) + 32];
        byte bottom = pixels[(224 * 256) + 224];
        Assert.InRange(bottomRight ? bottom : topLeft, 220, 245);
        Assert.InRange(bottomRight ? topLeft : bottom, 8, 28);
    }

    private static void AssertCornerColors(SKSurface target, int width, int height)
    {
        using var pixels = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        Assert.True(target.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, 0, 0));
        Assert.Equal(SKColors.Red, pixels.GetPixel(width / 8, height / 8));
        Assert.Equal(SKColors.Lime, pixels.GetPixel(width * 7 / 8, height / 8));
        Assert.Equal(SKColors.Blue, pixels.GetPixel(width / 8, height * 7 / 8));
        Assert.Equal(SKColors.Yellow, pixels.GetPixel(width * 7 / 8, height * 7 / 8));
    }

    private static SKBitmap DrawFrame(WindowsGpuContext context, OrbScene scene, SKSizeI size, double seconds)
    {
        using SKSurface target = context.CreateWindowTarget(size.Width, size.Height);
        SKCanvas canvas = target.Canvas;
        canvas.Clear(SKColors.Black);
        float scale = Math.Min(size.Width / (float)OrbScene.Width, size.Height / (float)OrbScene.Height);
        canvas.Translate((size.Width - (OrbScene.Width * scale)) / 2, (size.Height - (OrbScene.Height * scale)) / 2);
        canvas.Scale(scale);
        scene.Draw(canvas, seconds);
        target.Flush();
        context.Skia.Flush(true, true);
        var pixels = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (!target.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, 0, 0))
        {
            pixels.Dispose();
            throw new InvalidOperationException("GPU validation readback failed.");
        }

        context.Present();
        return pixels;
    }
}
