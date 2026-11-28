// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Desktop;
using SkiaSharp;
using Xilium.CefGlue;

namespace Denice.CrossPlatform.MacOS.Desktop;

/// <summary>Owns a Metal-backed Skia scene and composites Chromium GPU images above it.</summary>
[SuppressMessage("Design", "CA2216", Justification = "Cocoa and Metal resources must be released on the owning main thread; the desktop lifetime always disposes this window there.")]
internal sealed class MacGpuWindow : IDisposable
{
    /// <summary>The initial scene width in pixels.</summary>
    internal const int Width = OrbScene.Width;

    /// <summary>The initial scene height in pixels.</summary>
    internal const int Height = OrbScene.Height;

    private readonly nint _window;
    private readonly nint _view;
    private readonly nint _layer;
    private readonly nint _device;
    private readonly nint _queue;
    private readonly GRContext _context;
    private readonly SKSurface _scene;
    private readonly SKSurface _browser;
    private readonly SKSurface _popup;
    private readonly SKSurface _recording;
    private readonly OrbScene _orb;
    private SKSurface? _presentation;
    private int _presentationWidth;
    private int _presentationHeight;
    private SKRect _popupRect;
    private bool _popupVisible;
    private bool _disposed;

    private MacGpuWindow()
    {
        _device = MacNative.MTLCreateSystemDefaultDevice();
        if (_device == 0)
        {
            throw new InvalidOperationException("DENICE requires a Metal GPU.");
        }

        _queue = MacNative.Call(_device, "newCommandQueue");
        using var backend = new GRMtlBackendContext { DeviceHandle = _device, QueueHandle = _queue };
        _context = GRContext.CreateMetal(backend);
        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _scene = SKSurface.Create(_context, false, info);
        _browser = SKSurface.Create(_context, false, info);
        _popup = SKSurface.Create(_context, false, info);
        _browser.Canvas.Clear(SKColors.Transparent);
        _popup.Canvas.Clear(SKColors.Transparent);
        _window = MacNative.Window(MacNative.Call(MacNative.Class("NSWindow"), "alloc"), MacNative.Selector("initWithContentRect:styleMask:backing:defer:"), new MacNative.Rect(new MacNative.Point(120, 100), new MacNative.Size(Width, Height)), 15, 2, 0);
        MacNative.Call(_window, "setReleasedWhenClosed:", 0);
        MacNative.Call(_window, "setTitle:", MacNative.Text("DENICE — Native GPU Prototype"));
        _view = MacNative.Call(_window, "contentView");
        MacNative.Call(_view, "setWantsLayer:", 1);
        _layer = MacNative.Call(MacNative.Class("CAMetalLayer"), "new");
        MacNative.Call(_layer, "setDevice:", _device);
        MacNative.Call(_layer, "setPixelFormat:", 80);
        MacNative.Call(_layer, "setFramebufferOnly:", 0);
        MacNative.Call(_view, "setLayer:", _layer);
        MacNative.Call(_window, "setAcceptsMouseMovedEvents:", 1);
        MacNative.Call(_window, "center");
        MacNative.Call(_window, "makeKeyAndOrderFront:", 0);
        MacNative.GetRect(out MacNative.Rect initialBounds, _view, MacNative.Selector("bounds"));
        double backingScale = MacNative.GetDouble(_window, MacNative.Selector("backingScaleFactor"));
        RecordingWidth = EncoderAlignedPixels(initialBounds.Size.Width, backingScale);
        RecordingHeight = EncoderAlignedPixels(initialBounds.Size.Height, backingScale);
        _recording = SKSurface.Create(_context, false, new SKImageInfo(RecordingWidth, RecordingHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        _orb = OrbScene.Create(_context);
    }

    /// <summary>Gets the initial drawable pixel width, aligned to an even codec dimension.</summary>
    internal int RecordingWidth { get; }

    /// <summary>Gets the initial drawable pixel height, aligned to an even codec dimension.</summary>
    internal int RecordingHeight { get; }

    /// <summary>Gets a value indicating whether the window remains open.</summary>
    internal bool IsOpen => MacNative.GetBool(_window, MacNative.Selector("isVisible")) != 0 && !MacApplication.QuitRequested;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _context.Flush(true, true);
        _orb.Dispose();
        _presentation?.Dispose();
        _recording.Dispose();
        _popup.Dispose();
        _browser.Dispose();
        _scene.Dispose();
        _context.Dispose();
        MacNative.Call(_window, "close");
        MacNative.Release(_window);
        MacNative.Release(_layer);
        MacNative.Release(_queue);
        MacNative.Release(_device);
    }

    /// <summary>Provides the Create operation for the desktop compositor.</summary>
    /// <returns>The created object or native operation result.</returns>
    internal static MacGpuWindow Create() => new();

    /// <summary>Provides the Popup operation for the desktop compositor.</summary>
    /// <param name="visible">Whether the popup is visible.</param>
    /// <param name="x">The page-relative horizontal position.</param>
    /// <param name="y">The page-relative vertical position.</param>
    /// <param name="width">The pixel width.</param>
    /// <param name="height">The pixel height.</param>
    internal void Popup(bool visible, int x, int y, int width, int height)
    {
        _popupVisible = visible;
        _popupRect = SKRect.Create(x, y, width, height);
    }

    /// <summary>Provides the CopyBrowserSurface operation for the desktop compositor.</summary>
    /// <param name="surface">The borrowed IOSurface pointer.</param>
    /// <param name="format">The native texture pixel format.</param>
    /// <param name="width">The pixel width.</param>
    /// <param name="height">The pixel height.</param>
    /// <param name="visibleX">The source image horizontal offset.</param>
    /// <param name="visibleY">The source image vertical offset.</param>
    /// <param name="visibleWidth">The source visible pixel width.</param>
    /// <param name="visibleHeight">The source visible pixel height.</param>
    /// <param name="popup">Whether this image belongs to a popup.</param>
    internal void CopyBrowserSurface(nint surface, int format, int width, int height, int visibleX, int visibleY, int visibleWidth, int visibleHeight, bool popup)
    {
        if (surface == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        SKColorType colorType = (CefColorType)format switch
        {
            CefColorType.Bgra8888 => SKColorType.Bgra8888,
            CefColorType.Rgba8888 => SKColorType.Rgba8888,
            _ => throw new InvalidOperationException($"Unsupported Chromium GPU color format {format}."),
        };
        nuint metalFormat = colorType == SKColorType.Bgra8888 ? 80u : 70u;
        nint texture = ImportSurface(surface, metalFormat, width, height, 1);

        try
        {
            using var backend = new GRBackendTexture(width, height, false, new GRMtlTextureInfo(texture));
            using SKImage image = SKImage.FromTexture(_context, backend, GRSurfaceOrigin.TopLeft, colorType, SKAlphaType.Premul);
            SKSurface target = popup ? _popup : _browser;
            target.Canvas.Clear(SKColors.Transparent);
            SKRect source = SKRect.Create(visibleX, visibleY, visibleWidth > 0 ? visibleWidth : width, visibleHeight > 0 ? visibleHeight : height);
            SKRect destination = popup ? SKRect.Create(0, 0, _popupRect.Width, _popupRect.Height) : SKRect.Create(Width, Height);
            target.Canvas.DrawImage(image, source, destination, SKSamplingOptions.Default);
            target.Flush();
            _context.Flush(true, true);

            // The borrowed IOSurface may be reused as soon as this callback returns.
        }
        finally
        {
            MacNative.Release(texture);
        }
    }

    /// <summary>Provides the Render operation for the desktop compositor.</summary>
    /// <param name="seconds">The animation elapsed time in seconds.</param>
    internal void Render(double seconds)
    {
        SKCanvas canvas = _scene.Canvas;
        canvas.Clear(new SKColor(5, 8, 18));
        _orb.Draw(canvas, seconds);
        using (SKImage image = _browser.Snapshot())
        {
            canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        }

        if (_popupVisible)
        {
            using SKImage image = _popup.Snapshot();
            canvas.DrawImage(image, SKRect.Create(0, 0, _popupRect.Width, _popupRect.Height), _popupRect, SKSamplingOptions.Default);
        }

        _scene.Flush();
        MacNative.GetRect(out MacNative.Rect bounds, _view, MacNative.Selector("bounds"));
        double backingScale = MacNative.GetDouble(_window, MacNative.Selector("backingScaleFactor"));
        int width = EncoderAlignedPixels(bounds.Size.Width, backingScale);
        int height = EncoderAlignedPixels(bounds.Size.Height, backingScale);
        MacNative.SetSize(_layer, MacNative.Selector("setDrawableSize:"), new MacNative.Size(width, height));
        nint drawable = MacNative.Call(_layer, "nextDrawable");
        if (drawable == 0)
        {
            return;
        }

        nint texture = MacNative.Call(drawable, "texture");
        using var backend = new GRBackendTexture(width, height, false, new GRMtlTextureInfo(texture));
        using SKSurface destination = WrapSurface(backend);
        EnsurePresentationSurface(width, height);
        using (SKImage scene = _scene.Snapshot())
        {
            DrawAspectFit(_presentation!.Canvas, scene, width, height);
        }

        using (SKImage presentation = _presentation!.Snapshot())
        {
            destination.Canvas.DrawImage(presentation, 0, 0, SKSamplingOptions.Default);
        }

        destination.Flush();
        _context.Flush();
        nint command = MacNative.Call(_queue, "commandBuffer");
        MacNative.Call(command, "presentDrawable:", drawable);
        MacNative.Call(command, "commit");
    }

    /// <summary>Provides the ReportRecordingError operation for the desktop compositor.</summary>
    /// <param name="message">The recording error to display.</param>
    internal void ReportRecordingError(string message) => MacNative.Call(_window, "setTitle:", MacNative.Text("DENICE — Recording failed: " + message));

    /// <summary>Provides the ReadFrame operation for the desktop compositor.</summary>
    /// <param name="destination">The BGRA pixel destination.</param>
    /// <param name="stride">The destination bytes per row.</param>
    internal void ReadFrame(nint destination, int stride)
    {
        if (_presentation is null)
        {
            throw new IOException("No completed window frame is available for recording.");
        }

        using (SKImage presentation = _presentation.Snapshot())
        {
            DrawAspectFit(_recording.Canvas, presentation, RecordingWidth, RecordingHeight);
        }

        if (!_recording.ReadPixels(new SKImageInfo(RecordingWidth, RecordingHeight, SKColorType.Bgra8888, SKAlphaType.Premul), destination, stride, 0, 0))
        {
            throw new IOException("Cannot read the composed GPU frame for recording.");
        }
    }

    /// <summary>Copies the completed presentation directly into the encoder's GPU-backed buffer.</summary>
    /// <param name="pixelBuffer">The encoder-owned Core Video buffer.</param>
    internal void CopyRecordingFrame(nint pixelBuffer)
    {
        if (_presentation is null)
        {
            throw new IOException("No completed window frame is available for recording.");
        }

        nint surface = Recording.RecordingNative.CVPixelBufferGetIOSurface(pixelBuffer);
        nint texture = ImportSurface(surface, 80, RecordingWidth, RecordingHeight, 5);

        try
        {
            using var backend = new GRBackendTexture(RecordingWidth, RecordingHeight, false, new GRMtlTextureInfo(texture));
            using SKSurface target = WrapSurface(backend);
            using SKImage presentation = _presentation.Snapshot();
            DrawAspectFit(target.Canvas, presentation, RecordingWidth, RecordingHeight);
            target.Flush();
            _context.Flush(true, true);
        }
        finally
        {
            MacNative.Release(texture);
        }
    }

    /// <summary>Provides the HandleEvent operation for the desktop compositor.</summary>
    /// <param name="evt">The Cocoa input event.</param>
    /// <param name="browser">The browser receiving page input.</param>
    internal void HandleEvent(nint evt, IPlatformBrowser browser)
    {
        int type = (int)MacNative.Call(evt, "type");
        if (MacNative.Call(evt, "window") != _window)
        {
            return;
        }

        MacNative.Point point = MacNative.GetPoint(evt, MacNative.Selector("locationInWindow"));
        MacNative.GetRect(out MacNative.Rect bounds, _view, MacNative.Selector("bounds"));
        double scale = Math.Min(bounds.Size.Width / Width, bounds.Size.Height / Height);
        int x = (int)((point.X - ((bounds.Size.Width - (Width * scale)) / 2)) / scale);
        int y = (int)(((bounds.Size.Height - point.Y) - ((bounds.Size.Height - (Height * scale)) / 2)) / scale);
        switch (type)
        {
            case 1:
            case 2:
            case 3:
            case 4:
            case 25:
            case 26:
                browser.MouseClick(x, y, type switch { 3 or 4 => 2, 25 or 26 => 1, _ => 0 }, type is 2 or 4 or 26, (int)MacNative.Call(evt, "clickCount"));
                break;
            case 5:
            case 6:
            case 7:
            case 27:
                browser.MouseMove(x, y, false);
                break;
        }
    }

    private static int EncoderAlignedPixels(double points, double backingScale)
    {
        // H.264 chroma dimensions must be even; the drawable uses the same aligned size as recording.
        int pixels = Math.Max(2, checked((int)Math.Ceiling(points * backingScale)));
        return pixels + (pixels & 1);
    }

    private static void DrawAspectFit(SKCanvas canvas, SKImage image, int width, int height)
    {
        canvas.Clear(SKColors.Black);
        float scale = Math.Min(width / (float)image.Width, height / (float)image.Height);
        var target = SKRect.Create((width - (image.Width * scale)) / 2, (height - (image.Height * scale)) / 2, image.Width * scale, image.Height * scale);
        canvas.DrawImage(image, target, SKSamplingOptions.Default);
    }

    private SKSurface WrapSurface(GRBackendTexture texture) =>
        SKSurface.Create(_context, texture, GRSurfaceOrigin.TopLeft, SKColorType.Bgra8888);

    private nint ImportSurface(nint surface, nuint format, int width, int height, nint usage)
    {
        nint descriptor = MacNative.TextureDescriptor(MacNative.Class("MTLTextureDescriptor"), MacNative.Selector("texture2DDescriptorWithPixelFormat:width:height:mipmapped:"), format, (nuint)width, (nuint)height, 0);
        MacNative.Call(descriptor, "setUsage:", usage);
        MacNative.Call(descriptor, "setStorageMode:", 0);
        nint texture = MacNative.Send(_device, MacNative.Selector("newTextureWithDescriptor:iosurface:plane:"), descriptor, surface, 0);
        return texture != 0 ? texture : throw new IOException("Cannot import the shared IOSurface as a Metal texture.");
    }

    private void EnsurePresentationSurface(int width, int height)
    {
        if (_presentation is not null && _presentationWidth == width && _presentationHeight == height)
        {
            return;
        }

        _presentation?.Dispose();
        _presentation = SKSurface.Create(_context, false, new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        _presentationWidth = width;
        _presentationHeight = height;
    }
}
