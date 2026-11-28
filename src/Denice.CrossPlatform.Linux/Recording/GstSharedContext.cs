// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Denice.CrossPlatform.Linux.Desktop;

namespace Denice.CrossPlatform.Linux.Recording;

/// <summary>Wraps the compositor context for GStreamer while retaining its external ownership.</summary>
[SuppressMessage("Design", "CA2216", Justification = "The graphics owner disposes the wrapper on its context thread before destroying EGL.")]
internal sealed class GstSharedContext : IDisposable
{
    private bool _active;

    /// <summary>Gets the owned GStreamer display wrapper.</summary>
    internal nint Display { get; private set; }

    /// <summary>Gets the owned GStreamer context wrapper.</summary>
    internal nint Context { get; private set; }

    /// <summary>Gets the shared context with a GStreamer-owned GL thread.</summary>
    internal nint ConsumerContext { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (ConsumerContext != 0)
        {
            GstNative.Unref(ConsumerContext);
            ConsumerContext = 0;
        }

        if (Context != 0)
        {
            if (_active)
            {
                _ = GstGlNative.Activate(Context, 0);
                _active = false;
            }

            GstNative.Unref(Context);
            Context = 0;
        }

        if (Display != 0)
        {
            GstNative.Unref(Display);
            Display = 0;
        }
    }

    /// <summary>Creates wrappers while the compositor context is current on this thread.</summary>
    /// <param name="graphics">The graphics owner which outlives this wrapper and all submitted frames.</param>
    /// <returns>The initialized wrapper.</returns>
    internal static GstSharedContext Create(LinuxGpuContext graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        var owner = new GstSharedContext();
        try
        {
            owner.Initialize(graphics);
            return owner;
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    private void Initialize(LinuxGpuContext graphics)
    {
        Display = GstGlNative.WrapDisplay(graphics.Display);
        if (Display == 0)
        {
            throw new InvalidOperationException("GStreamer could not wrap the compositor EGL display.");
        }

        const uint gles = 1U << 16;
        GstGlNative.FilterApi(Display, gles);
        Context = GstGlNative.WrapContext(Display, (nuint)graphics.NativeContext, 1, gles);
        if (Context == 0 || GstGlNative.Activate(Context, 1) == 0)
        {
            throw new InvalidOperationException("GStreamer could not activate the compositor context wrapper.");
        }

        _active = true;
        if (GstGlNative.FillInfo(Context, out nint error) == 0)
        {
            throw GstRuntime.ReadError(error, "GStreamer could not inspect the compositor GL capabilities.");
        }

        // A wrapped context has no GStreamer thread. Memory allocation and
        // downstream work must use a real context that shares its objects.
        ConsumerContext = GstGlNative.NewContext(Display);
        if (ConsumerContext == 0)
        {
            throw new InvalidOperationException("GStreamer could not allocate a shared recording context.");
        }

        if (GstGlNative.CreateContext(ConsumerContext, Context, out error) == 0)
        {
            throw GstRuntime.ReadError(error, "GStreamer could not create a shared recording context.");
        }

        if (GstGlNative.CanShare(ConsumerContext, Context) == 0)
        {
            throw new NotSupportedException("The GStreamer recording context cannot share compositor textures.");
        }
    }
}
