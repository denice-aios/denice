// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using SkiaSharp;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Denice.CrossPlatform.Windows.Desktop;

/// <summary>Owns one hardware D3D11 device shared with ANGLE and the Skia compositor.</summary>
[SuppressMessage("Design", "CA2216", Justification = "EGL resources must be released by the owning graphics thread, not the finalizer thread.")]
internal sealed class WindowsGpuContext : IDisposable
{
    private readonly ID3D11Device _device;
    private ID3D11DeviceContext? _commands;
    private nint _eglDevice;
    private nint _display;
    private nint _context;
    private nint _surface;
    private nint _config;
    private nint _presentationSurface;
    private ID3D11Texture2D? _presentation;
    private GRGlInterface? _gl;
    private GRContext? _skia;
    private bool _disposed;

    private WindowsGpuContext(ID3D11Device device)
    {
        _device = device;
    }

    /// <summary>Gets a value indicating whether native teardown reported an error.</summary>
    internal bool CleanupFailed { get; private set; }

    /// <summary>Gets the hardware device used for texture import and encoding.</summary>
    internal ID3D11Device Device => _device;

    /// <summary>Gets the Skia context backed by the same hardware D3D11 device.</summary>
    internal GRContext Skia => _skia ?? throw new ObjectDisposedException(nameof(WindowsGpuContext));

    /// <summary>Gets the owned GPU composition shared by presentation and recording.</summary>
    internal ID3D11Texture2D Presentation => _presentation ?? throw new InvalidOperationException("No composed presentation texture exists.");

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        bool released = true;
        _skia?.Dispose();
        _gl?.Dispose();
        _skia = null;
        _gl = null;
        if (_display != 0)
        {
            released &= AngleNative.MakeCurrent(_display, 0, 0, 0) != 0;
            if (_presentationSurface != 0)
            {
                released &= AngleNative.DestroySurface(_display, _presentationSurface) != 0;
            }

            if (_surface != 0)
            {
                released &= AngleNative.DestroySurface(_display, _surface) != 0;
            }

            if (_context != 0)
            {
                released &= AngleNative.DestroyContext(_display, _context) != 0;
            }

            released &= AngleNative.Terminate(_display) != 0;
        }

        if (_eglDevice != 0)
        {
            released &= AngleNative.ReleaseDevice(_eglDevice) != 0;
        }

