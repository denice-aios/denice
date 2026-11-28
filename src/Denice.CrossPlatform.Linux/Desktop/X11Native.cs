// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Desktop;

/// <summary>Binds the system libX11.so.6 native API.</summary>
internal static partial class X11Native
{
    /// <summary>Invokes XOpenDisplay.</summary>
    /// <param name="name">The native name argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XOpenDisplay")]
    internal static partial nint OpenDisplay(nint name);

    /// <summary>Invokes XCloseDisplay.</summary>
    /// <param name="display">The native display argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    internal static partial int CloseDisplay(nint display);

    /// <summary>Invokes XRootWindow.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="screen">The selected visual screen.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XRootWindow")]
    internal static partial nuint RootWindow(nint display, int screen);

    /// <summary>Invokes XGetVisualInfo.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="mask">The native mask argument.</param>
    /// <param name="template">The native template argument.</param>
    /// <param name="count">The native count argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XGetVisualInfo")]
    internal static partial nint GetVisualInfo(nint display, nint mask, ref VisualInfo template, out int count);

    /// <summary>Invokes XFree.</summary>
    /// <param name="data">The native data argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XFree")]
    internal static partial int Free(nint data);

    /// <summary>Invokes XCreateColormap.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="window">The native window argument.</param>
    /// <param name="visual">The native visual argument.</param>
    /// <param name="allocate">The native allocate argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XCreateColormap")]
    internal static partial nuint CreateColormap(nint display, nuint window, nint visual, int allocate);

    /// <summary>Invokes XFreeColormap.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="colormap">The native colormap argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XFreeColormap")]
    internal static partial int FreeColormap(nint display, nuint colormap);

    /// <summary>Invokes XCreateWindow.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="parent">The native parent argument.</param>
    /// <param name="x">The native x argument.</param>
    /// <param name="y">The native y argument.</param>
    /// <param name="width">The native width argument.</param>
    /// <param name="height">The native height argument.</param>
    /// <param name="border">The native border argument.</param>
    /// <param name="depth">The native depth argument.</param>
    /// <param name="windowClass">The native windowClass argument.</param>
    /// <param name="visual">The native visual argument.</param>
    /// <param name="valueMask">The native valueMask argument.</param>
    /// <param name="attributes">The native attributes argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XCreateWindow")]
    internal static partial nuint CreateWindow(nint display, nuint parent, int x, int y, uint width, uint height, uint border, int depth, uint windowClass, nint visual, nuint valueMask, ref WindowAttributes attributes);

    /// <summary>Invokes XDestroyWindow.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="window">The native window argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XDestroyWindow")]
    internal static partial int DestroyWindow(nint display, nuint window);

    /// <summary>Invokes XMapWindow.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="window">The native window argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XMapWindow")]
    internal static partial int MapWindow(nint display, nuint window);

    /// <summary>Invokes XStoreName.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="window">The native window argument.</param>
    /// <param name="name">The native name argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XStoreName", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int StoreName(nint display, nuint window, string name);

    /// <summary>Invokes XInternAtom.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="name">The native name argument.</param>
    /// <param name="onlyIfExists">The native onlyIfExists argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XInternAtom", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nuint InternAtom(nint display, string name, int onlyIfExists);

    /// <summary>Invokes XSetWMProtocols.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="window">The native window argument.</param>
    /// <param name="protocols">The native protocols argument.</param>
    /// <param name="count">The native count argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XSetWMProtocols")]
    internal static partial int SetProtocols(nint display, nuint window, ref nuint protocols, int count);

    /// <summary>Invokes XPending.</summary>
    /// <param name="display">The native display argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XPending")]
    internal static partial int Pending(nint display);

    /// <summary>Invokes XNextEvent.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="value">The native value argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XNextEvent")]
    internal static partial int NextEvent(nint display, out Event value);

    /// <summary>Invokes XFlush.</summary>
    /// <param name="display">The native display argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XFlush")]
    internal static partial int Flush(nint display);

    /// <summary>Installs a scoped startup error callback.</summary>
    /// <param name="handler">The native callback pointer.</param>
    /// <returns>The previous process-wide callback.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XSetErrorHandler")]
    internal static partial nint SetErrorHandler(nint handler);

    /// <summary>Waits for queued requests and dispatches protocol errors.</summary>
    /// <param name="display">The connection.</param>
    /// <param name="discard">Whether queued events are discarded.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libX11.so.6", EntryPoint = "XSync")]
    internal static partial int Sync(nint display, int discard);

    /// <summary>The LP64 Xlib VisualInfo layout used by Linux x64 and ARM64.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct VisualInfo
    {
        /// <summary>The native Visual field.</summary>
        [FieldOffset(0)]
        internal nint Visual;

        /// <summary>The native Id field.</summary>
        [FieldOffset(8)]
        internal nuint Id;

        /// <summary>The native Screen field.</summary>
        [FieldOffset(16)]
        internal int Screen;

        /// <summary>The native Depth field.</summary>
        [FieldOffset(20)]
        internal int Depth;

        /// <summary>The native Class field.</summary>
        [FieldOffset(24)]
        internal int Class;

        /// <summary>The native RedMask field.</summary>
        [FieldOffset(32)]
        internal nuint RedMask;

        /// <summary>The native GreenMask field.</summary>
        [FieldOffset(40)]
        internal nuint GreenMask;

        /// <summary>The native BlueMask field.</summary>
        [FieldOffset(48)]
        internal nuint BlueMask;

        /// <summary>The native ColormapSize field.</summary>
        [FieldOffset(56)]
        internal int ColormapSize;

        /// <summary>The native BitsPerRgb field.</summary>
        [FieldOffset(60)]
        internal int BitsPerRgb;
    }

    /// <summary>The LP64 Xlib WindowAttributes layout used by Linux x64 and ARM64.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 112)]
    internal struct WindowAttributes
    {
        /// <summary>The native EventMask field.</summary>
        [FieldOffset(72)]
        internal nint EventMask;

        /// <summary>The native Colormap field.</summary>
        [FieldOffset(96)]
        internal nuint Colormap;
    }

    /// <summary>The LP64 Xlib Event layout used by Linux x64 and ARM64.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 192)]
    internal struct Event
    {
        /// <summary>The native Type field.</summary>
        [FieldOffset(0)]
        internal int Type;

        /// <summary>The native Window field.</summary>
        [FieldOffset(32)]
        internal nuint Window;

        /// <summary>The native MessageType field.</summary>
        [FieldOffset(40)]
        internal nuint MessageType;

        /// <summary>The native Format field.</summary>
        [FieldOffset(48)]
        internal int Format;

        /// <summary>The native Data field.</summary>
        [FieldOffset(56)]
        internal nint Data;

        /// <summary>The native Width field.</summary>
        [FieldOffset(56)]
        internal int Width;

        /// <summary>The native Height field.</summary>
        [FieldOffset(60)]
        internal int Height;

        /// <summary>The native X field.</summary>
        [FieldOffset(64)]
        internal int X;

        /// <summary>The native Y field.</summary>
        [FieldOffset(68)]
        internal int Y;

        /// <summary>The native State field.</summary>
        [FieldOffset(80)]
        internal uint State;

        /// <summary>The native Button field.</summary>
        [FieldOffset(84)]
        internal uint Button;
    }
}
