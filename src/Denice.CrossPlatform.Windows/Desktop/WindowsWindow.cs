// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Desktop;
using SkiaSharp;

namespace Denice.CrossPlatform.Windows.Desktop;

/// <summary>Owns a Win32 desktop window and dispatches its messages on the calling thread.</summary>
[SuppressMessage("Security", "S6640", Justification = "The registered Win32 window procedure uses a stable UnmanagedCallersOnly entry point.")]
[SuppressMessage("Design", "CA2216", Justification = "Win32 windows must be destroyed on their owning UI thread.")]
internal sealed unsafe class WindowsWindow : IDisposable
{
    private const string ClassName = "DeniceGpuDesktop";
    private static readonly Dictionary<nint, WindowsWindow> Windows = [];
    private static bool _classRegistered;
    private readonly nint _handle;
    private IPlatformBrowser? _browser;
    private Exception? _callbackError;
    private bool _closed;
    private bool _closeRequested;
    private bool _disposed;
    private bool _trackingLeave;

    private WindowsWindow(nint handle)
    {
        _handle = handle;
    }

    /// <summary>Gets the native window handle used by GPU presentation.</summary>
    internal nint Handle => _handle;

    /// <summary>Gets a value indicating whether the native window remains open.</summary>
    internal bool IsOpen => !_closed && !_closeRequested && !_disposed;

    /// <summary>Gets a value indicating whether native window cleanup failed.</summary>
    internal bool CleanupFailed { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_closed && WindowNative.DestroyWindow(_handle) == 0)
        {
            CleanupFailed = true;
        }

