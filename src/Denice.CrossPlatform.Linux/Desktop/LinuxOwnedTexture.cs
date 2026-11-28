// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using SkiaSharp;

namespace Denice.CrossPlatform.Linux.Desktop;

/// <summary>Owns a compositor texture whose lifetime can cover asynchronous recording.</summary>
[SuppressMessage("Design", "CA2216", Justification = "The compositor releases GL resources on the creating context thread after consumers complete.")]
internal sealed class LinuxOwnedTexture : IDisposable
{
    private readonly LinuxGpuContext _graphics;
    private GRBackendTexture? _backend;
    private SKSurface? _surface;
    private uint _texture;
    private int _consumers;

    private LinuxOwnedTexture(LinuxGpuContext graphics)
    {
        _graphics = graphics;
    }

    /// <summary>Gets the pixel width.</summary>
    internal int Width { get; private set; }

    /// <summary>Gets the pixel height.</summary>
    internal int Height { get; private set; }

    /// <summary>Gets the owned GLES texture name.</summary>
    internal uint Texture => _texture;

    /// <summary>Gets a value indicating whether an asynchronous GPU consumer still retains this image.</summary>
    internal bool IsRetained => Volatile.Read(ref _consumers) != 0;

    /// <summary>Gets the Skia render surface.</summary>
    internal SKSurface Surface => IsRetained
        ? throw new InvalidOperationException("A retained compositor texture cannot be rendered again.")
        : _surface ?? throw new ObjectDisposedException(nameof(LinuxOwnedTexture));

    /// <inheritdoc />
    [SuppressMessage("Design", "S3877", Justification = "Deleting a texture retained by GPU consumers would invalidate their native resources; the owner must drain them before disposal.")]
    public void Dispose()
    {
        if (_texture == 0)
        {
            return;
        }

        if (IsRetained)
        {
            throw new InvalidOperationException("GPU consumers must release the compositor texture before disposal.");
        }

        _graphics.Skia.Flush(true, true);
        GlesNative.Finish();
        _surface?.Dispose();
        _backend?.Dispose();
        GlesNative.DeleteTextures(1, in _texture);
        _texture = 0;
        _surface = null;
        _backend = null;
        _graphics.Skia.ResetContext();
    }

    /// <summary>Allocates a texture without CPU pixel storage.</summary>
    /// <param name="graphics">The creating graphics context.</param>
    /// <param name="width">The pixel width.</param>
    /// <param name="height">The pixel height.</param>
    /// <returns>The owned texture.</returns>
    internal static LinuxOwnedTexture Create(LinuxGpuContext graphics, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var texture = new LinuxOwnedTexture(graphics);
        try
        {
            texture.Initialize(width, height);
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    /// <summary>Completes producer work and retains the immutable image for an asynchronous consumer.</summary>
    /// <returns>A lease released only after the consumer completes its GPU reads.</returns>
    /// <remarks>Call on the graphics thread. Lease disposal may occur on a consumer thread and never calls GL.</remarks>
    internal IDisposable Retain()
    {
        ObjectDisposedException.ThrowIf(_texture == 0, this);
        _graphics.MakeCurrent();
        _graphics.Skia.Flush(true, true);
        GlesNative.Finish();
        if (GlesNative.GetError() != 0)
        {
            throw new InvalidOperationException("GLES could not complete the composition texture before recording.");
        }

        _ = Interlocked.Increment(ref _consumers);
        return new ConsumerLease(this);
    }

    private void Initialize(int width, int height)
    {
        Width = width;
        Height = height;
        const uint target = 0x0DE1;
        const uint rgba8 = 0x8058;
        GlesNative.GenTextures(1, out _texture);
        GlesNative.BindTexture(target, _texture);
        GlesNative.TexParameter(target, 0x2801, 0x2601);
        GlesNative.TexParameter(target, 0x2800, 0x2601);
        GlesNative.TexParameter(target, 0x2802, 0x812F);
        GlesNative.TexParameter(target, 0x2803, 0x812F);
        GlesNative.TextureStorage(target, 1, rgba8, width, height);
        uint error = GlesNative.GetError();
        if (_texture == 0 || error != 0)
        {
            throw new InvalidOperationException("GLES could not allocate the owned composition texture.");
        }

        _graphics.Skia.ResetContext();
        _backend = new GRBackendTexture(width, height, false, new GRGlTextureInfo(target, _texture, rgba8));
        _surface = SKSurface.Create(_graphics.Skia, _backend, GRSurfaceOrigin.TopLeft, SKColorType.Rgba8888);
        if (_surface.Handle == 0)
        {
            throw new InvalidOperationException("Skia could not wrap the owned composition texture.");
        }
    }

    [SuppressMessage("Usage", "CA2213", Justification = "The lease borrows its texture owner and releases only its reference count; GL disposal belongs to the creating thread.")]
    private sealed class ConsumerLease(LinuxOwnedTexture owner) : IDisposable
    {
        private LinuxOwnedTexture? _owner = owner;

        public void Dispose()
        {
            LinuxOwnedTexture? retained = Interlocked.Exchange(ref _owner, null);
            if (retained is not null)
            {
                _ = Interlocked.Decrement(ref retained._consumers);
            }
        }
    }
}
