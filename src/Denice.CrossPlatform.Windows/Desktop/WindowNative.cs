// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Windows.Desktop;

/// <summary>Win32 window and message-loop bindings for the native desktop.</summary>
internal static partial class WindowNative
{
    /// <summary>Selects per-monitor DPI awareness before creating a window.</summary>
    /// <param name="awareness">The process DPI awareness value.</param>
    /// <returns>The HRESULT result.</returns>
    [LibraryImport("shcore", EntryPoint = "SetProcessDpiAwareness")]
    internal static partial int SetProcessDpiAwareness(int awareness);

    /// <summary>Reads the process DPI awareness.</summary>
    /// <param name="process">Zero for the current process.</param>
    /// <param name="awareness">The current awareness value.</param>
    /// <returns>The HRESULT result.</returns>
    [LibraryImport("shcore", EntryPoint = "GetProcessDpiAwareness")]
    internal static partial int GetProcessDpiAwareness(nint process, out int awareness);

    /// <summary>Invokes GetModuleHandleW.</summary>
    /// <param name="name">The native name argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("kernel32", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint Module(string? name);

    /// <summary>Invokes RegisterClassExW.</summary>
    /// <param name="value">The native value argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "RegisterClassExW")]
    internal static partial ushort RegisterClass(in WindowClass value);

    /// <summary>Invokes CreateWindowExW.</summary>
    /// <param name="extendedStyle">The native extendedStyle argument.</param>
    /// <param name="className">The native className argument.</param>
    /// <param name="title">The native title argument.</param>
    /// <param name="style">The native style argument.</param>
    /// <param name="x">The native x argument.</param>
    /// <param name="y">The native y argument.</param>
    /// <param name="width">The native width argument.</param>
    /// <param name="height">The native height argument.</param>
    /// <param name="parent">The native parent argument.</param>
    /// <param name="menu">The native menu argument.</param>
    /// <param name="instance">The native instance argument.</param>
    /// <param name="parameter">The native parameter argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindow(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    /// <summary>Invokes DefWindowProcW.</summary>
    /// <param name="window">The native window argument.</param>
    /// <param name="message">The native message argument.</param>
    /// <param name="wParam">The native wParam argument.</param>
    /// <param name="lParam">The native lParam argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefaultProcedure(nint window, uint message, nuint wParam, nint lParam);

    /// <summary>Invokes DestroyWindow.</summary>
    /// <param name="window">The native window argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "DestroyWindow")]
    internal static partial int DestroyWindow(nint window);

    /// <summary>Invokes ShowWindow.</summary>
    /// <param name="window">The native window argument.</param>
    /// <param name="command">The native command argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "ShowWindow")]
    internal static partial int ShowWindow(nint window, int command);

    /// <summary>Invokes PeekMessageW.</summary>
    /// <param name="message">The native message argument.</param>
    /// <param name="window">The native window argument.</param>
    /// <param name="minimum">The native minimum argument.</param>
    /// <param name="maximum">The native maximum argument.</param>
    /// <param name="remove">The native remove argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "PeekMessageW")]
    internal static partial int PeekMessage(out Message message, nint window, uint minimum, uint maximum, uint remove);

    /// <summary>Invokes TranslateMessage.</summary>
    /// <param name="message">The native message argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "TranslateMessage")]
    internal static partial int TranslateMessage(in Message message);

    /// <summary>Invokes DispatchMessageW.</summary>
    /// <param name="message">The native message argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(in Message message);

    /// <summary>Invokes GetClientRect.</summary>
    /// <param name="window">The native window argument.</param>
    /// <param name="rectangle">The native rectangle argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "GetClientRect")]
    internal static partial int GetClientRect(nint window, out Rectangle rectangle);

    /// <summary>Invokes IsWindow.</summary>
    /// <param name="window">The native window argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "IsWindow")]
    internal static partial int IsWindow(nint window);

