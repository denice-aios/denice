// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Desktop;
using SkiaSharp;

namespace Denice.CrossPlatform.Linux.Desktop;

/// <summary>Owns an X11 window while its display connection remains borrowed.</summary>
[SuppressMessage("Design", "CA2216", Justification = "X11 resources are released by the owning desktop thread.")]
internal sealed class LinuxWindow : IDisposable
{
    private readonly nint _display;
    private readonly nuint _colormap;
    private readonly nuint _protocols;
    private readonly nuint _deleteWindow;
    private bool _disposed;
    private bool _destroyed;
    private IPlatformBrowser? _browser;

    private LinuxWindow(nint display, nuint handle, nuint colormap, int width, int height)
    {
        _display = display;
        Handle = handle;
        _colormap = colormap;
        Size = new SKSizeI(width, height);
        _protocols = X11Native.InternAtom(display, "WM_PROTOCOLS", 0);
        _deleteWindow = X11Native.InternAtom(display, "WM_DELETE_WINDOW", 0);
    }

    /// <summary>Gets the drawable X11 window identifier.</summary>
    internal nuint Handle { get; }

    /// <summary>Gets the latest drawable dimensions.</summary>
    internal SKSizeI Size { get; private set; }

    /// <summary>Gets a value indicating whether the owner should begin ordered shutdown.</summary>
    internal bool CloseRequested { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_destroyed)
        {
            _ = X11Native.DestroyWindow(_display, Handle);
        }

        _ = X11Native.FreeColormap(_display, _colormap);
        _disposed = true;
    }

    /// <summary>Creates the X11 window using the visual selected by EGL.</summary>
    /// <param name="display">The borrowed X11 connection.</param>
    /// <param name="visualId">The EGL configuration's native visual.</param>
    /// <param name="width">Initial drawable width.</param>
    /// <param name="height">Initial drawable height.</param>
    /// <returns>The window owner.</returns>
    internal static LinuxWindow Create(nint display, int visualId, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        X11Native.VisualInfo visual = FindVisual(display, visualId);
        nuint root = X11Native.RootWindow(display, visual.Screen);
        nuint colormap = X11Native.CreateColormap(display, root, visual.Visual, 0);
        nuint handle = 0;
        try
        {
            // Pointer buttons/motion/leave and structure notifications. The window
            // manager receives WM_DELETE_WINDOW so the GPU owner can release first.
            var attributes = new X11Native.WindowAttributes { Colormap = colormap, EventMask = (1 << 2) | (1 << 3) | (1 << 5) | (1 << 6) | (1 << 17) };
            handle = X11Native.CreateWindow(display, root, 0, 0, (uint)width, (uint)height, 0, visual.Depth, 1, visual.Visual, (1U << 11) | (1U << 13), ref attributes);
            if (handle == 0)
            {
                throw new InvalidOperationException("X11 could not create the desktop window.");
            }

            var window = new LinuxWindow(display, handle, colormap, width, height);
            nuint protocol = window._deleteWindow;
            if (X11Native.SetProtocols(display, handle, ref protocol, 1) == 0)
            {
                throw new InvalidOperationException("X11 could not register ordered window shutdown.");
            }

            _ = X11Native.StoreName(display, handle, "DENICE — Native GPU Prototype");
            _ = X11Native.MapWindow(display, handle);
            _ = X11Native.Flush(display);
            return window;
        }
        catch
        {
            if (handle != 0)
            {
                _ = X11Native.DestroyWindow(display, handle);
            }

            _ = X11Native.FreeColormap(display, colormap);
            throw;
        }
    }

    /// <summary>Attaches the browser owned by the desktop lifetime.</summary>
    /// <param name="browser">The borrowed browser.</param>
    internal void AttachBrowser(IPlatformBrowser browser) => _browser = browser;

    /// <summary>Processes pending X11 events without blocking the CEF pump.</summary>
    internal void Pump()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (X11Native.Pending(_display) != 0)
        {
            _ = X11Native.NextEvent(_display, out X11Native.Event value);
            if (value.Type == 22 && value.MessageType == Handle)
            {
                Size = new SKSizeI(value.Width, value.Height);
            }
            else if (value.Type == 33 && value.Window == Handle && value.MessageType == _protocols && value.Format == 32 && (nuint)value.Data == _deleteWindow)
            {
                CloseRequested = true;
            }
            else if (value.Type == 17 && value.MessageType == Handle)
            {
                _destroyed = true;
                CloseRequested = true;
            }
            else if (value.Window == Handle && _browser is not null)
            {
                SendPointer(value);
            }
        }
    }

    private static X11Native.VisualInfo FindVisual(nint display, int visualId)
    {
        var template = new X11Native.VisualInfo { Id = (nuint)(uint)visualId };
        nint found = X11Native.GetVisualInfo(display, 1, ref template, out int count);
        if (found == 0)
        {
            throw new InvalidOperationException("X11 did not provide the EGL configuration's visual.");
        }

        X11Native.VisualInfo visual;
        try
        {
            if (count != 1)
            {
                throw new InvalidOperationException("The EGL native visual did not resolve uniquely.");
            }

            visual = Marshal.PtrToStructure<X11Native.VisualInfo>(found);
        }
        finally
        {
            _ = X11Native.Free(found);
        }

        return visual;
    }

    private void SendPointer(X11Native.Event value)
    {
        if (_browser is null)
        {
            return;
        }

        double scale = Math.Min(Size.Width / (double)OrbScene.Width, Size.Height / (double)OrbScene.Height);
        if (scale <= 0)
        {
            return;
        }

        int x = (int)((value.X - ((Size.Width - (OrbScene.Width * scale)) / 2)) / scale);
        int y = (int)((value.Y - ((Size.Height - (OrbScene.Height * scale)) / 2)) / scale);
        switch (value.Type)
        {
            case 6:
                _browser.MouseMove(x, y, false);
                break;
            case 8:
                _browser.MouseMove(-1, -1, true);
                break;
            case 4 or 5 when value.Button is >= 1 and <= 3:
                _browser.MouseClick(x, y, (int)value.Button - 1, value.Type == 5, 1);
                break;
            case 4 when value.Button is >= 4 and <= 7:
                _browser.MouseWheel(x, y, value.Button switch { 6 => -120, 7 => 120, _ => 0 }, value.Button switch { 4 => 120, 5 => -120, _ => 0 });
                break;
        }
    }
}
