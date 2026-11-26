// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Denice.CrossPlatform.Linux.Desktop;

/// <summary>Owns an X11/EGL GLES3 context and the Skia renderer on its creating thread.</summary>
[SuppressMessage("Design", "CA2216", Justification = "The graphics thread releases EGL and X11 resources in dependency order.")]
internal sealed class LinuxGpuContext : IDisposable
{
    private nint _connection;
    private nint _context;
    private nint _surface;
    private GRGlInterface? _gl;
    private GRContext? _skia;
    private LinuxWindow? _window;
    private bool _disposed;

    /// <summary>Gets the EGL display borrowed by image importers.</summary>
    internal nint Display { get; private set; }

    /// <summary>Gets the EGL context borrowed by the recorder on the graphics thread.</summary>
    internal nint NativeContext => _disposed ? throw new ObjectDisposedException(nameof(LinuxGpuContext)) : _context;

    /// <summary>Gets the owned native window.</summary>
    internal LinuxWindow Window => _window ?? throw new ObjectDisposedException(nameof(LinuxGpuContext));

    /// <summary>Gets the context used by the shared scene and texture importer.</summary>
    internal GRContext Skia => _skia ?? throw new ObjectDisposedException(nameof(LinuxGpuContext));

    /// <summary>Gets the actual GL renderer string.</summary>
    internal string Renderer { get; private set; } = string.Empty;

    /// <summary>Gets the EGL display extension list.</summary>
    internal string Extensions { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether native cleanup reported failure.</summary>
    internal bool CleanupFailed { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _skia?.Dispose();
        _gl?.Dispose();
        _skia = null;
        _gl = null;
        if (Display != 0)
        {
            CleanupFailed |= EglNative.MakeCurrent(Display, 0, 0, 0) == 0;
            if (_surface != 0)
            {
                CleanupFailed |= EglNative.DestroySurface(Display, _surface) == 0;
            }

            if (_context != 0)
            {
                CleanupFailed |= EglNative.DestroyContext(Display, _context) == 0;
            }

            CleanupFailed |= EglNative.Terminate(Display) == 0;
        }

        _window?.Dispose();
        _window = null;
        if (_connection != 0)
        {
            _ = X11Native.CloseDisplay(_connection);
        }

        _disposed = true;
    }

    /// <summary>Creates a GLES3 window using the current graphical session.</summary>
    /// <param name="width">The initial client width.</param>
    /// <param name="height">The initial client height.</param>
    /// <returns>The graphics owner.</returns>
    internal static LinuxGpuContext Create(int width, int height)
    {
        var result = new LinuxGpuContext { _connection = X11Native.OpenDisplay(0) };
        if (result._connection == 0)
        {
            throw new InvalidOperationException("DENICE requires a usable X11 graphical session.");
        }

        // The desktop must construct graphics before calling browser.Start,
        // which initializes CEF and its own Xlib clients.
        using var errors = X11StartupErrors.Begin(result._connection);
        try
        {
            result.Initialize(width, height);
            errors.Complete();
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    /// <summary>Makes the owned context current before native or Skia work.</summary>
    internal void MakeCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Require(EglNative.MakeCurrent(Display, _surface, _surface, _context), "make the GLES context current");
    }

    /// <summary>Wraps the EGL window framebuffer.</summary>
    /// <returns>A temporary Skia surface borrowing the window framebuffer.</returns>
    internal SKSurface CreateWindowTarget()
    {
        MakeCurrent();
        SKSizeI size = Window.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new InvalidOperationException("The X11 drawable has no visible area.");
        }

        using var target = new GRBackendRenderTarget(size.Width, size.Height, 0, 0, new GRGlFramebufferInfo(0, 0x8058));
        SKSurface surface = SKSurface.Create(Skia, target, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
        if (surface.Handle == 0)
        {
            surface.Dispose();
            throw new InvalidOperationException("Skia could not wrap the EGL window framebuffer.");
        }

        return surface;
    }

    /// <summary>Submits Skia work and presents the composed frame.</summary>
    internal void Present()
    {
        Skia.Flush();
        Require(EglNative.SwapBuffers(Display, _surface), "present the EGL window surface");
    }

    /// <summary>Requires an EGL extension before invoking its entry points.</summary>
    /// <param name="name">The exact extension name.</param>
    internal void RequireExtension(string name)
    {
        if (!Extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal))
        {
            throw new NotSupportedException("The selected EGL display does not support " + name + ".");
        }
    }

    /// <summary>Requires an actual GLES extension rather than only an exported function pointer.</summary>
    /// <param name="name">The exact GLES extension name.</param>
    internal void RequireGlExtension(string name)
    {
        MakeCurrent();
        GlesNative.GetInteger(0x821D, out int count);
        for (uint index = 0; index < count; index++)
        {
            if (string.Equals(Marshal.PtrToStringUTF8(GlesNative.GetStringAt(0x1F03, index)), name, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new NotSupportedException("The selected GLES context does not support " + name + ".");
    }

    private static void Require(int success, string operation)
    {
        if (success == 0)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"EGL could not {operation}; error 0x{EglNative.GetError():X4}."));
        }
    }

    private void Initialize(int width, int height)
    {
        Display = EglNative.GetDisplay(_connection);
        Require(Display == 0 ? 0 : 1, "open the X11 EGL display");
        Require(EglNative.Initialize(Display, out _, out _), "initialize the X11 EGL display");
        Extensions = Marshal.PtrToStringUTF8(EglNative.QueryString(Display, 0x3055)) ?? string.Empty;
        Require(EglNative.BindApi(0x30A0), "select GLES");
        int[] attributes = [0x3024, 8, 0x3023, 8, 0x3022, 8, 0x3021, 8, 0x3033, 4, 0x3040, 0x40, 0x3038];
        Require(EglNative.ChooseConfig(Display, attributes, out nint config, 1, out int count), "choose an RGBA8 GLES3 configuration");
        Require(count, "find an RGBA8 GLES3 configuration");
        Require(EglNative.GetConfigAttribute(Display, config, 0x302E, out int visual), "read the X11 visual");
        _window = LinuxWindow.Create(_connection, visual, width, height);
        _context = EglNative.CreateContext(Display, config, 0, [0x3098, 3, 0x3038]);
        Require(_context == 0 ? 0 : 1, "create the GLES3 context");
        _surface = EglNative.CreateWindowSurface(Display, config, Window.Handle, [0x3038]);
        Require(_surface == 0 ? 0 : 1, "create the presentation surface");
        MakeCurrent();
        Renderer = Marshal.PtrToStringUTF8(GlesNative.GetString(0x1F01)) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Renderer)
            || Renderer.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase)
            || Renderer.Contains("softpipe", StringComparison.OrdinalIgnoreCase)
            || Renderer.Contains("Software Rasterizer", StringComparison.OrdinalIgnoreCase)
            || Renderer.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("The EGL display supplied no identifiable hardware renderer: " + Renderer);
        }

        _gl = GRGlInterface.CreateGles(EglNative.GetProcAddress);
        if (_gl.Handle == 0 || !_gl.Validate())
        {
            throw new InvalidOperationException("The GLES interface is incomplete for Skia.");
        }

        _skia = GRContext.CreateGl(_gl);
        if (_skia.Handle == 0)
        {
            throw new InvalidOperationException("Skia could not create the Linux GPU context.");
        }
    }
}