        _presentation?.Dispose();
        _presentation = null;
        _commands?.Dispose();
        _commands = null;
        _device.Dispose();
        CleanupFailed |= !released;
        _disposed = true;
    }

    /// <summary>Creates the hardware graphics context without a software fallback.</summary>
    /// <returns>The thread-affine context owner.</returns>
    internal static WindowsGpuContext Create()
    {
        var result = new WindowsGpuContext(CreateHardwareDevice());
        try
        {
            result.Initialize();
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    /// <summary>Makes this context current on its owning graphics thread.</summary>
    internal void MakeCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Require(AngleNative.MakeCurrent(_display, _surface, _surface, _context), "make the graphics context current");
    }

    /// <summary>Replaces the bootstrap pbuffer with the native window's presentation surface.</summary>
    /// <param name="window">The borrowed HWND, which must outlive this context.</param>
    internal void AttachWindow(nint window)
    {
        nint replacement = AngleNative.CreateWindowSurface(_display, _config, window, [0x3038]);
        Require(replacement == 0 ? 0 : 1, "create the window presentation surface");
        if (AngleNative.MakeCurrent(_display, replacement, replacement, _context) == 0)
        {
            _ = AngleNative.DestroySurface(_display, replacement);
            throw new InvalidOperationException("ANGLE could not make the window surface current.");
        }

        nint previous = _surface;
        _surface = replacement;
        CleanupFailed |= AngleNative.DestroySurface(_display, previous) == 0;
        Skia.ResetContext();
    }

    /// <summary>Wraps the current EGL window framebuffer without a CPU image.</summary>
    /// <param name="width">The physical client width.</param>
    /// <param name="height">The physical client height.</param>
    /// <returns>The borrowed framebuffer surface wrapper.</returns>
    [SuppressMessage("Maintainability", "CA1508", Justification = "Skia native factories can return null on GPU allocation failure despite the binding annotation.")]
    internal SKSurface CreateWindowTarget(int width, int height)
    {
        MakeCurrent();
        using var target = new GRBackendRenderTarget(width, height, 0, 0, new GRGlFramebufferInfo(0, 0x8058));
        return SKSurface.Create(Skia, target, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
            ?? throw new InvalidOperationException("Skia could not create the ANGLE window framebuffer.");
    }

    /// <summary>Creates the current physical-size composition target on owned GPU storage.</summary>
    /// <param name="width">Physical client width.</param>
    /// <param name="height">Physical client height.</param>
    /// <returns>The caller-owned Skia wrapper for the composition surface.</returns>
    [SuppressMessage("Maintainability", "CA1508", Justification = "The native Skia factory may return null despite the binding annotation.")]
    internal SKSurface CreatePresentationTarget(int width, int height)
    {
        MakeCurrent();
        if (_presentation is null || _presentation.Description.Width != (uint)width || _presentation.Description.Height != (uint)height)
        {
            ReplacePresentation(width, height);
        }

        Require(AngleNative.MakeCurrent(_display, _presentationSurface, _presentationSurface, _context), "select the composition surface");
        Skia.ResetContext();
        using var target = new GRBackendRenderTarget(width, height, 0, 0, new GRGlFramebufferInfo(0, 0x8058));
        return SKSurface.Create(Skia, target, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
            ?? throw new InvalidOperationException("Skia could not wrap the composition framebuffer.");
    }

    /// <summary>Submits GPU drawing and presents the window surface.</summary>
    internal void Present()
    {
        Skia.Flush();
        Require(AngleNative.SwapBuffers(_display, _surface), "present the composed GPU frame");
    }

    /// <summary>Reads the adapter selected for this hardware D3D11 device.</summary>
    /// <returns>The adapter name and PCI vendor/device identifiers.</returns>
    internal string DescribeAdapter()
    {
        using var device = _device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = device.GetAdapter();
        AdapterDescription description = adapter.Description;
        return string.Create(CultureInfo.InvariantCulture, $"{description.Description}; vendor=0x{description.VendorId:X4}; device=0x{description.DeviceId:X4}");
    }

    /// <summary>Draws an owned D3D11 image through ANGLE without a CPU pixel copy.</summary>
    /// <param name="texture">The owned source texture, protected from concurrent updates.</param>
    /// <param name="canvas">The destination GPU canvas.</param>
    /// <param name="source">The visible source rectangle.</param>
    /// <param name="destination">The logical destination rectangle.</param>
    internal void DrawTexture(ID3D11Texture2D texture, SKCanvas canvas, SKRect source, SKRect destination)
    {
        // EGL_D3D11_TEXTURE_ANGLE (EGL_ANGLE_image_d3d11_texture). EGL_D3D_TEXTURE_ANGLE (0x33A3) is only a pbuffer
        // client-buffer type; eglCreateImageKHR rejects it with EGL_BAD_PARAMETER (run 45906718383).
        nint image = AngleNative.CreateImage(_display, 0, 0x3484, texture.NativePointer, [0x3038]);
        Require(image == 0 ? 0 : 1, "import the owned browser D3D11 texture");
        uint name = 0;
        try
        {
            AngleNative.GenerateTexture(1, out name);
            AngleNative.BindTexture(0x0DE1, name);
            AngleNative.TextureParameter(0x0DE1, 0x2801, 0x2601);
            AngleNative.TextureParameter(0x0DE1, 0x2800, 0x2601);
            AngleNative.BindImage(0x0DE1, image);
            if (name == 0 || AngleNative.GlError() != 0)
            {
                throw new InvalidOperationException("ANGLE could not bind the owned browser texture.");
            }

            Skia.ResetContext();
            Texture2DDescription description = texture.Description;
            using var backend = new GRBackendTexture((int)description.Width, (int)description.Height, false, new GRGlTextureInfo(0x0DE1, name, 0x8058));
            using SKImage pixels = SKImage.FromTexture(Skia, backend, GRSurfaceOrigin.TopLeft, SKColorType.Rgba8888, SKAlphaType.Premul);
            if (pixels is null || pixels.Handle == 0)
            {
                throw new InvalidOperationException("Skia could not wrap the owned browser texture.");
            }

            canvas.DrawImage(pixels, source, destination, SKSamplingOptions.Default);
            Skia.Flush(true, true);
        }
        finally
        {
            // Complete all GL reads before the next CEF callback may overwrite our copy.
            AngleNative.Finish();
            if (name != 0)
            {
                AngleNative.DeleteTexture(1, in name);
            }

            CleanupFailed |= AngleNative.DestroyImage(_display, image) == 0;
            Skia.ResetContext();
        }
    }

    /// <summary>Creates the device on the first DXGI adapter that is not a software rasterizer.</summary>
    /// <returns>The hardware device.</returns>
    /// <remarks>
    /// The default device selected Microsoft Basic Render Driver in the hosted Windows fixture run.
    /// Enumerating adapters explicitly prevents that software device from satisfying the hardware requirement.
    /// </remarks>
    private static ID3D11Device CreateHardwareDevice()
    {
        using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        var rejected = new List<string>();
        bool enumerated = true;
        uint index = 0;
        while (enumerated)
        {
            IDXGIAdapter1? adapter = null;
            try
            {
                enumerated = factory.EnumAdapters1(index++, out adapter).Success;
                if (!enumerated || adapter is null)
                {
                    continue;
                }

                AdapterDescription1 description = adapter.Description1;
                string name = string.Create(CultureInfo.InvariantCulture, $"{description.Description} (vendor=0x{description.VendorId:X4}; device=0x{description.DeviceId:X4})");
                bool basicRender = description.VendorId == 0x1414 && description.DeviceId == 0x008C;
                if ((description.Flags & AdapterFlags.Software) == AdapterFlags.Software || basicRender)
                {
                    rejected.Add(name + ": software adapter");
                    continue;
                }

                SharpGen.Runtime.Result created = D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport, [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out ID3D11Device? device);
                if (created.Success && device is not null)
                {
                    return device;
                }

                device?.Dispose();
                rejected.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: D3D11CreateDevice HRESULT 0x{created.Code:X8}"));
            }
            finally
            {
                adapter?.Dispose();
            }
        }

        throw new InvalidOperationException("No hardware D3D11 adapter with video support is available; software rendering is not substituted. Adapters: " + (rejected.Count == 0 ? "none" : string.Join("; ", rejected)) + ".");
    }

    private static void Require(int result, string operation)
    {
        if (result == 0)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"ANGLE could not {operation}; EGL error 0x{AngleNative.GetError():X4}."));
        }
    }

    private static void RequireGpuResource<T>(T? resource, string description)
        where T : SKObject
    {
        if (resource is null || resource.Handle == 0)
        {
            resource?.Dispose();
            throw new InvalidOperationException($"Skia could not create {description}.");
        }
    }

    private void ReplacePresentation(int width, int height)
    {
        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        };
        ID3D11Texture2D replacement = _device.CreateTexture2D(description);
        nint surface = AngleNative.CreateTextureSurface(_display, 0x33A3, replacement.NativePointer, _config, [0x3038]);
        if (surface == 0)
        {
            replacement.Dispose();
            throw new InvalidOperationException("ANGLE could not create the owned D3D11 composition surface.");
        }

        if (_presentationSurface != 0)
        {
            CleanupFailed |= AngleNative.DestroySurface(_display, _presentationSurface) == 0;
        }

        _presentation?.Dispose();
        _presentation = replacement;
        _presentationSurface = surface;
    }

    private void Initialize()
    {
        // Vortice caches this wrapper on the device. The context owner releases it
        // once; importers obtain separate QueryInterface references when needed.
        _commands = _device.ImmediateContext;
        using var multithread = _commands.QueryInterface<ID3D11Multithread>();
        _ = multithread.SetMultithreadProtected(true);

        // EGL_D3D11_DEVICE_ANGLE and EGL_PLATFORM_DEVICE_EXT bind ANGLE to our device,
        // rather than allowing a second device or software renderer to be selected.
        _eglDevice = AngleNative.CreateDevice(0x33A1, _device.NativePointer, 0);
        Require(_eglDevice == 0 ? 0 : 1, "import the D3D11 device");
        _display = AngleNative.GetPlatformDisplay(0x313F, _eglDevice, 0);
        Require(_display == 0 ? 0 : 1, "create the device display");
        Require(AngleNative.Initialize(_display, out _, out _), "initialize the device display");
        Require(AngleNative.BindApi(0x30A0), "select OpenGL ES");

        // The compositor uses RGBA textures on the GPU.
        // A one-pixel pbuffer makes the context current before a window exists.
        int[] configAttributes = [0x3024, 8, 0x3023, 8, 0x3022, 8, 0x3021, 8, 0x3033, 5, 0x3040, 0x40, 0x3038];
        Require(AngleNative.ChooseConfig(_display, configAttributes, out _config, 1, out int count), "choose an RGBA8 GLES3 configuration");
        Require(count, "find an RGBA8 GLES3 configuration");
        int[] contextAttributes = [0x3098, 3, 0x3038];
        _context = AngleNative.CreateContext(_display, _config, 0, contextAttributes);
        Require(_context == 0 ? 0 : 1, "create the GLES3 context");
        int[] surfaceAttributes = [0x3057, 1, 0x3056, 1, 0x3038];
        _surface = AngleNative.CreatePbuffer(_display, _config, surfaceAttributes);
        Require(_surface == 0 ? 0 : 1, "create the context surface");
        MakeCurrent();
        _gl = GRGlInterface.CreateGles(AngleNative.GetProcAddress);
        RequireGpuResource(_gl, "the GLES interface");
        _skia = GRContext.CreateGl(_gl);
        RequireGpuResource(_skia, "the GPU context");
        if (!_gl.Validate())
        {
            throw new InvalidOperationException("ANGLE did not provide a usable Skia GPU context.");
        }
    }
}
