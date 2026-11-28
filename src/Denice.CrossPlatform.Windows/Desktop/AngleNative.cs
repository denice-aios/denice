// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]

namespace Denice.CrossPlatform.Windows.Desktop;

/// <summary>Binds the stock combined ANGLE library shipped for Windows x64 and ARM64.</summary>
internal static partial class AngleNative
{
    /// <summary>Invokes the stock EGL_CreateDeviceANGLE entry point.</summary>
    /// <param name="type">The EGL type argument.</param>
    /// <param name="device">The EGL device argument.</param>
    /// <param name="attributes">The EGL attributes argument.</param>
    /// <returns>The native EGL result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_CreateDeviceANGLE")]
    internal static partial nint CreateDevice(int type, nint device, nint attributes);

    /// <summary>Invokes the stock EGL_ReleaseDeviceANGLE entry point.</summary>
    /// <param name="device">The EGL device argument.</param>
    /// <returns>The native EGL result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_ReleaseDeviceANGLE")]
    internal static partial int ReleaseDevice(nint device);

    /// <summary>Invokes the stock EGL_GetPlatformDisplayEXT entry point.</summary>
    /// <param name="platform">The EGL platform argument.</param>
    /// <param name="device">The EGL device argument.</param>
    /// <param name="attributes">The EGL attributes argument.</param>
    /// <returns>The native EGL result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_GetPlatformDisplayEXT")]
    internal static partial nint GetPlatformDisplay(int platform, nint device, nint attributes);

    /// <summary>Invokes the stock EGL_Initialize entry point.</summary>
    /// <param name="display">The EGL display argument.</param>
    /// <param name="major">The EGL major argument.</param>
    /// <param name="minor">The EGL minor argument.</param>
    /// <returns>The native EGL result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_Initialize")]
    internal static partial int Initialize(nint display, out int major, out int minor);

    /// <summary>Invokes the stock EGL_Terminate entry point.</summary>
    /// <param name="display">The EGL display argument.</param>
    /// <returns>The native EGL result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_Terminate")]
    internal static partial int Terminate(nint display);

    /// <summary>Invokes the stock EGL_BindAPI entry point.</summary>
    /// <param name="api">The EGL api argument.</param>
    /// <returns>The native EGL result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_BindAPI")]
    internal static partial int BindApi(int api);

    /// <summary>Invokes the stock EGL_ChooseConfig entry point.</summary>
    /// <param name="display">The EGL display argument.</param>
    /// <param name="attributes">The EGL attributes argument.</param>
    /// <param name="config">The EGL config argument.</param>
    /// <param name="size">The EGL size argument.</param>
    /// <param name="count">The EGL count argument.</param>
    /// <returns>The native EGL result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_ChooseConfig")]
    internal static partial int ChooseConfig(nint display, [In] int[] attributes, out nint config, int size, out int count);

    /// <summary>Invokes the stock EGL_CreateContext entry point.</summary>
    /// <param name="display">The EGL display argument.</param>
    /// <param name="config">The EGL config argument.</param>
    /// <param name="shared">The EGL shared argument.</param>
    /// <param name="attributes">The EGL attributes argument.</param>
    /// <returns>The native EGL result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_CreateContext")]
    internal static partial nint CreateContext(nint display, nint config, nint shared, [In] int[] attributes);

    /// <summary>Invokes the stock EGL_DestroyContext entry point.</summary>
    /// <param name="display">The EGL display argument.</param>
    /// <param name="context">The EGL context argument.</param>
    /// <returns>The native EGL result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_DestroyContext")]
    internal static partial int DestroyContext(nint display, nint context);

    /// <summary>Invokes the stock EGL_CreatePbufferSurface entry point.</summary>
    /// <param name="display">The EGL display argument.</param>
    /// <param name="config">The EGL config argument.</param>
    /// <param name="attributes">The EGL attributes argument.</param>
    /// <returns>The native EGL result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_CreatePbufferSurface")]
    internal static partial nint CreatePbuffer(nint display, nint config, [In] int[] attributes);

    /// <summary>Invokes the stock EGL_DestroySurface entry point.</summary>
    /// <param name="display">The EGL display argument.</param>
    /// <param name="surface">The EGL surface argument.</param>
    /// <returns>The native EGL result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_DestroySurface")]
    internal static partial int DestroySurface(nint display, nint surface);

    /// <summary>Invokes the stock EGL_MakeCurrent entry point.</summary>
    /// <param name="display">The EGL display argument.</param>
    /// <param name="draw">The EGL draw argument.</param>
    /// <param name="read">The EGL read argument.</param>
    /// <param name="context">The EGL context argument.</param>
    /// <returns>The native EGL result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_MakeCurrent")]
    internal static partial int MakeCurrent(nint display, nint draw, nint read, nint context);

