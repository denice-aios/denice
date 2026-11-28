// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Linux.Desktop;
using SkiaSharp;

namespace Denice.CrossPlatform.Linux.Browser;

/// <summary>Copies callback-borrowed DMA-BUF images into an owned Skia GPU surface.</summary>
[SuppressMessage("Security", "S6640", Justification = "EGL extension entry points use their published C ABI and checked pointers.")]
internal sealed unsafe class LinuxSharedFrame : IDisposable
{
    private readonly LinuxGpuContext _graphics;
    private readonly delegate* unmanaged<nint, nint, int, nint, int*, nint> _createImage;
    private readonly delegate* unmanaged<nint, nint, int> _destroyImage;
    private readonly delegate* unmanaged<uint, nint, void> _imageTarget;
    private SKSurface? _surface;

    /// <summary>Initializes a new instance of the <see cref="LinuxSharedFrame"/> class.</summary>
    /// <param name="graphics">The borrowed graphics owner, which must outlive this importer.</param>
    internal LinuxSharedFrame(LinuxGpuContext graphics)
    {
        _graphics = graphics;
        graphics.RequireExtension("EGL_KHR_image_base");
        graphics.RequireGlExtension("GL_OES_EGL_image");
        graphics.RequireExtension("EGL_EXT_image_dma_buf_import");
        graphics.RequireExtension("EGL_EXT_image_dma_buf_import_modifiers");
        _createImage = (delegate* unmanaged<nint, nint, int, nint, int*, nint>)RequireProc("eglCreateImageKHR");
        _destroyImage = (delegate* unmanaged<nint, nint, int>)RequireProc("eglDestroyImageKHR");
        _imageTarget = (delegate* unmanaged<uint, nint, void>)RequireProc("glEGLImageTargetTexture2DOES");
    }

    /// <summary>Gets the owned image from the most recent successful copy.</summary>
    internal SKSurface Surface => _surface ?? throw new InvalidOperationException("No Linux browser frame has arrived.");

    /// <inheritdoc />
    public void Dispose()
    {
        _surface?.Dispose();
        _surface = null;
    }

    /// <summary>Copies every plane and finishes the GPU source read before returning.</summary>
    /// <param name="buffer">The borrowed native pixmap.</param>
    /// <param name="format">The CEF color format.</param>
    /// <param name="width">The coded width.</param>
    /// <param name="height">The coded height.</param>
    internal void Copy(scoped in BrowserGpuBuffer buffer, int format, int width, int height)
    {
        int[] attributes = LinuxDmaBufAttributes.Create(in buffer, format, width, height);
        _graphics.MakeCurrent();
        nint image;
        fixed (int* pointer = attributes)
        {
            image = _createImage(_graphics.Display, 0, 0x3270, 0, pointer);
        }

        if (image == 0)
        {
            throw new InvalidOperationException("EGL rejected the CEF DMA-BUF format, planes or modifier.");
        }

        uint texture = 0;
        bool submitted = false;
        try
        {
            BindImage(image, out texture);

            _graphics.Skia.ResetContext();
            using var backend = new GRBackendTexture(width, height, false, new GRGlTextureInfo(0x0DE1, texture, 0x8058));
            using SKImage source = SKImage.FromTexture(_graphics.Skia, backend, GRSurfaceOrigin.TopLeft, SKColorType.Rgba8888, SKAlphaType.Premul)
                ?? throw new InvalidOperationException("Skia could not wrap the imported DMA-BUF texture.");
            EnsureSurface(width, height);
            Surface.Canvas.Clear(SKColors.Transparent);
            submitted = true;
            Surface.Canvas.DrawImage(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            Surface.Flush();
            _graphics.Skia.Flush(true, true);
        }
        finally
        {
            // Even an exception after draw must complete the borrowed source read.
            // Finish is a synchronization boundary, not a CPU pixel readback.
            try
            {
                if (submitted)
                {
                    DrainBorrowedRead();
                }
            }
            finally
            {
                if (texture != 0)
                {
                    GlesNative.DeleteTextures(1, in texture);
                }

                _ = _destroyImage(_graphics.Display, image);
                if (!_graphics.Skia.IsAbandoned)
                {
                    _graphics.Skia.ResetContext();
                }
            }
        }
    }

    private static nint RequireProc(string name)
    {
        nint pointer = EglNative.GetProcAddress(name);
        return pointer != 0 ? pointer : throw new NotSupportedException("EGL did not expose " + name + ".");
    }

    private void BindImage(nint image, out uint texture)
    {
        GlesNative.GenTextures(1, out texture);
        GlesNative.BindTexture(0x0DE1, texture);
        GlesNative.TexParameter(0x0DE1, 0x2801, 0x2601);
        GlesNative.TexParameter(0x0DE1, 0x2800, 0x2601);
        GlesNative.TexParameter(0x0DE1, 0x2802, 0x812F);
        GlesNative.TexParameter(0x0DE1, 0x2803, 0x812F);
        _imageTarget(0x0DE1, image);
        if (texture == 0 || GlesNative.GetError() != 0)
        {
            throw new InvalidOperationException("GLES cannot sample this DMA-BUF as a two-dimensional texture.");
        }
    }

    private void DrainBorrowedRead()
    {
        try
        {
            // Skia can retain DrawImage until flush. glFinish alone does not
            // submit that deferred work, including on the exceptional path.
            _graphics.Skia.Flush(true, true);
        }
        catch
        {
            // Discard any remaining deferred references after a failed flush.
            // This context is no longer usable by the desktop after this error.
            _graphics.Skia.AbandonContext(false);
            throw;
        }
        finally
        {
            GlesNative.Finish();
        }
    }

    private void EnsureSurface(int width, int height)
    {
        if (_surface is not null && _surface.Canvas.DeviceClipBounds.Width == width && _surface.Canvas.DeviceClipBounds.Height == height)
        {
            return;
        }

        SKSurface replacement = SKSurface.Create(_graphics.Skia, false, new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (replacement.Handle == 0)
        {
            replacement.Dispose();
            throw new InvalidOperationException("Skia could not allocate the owned Linux browser frame.");
        }

        _surface?.Dispose();
        _surface = replacement;
    }
}