    /// <summary>Invokes SetCapture.</summary>
    /// <param name="window">The native window argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "SetCapture")]
    internal static partial nint SetCapture(nint window);

    /// <summary>Invokes ReleaseCapture.</summary>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "ReleaseCapture")]
    internal static partial int ReleaseCapture();

    /// <summary>Invokes TrackMouseEvent.</summary>
    /// <param name="tracking">The native tracking argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "TrackMouseEvent")]
    internal static partial int TrackMouseEvent(ref MouseTracking tracking);

    /// <summary>Invokes ScreenToClient.</summary>
    /// <param name="window">The native window argument.</param>
    /// <param name="point">The native point argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "ScreenToClient")]
    internal static partial int ScreenToClient(nint window, ref Point point);

    /// <summary>Invokes SetWindowPos.</summary>
    /// <param name="window">The native window argument.</param>
    /// <param name="insertAfter">The native insertAfter argument.</param>
    /// <param name="x">The native x argument.</param>
    /// <param name="y">The native y argument.</param>
    /// <param name="width">The native width argument.</param>
    /// <param name="height">The native height argument.</param>
    /// <param name="flags">The native flags argument.</param>
    /// <returns>The Win32 operation result.</returns>
    [LibraryImport("user32", EntryPoint = "SetWindowPos")]
    internal static partial int SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    /// <summary>The sequential Win32 WindowClass layout.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowClass
    {
        /// <summary>The native Size field.</summary>
        internal uint Size;

        /// <summary>The native Style field.</summary>
        internal uint Style;

        /// <summary>The native Procedure field.</summary>
        internal nint Procedure;

        /// <summary>The native ClassExtra field.</summary>
        internal int ClassExtra;

        /// <summary>The native WindowExtra field.</summary>
        internal int WindowExtra;

        /// <summary>The native Instance field.</summary>
        internal nint Instance;

        /// <summary>The native Icon field.</summary>
        internal nint Icon;

        /// <summary>The native Cursor field.</summary>
        internal nint Cursor;

        /// <summary>The native Background field.</summary>
        internal nint Background;

        /// <summary>The native MenuName field.</summary>
        internal nint MenuName;

        /// <summary>The native ClassName field.</summary>
        internal nint ClassName;

        /// <summary>The native SmallIcon field.</summary>
        internal nint SmallIcon;
    }

    /// <summary>The sequential Win32 Message layout.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        /// <summary>The native Window field.</summary>
        internal nint Window;

        /// <summary>The native Id field.</summary>
        internal uint Id;

        /// <summary>The native WParam field.</summary>
        internal nuint WParam;

        /// <summary>The native LParam field.</summary>
        internal nint LParam;

        /// <summary>The native Time field.</summary>
        internal uint Time;

        /// <summary>The native X field.</summary>
        internal int X;

        /// <summary>The native Y field.</summary>
        internal int Y;

        /// <summary>The native Private field.</summary>
        internal uint Private;
    }

    /// <summary>The sequential Win32 Point layout.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        /// <summary>The native X field.</summary>
        internal int X;

        /// <summary>The native Y field.</summary>
        internal int Y;
    }

    /// <summary>The sequential Win32 MouseTracking layout.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseTracking
    {
        /// <summary>The native Size field.</summary>
        internal uint Size;

        /// <summary>The native Flags field.</summary>
        internal uint Flags;

        /// <summary>The native Window field.</summary>
        internal nint Window;

        /// <summary>The native HoverTime field.</summary>
        internal uint HoverTime;
    }

    /// <summary>The sequential Win32 Rectangle layout.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rectangle
    {
        /// <summary>The native Left field.</summary>
        internal int Left;

        /// <summary>The native Top field.</summary>
        internal int Top;

        /// <summary>The native Right field.</summary>
        internal int Right;

        /// <summary>The native Bottom field.</summary>
        internal int Bottom;
    }
}