    /// <summary>Invokes the stock EGL_GetError entry point.</summary>
    /// <returns>The native EGL result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_GetError")]
    internal static partial int GetError();

    /// <summary>Invokes the stock EGL_GetProcAddress entry point.</summary>
    /// <param name="name">The EGL name argument.</param>
    /// <returns>The native EGL result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_GetProcAddress", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetProcAddress(string name);

    /// <summary>Creates an EGL presentation surface for a Win32 window.</summary>
    /// <param name="display">The EGL display.</param>
    /// <param name="config">The selected surface configuration.</param>
    /// <param name="window">The native HWND.</param>
    /// <param name="attributes">The EGL attribute list.</param>
    /// <returns>The presentation surface.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_CreateWindowSurface")]
    internal static partial nint CreateWindowSurface(nint display, nint config, nint window, [In] int[] attributes);

    /// <summary>Presents the completed EGL window surface.</summary>
    /// <param name="display">The EGL display.</param>
    /// <param name="surface">The window surface.</param>
    /// <returns>The EGL success result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_SwapBuffers")]
    internal static partial int SwapBuffers(nint display, nint surface);

    /// <summary>Invokes the stock EGL_CreateImageKHR GPU entry point.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="context">The native context argument.</param>
    /// <param name="target">The native target argument.</param>
    /// <param name="buffer">The native buffer argument.</param>
    /// <param name="attributes">The native attributes argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_CreateImageKHR")]
    internal static partial nint CreateImage(nint display, nint context, uint target, nint buffer, [In] int[] attributes);

    /// <summary>Invokes the stock EGL_DestroyImageKHR GPU entry point.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="image">The native image argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_DestroyImageKHR")]
    internal static partial int DestroyImage(nint display, nint image);

    /// <summary>Invokes the stock glGenTextures GPU entry point.</summary>
    /// <param name="count">The native count argument.</param>
    /// <param name="texture">The native texture argument.</param>
    [LibraryImport("av_libglesv2", EntryPoint = "glGenTextures")]
    internal static partial void GenerateTexture(int count, out uint texture);

    /// <summary>Invokes the stock glDeleteTextures GPU entry point.</summary>
    /// <param name="count">The native count argument.</param>
    /// <param name="texture">The native texture argument.</param>
    [LibraryImport("av_libglesv2", EntryPoint = "glDeleteTextures")]
    internal static partial void DeleteTexture(int count, in uint texture);

    /// <summary>Invokes the stock glBindTexture GPU entry point.</summary>
    /// <param name="target">The native target argument.</param>
    /// <param name="texture">The native texture argument.</param>
    [LibraryImport("av_libglesv2", EntryPoint = "glBindTexture")]
    internal static partial void BindTexture(uint target, uint texture);

    /// <summary>Invokes the stock glTexParameteri GPU entry point.</summary>
    /// <param name="target">The native target argument.</param>
    /// <param name="name">The native name argument.</param>
    /// <param name="value">The native value argument.</param>
    [LibraryImport("av_libglesv2", EntryPoint = "glTexParameteri")]
    internal static partial void TextureParameter(uint target, uint name, int value);

    /// <summary>Invokes the stock glEGLImageTargetTexture2DOES GPU entry point.</summary>
    /// <param name="target">The native target argument.</param>
    /// <param name="image">The native image argument.</param>
    [LibraryImport("av_libglesv2", EntryPoint = "glEGLImageTargetTexture2DOES")]
    internal static partial void BindImage(uint target, nint image);

    /// <summary>Invokes the stock glFinish GPU entry point.</summary>
    [LibraryImport("av_libglesv2", EntryPoint = "glFinish")]
    internal static partial void Finish();

    /// <summary>Invokes the stock glGetError GPU entry point.</summary>
    /// <returns>The native result.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "glGetError")]
    internal static partial uint GlError();

    /// <summary>Creates an EGL surface backed by an owned D3D11 texture.</summary>
    /// <param name="display">The EGL display.</param>
    /// <param name="type">The client buffer type.</param>
    /// <param name="buffer">The D3D11 texture pointer.</param>
    /// <param name="config">The chosen EGL configuration.</param>
    /// <param name="attributes">The terminated attributes.</param>
    /// <returns>The EGL surface.</returns>
    [LibraryImport("av_libglesv2", EntryPoint = "EGL_CreatePbufferFromClientBuffer")]
    internal static partial nint CreateTextureSurface(nint display, uint type, nint buffer, nint config, [In] int[] attributes);
}