        Windows.Remove(_handle);
        _disposed = true;
    }

    /// <summary>Creates and shows a native desktop window.</summary>
    /// <param name="width">Initial window width.</param>
    /// <param name="height">Initial window height.</param>
    /// <returns>The owner of the created window.</returns>
    internal static WindowsWindow Create(int width, int height)
    {
        EnsurePhysicalCoordinates();
        nint instance = WindowNative.Module(null);
        if (!_classRegistered)
        {
            fixed (char* name = ClassName)
            {
                var definition = new WindowNative.WindowClass
                {
                    Size = (uint)sizeof(WindowNative.WindowClass),
                    Instance = instance,
                    ClassName = (nint)name,
                    Procedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&Procedure,
                };

                // A registered class is process-owned and reused by subsequent windows.
                if (WindowNative.RegisterClass(in definition) == 0)
                {
                    throw new InvalidOperationException("Win32 could not register the desktop window class.");
                }

                _classRegistered = true;
            }
        }

        nint handle = WindowNative.CreateWindow(0, ClassName, "DENICE — Native GPU Prototype", 0x00CF0000, 0x100, 0x100, width, height, 0, 0, instance, 0);
        if (handle == 0)
        {
            throw new InvalidOperationException("Win32 could not create the native desktop window.");
        }

        var window = new WindowsWindow(handle);
        Windows.Add(handle, window);
        _ = WindowNative.ShowWindow(handle, 5);
        return window;
    }

    /// <summary>Attaches the browser receiving native pointer messages.</summary>
    /// <param name="browser">The browser owned by the desktop lifetime.</param>
    internal void AttachBrowser(IPlatformBrowser browser) => _browser = browser;

    /// <summary>Reads the current drawable client dimensions.</summary>
    /// <returns>The client width and height.</returns>
    internal SKSizeI GetClientSize()
    {
        if (WindowNative.GetClientRect(_handle, out WindowNative.Rectangle rectangle) == 0)
        {
            throw new InvalidOperationException("Win32 could not read the desktop client dimensions.");
        }

        return new SKSizeI(rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
    }

    /// <summary>Dispatches pending native messages without blocking the browser pump.</summary>
    internal void Pump()
    {
        while (WindowNative.PeekMessage(out WindowNative.Message message, 0, 0, 0, 1) != 0)
        {
            if (message.Id == 0x0012)
            {
                _closeRequested = true;
                break;
            }

            _ = WindowNative.TranslateMessage(in message);
            _ = WindowNative.DispatchMessage(in message);
        }

        if (_callbackError is { } failure)
        {
            _callbackError = null;
            throw new InvalidOperationException("Native desktop message processing failed.", failure);
        }
    }

    private static void EnsurePhysicalCoordinates()
    {
        // Windows 8.1+ provides this API. Per-monitor awareness keeps drawable and
        // mouse coordinates physical without requiring a newer Windows 10 API.
        if (WindowNative.GetProcessDpiAwareness(0, out int awareness) == 0 && awareness == 2)
        {
            return;
        }

        if (WindowNative.SetProcessDpiAwareness(2) != 0
            || WindowNative.GetProcessDpiAwareness(0, out awareness) != 0 || awareness != 2)
        {
            throw new InvalidOperationException("DENICE requires per-monitor DPI awareness before native windows are created.");
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Managed exceptions must be captured at the Win32 ABI and rethrown by the managed message pump.")]
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint Procedure(nint handle, uint message, nuint wParam, nint lParam)
    {
        if (Windows.TryGetValue(handle, out WindowsWindow? window))
        {
            try
            {
                if (message == 0x0082)
                {
                    window._closed = true;
                    Windows.Remove(handle);
                }
                else if (message == 0x0010)
                {
                    window._closeRequested = true;
                    return 0;
                }
                else if (message == 0x02E0)
                {
                    WindowNative.Rectangle rect = Marshal.PtrToStructure<WindowNative.Rectangle>(lParam);
                    if (WindowNative.SetWindowPos(handle, 0, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x0014) == 0)
                    {
                        throw new InvalidOperationException("Win32 could not apply the monitor's new DPI rectangle.");
                    }
                }
                else
                {
                    window.PointerMessage(message, wParam, lParam);
                }
            }
            catch (Exception error)
            {
                // Never unwind a managed exception across the Win32 callback ABI.
                window._callbackError = error;
            }
        }

        return WindowNative.DefaultProcedure(handle, message, wParam, lParam);
    }

    private void PointerMessage(uint message, nuint buttons, nint position)
    {
        if (_browser is null)
        {
            return;
        }

        if (message == 0x02A3)
        {
            _trackingLeave = false;
            _browser.MouseMove(-1, -1, true);
            return;
        }

        if (message is not (0x0200 or 0x0201 or 0x0202 or 0x0204 or 0x0205 or 0x0207 or 0x0208 or 0x020A or 0x020E))
        {
            return;
        }

        var point = new WindowNative.Point { X = (short)(position.ToInt64() & 0xFFFF), Y = (short)((position.ToInt64() >> 16) & 0xFFFF) };
        if (message is 0x020A or 0x020E && WindowNative.ScreenToClient(_handle, ref point) == 0)
        {
            throw new InvalidOperationException("Win32 could not translate mouse wheel coordinates.");
        }

        SKPointI logical = ToScene(point);
        if (message == 0x0200)
        {
            TrackPointerLeave();
            _browser.MouseMove(logical.X, logical.Y, false);
        }
        else if (message is 0x020A or 0x020E)
        {
            int delta = (short)((buttons >> 16) & 0xFFFF);
            _browser.MouseWheel(logical.X, logical.Y, message == 0x020E ? delta : 0, message == 0x020A ? delta : 0);
        }
        else
        {
            bool up = message is 0x0202 or 0x0205 or 0x0208;
            if (up && (buttons & 0x0013) == 0)
            {
                _ = WindowNative.ReleaseCapture();
            }
            else if (!up)
            {
                _ = WindowNative.SetCapture(_handle);
            }

            int button = message switch { 0x0201 or 0x0202 => 0, 0x0207 or 0x0208 => 1, _ => 2 };
            _browser.MouseClick(logical.X, logical.Y, button, up, 1);
        }
    }

    private SKPointI ToScene(WindowNative.Point point)
    {
        SKSizeI size = GetClientSize();
        int width = size.Width;
        int height = size.Height;
        double scale = Math.Min(width / (double)OrbScene.Width, height / (double)OrbScene.Height);
        if (scale <= 0)
        {
            return new SKPointI(-1, -1);
        }

        return new SKPointI((int)((point.X - ((width - (OrbScene.Width * scale)) / 2)) / scale), (int)((point.Y - ((height - (OrbScene.Height * scale)) / 2)) / scale));
    }

    private void TrackPointerLeave()
    {
        if (!_trackingLeave)
        {
            var tracking = new WindowNative.MouseTracking { Size = (uint)sizeof(WindowNative.MouseTracking), Flags = 2, Window = _handle };
            _trackingLeave = WindowNative.TrackMouseEvent(ref tracking) != 0;
        }
    }
}
