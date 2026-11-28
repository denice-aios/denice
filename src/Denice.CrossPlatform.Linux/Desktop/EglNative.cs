// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]

namespace Denice.CrossPlatform.Linux.Desktop;

/// <summary>Binds the system libEGL.so.1 native API.</summary>
internal static partial class EglNative
{
    /// <summary>Invokes eglGetDisplay.</summary>
    /// <param name="display">The native display argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglGetDisplay")]
    internal static partial nint GetDisplay(nint display);

    /// <summary>Invokes eglInitialize.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="major">The native major argument.</param>
    /// <param name="minor">The native minor argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglInitialize")]
    internal static partial int Initialize(nint display, out int major, out int minor);

    /// <summary>Invokes eglTerminate.</summary>
    /// <param name="display">The native display argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglTerminate")]
    internal static partial int Terminate(nint display);

    /// <summary>Invokes eglBindAPI.</summary>
    /// <param name="api">The native api argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglBindAPI")]
    internal static partial int BindApi(int api);

    /// <summary>Invokes eglChooseConfig.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="attributes">The native attributes argument.</param>
    /// <param name="config">The native config argument.</param>
    /// <param name="size">The native size argument.</param>
    /// <param name="count">The native count argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglChooseConfig")]
    internal static partial int ChooseConfig(nint display, [In] int[] attributes, out nint config, int size, out int count);

    /// <summary>Invokes eglGetConfigAttrib.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="config">The native config argument.</param>
    /// <param name="attribute">The native attribute argument.</param>
    /// <param name="value">The native value argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglGetConfigAttrib")]
    internal static partial int GetConfigAttribute(nint display, nint config, int attribute, out int value);

    /// <summary>Invokes eglCreateContext.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="config">The native config argument.</param>
    /// <param name="shared">The native shared argument.</param>
    /// <param name="attributes">The native attributes argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglCreateContext")]
    internal static partial nint CreateContext(nint display, nint config, nint shared, [In] int[] attributes);

    /// <summary>Invokes eglDestroyContext.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="context">The native context argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglDestroyContext")]
    internal static partial int DestroyContext(nint display, nint context);

    /// <summary>Invokes eglCreateWindowSurface.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="config">The native config argument.</param>
    /// <param name="window">The native window argument.</param>
    /// <param name="attributes">The native attributes argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglCreateWindowSurface")]
    internal static partial nint CreateWindowSurface(nint display, nint config, nuint window, [In] int[] attributes);

    /// <summary>Invokes eglDestroySurface.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="surface">The native surface argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglDestroySurface")]
    internal static partial int DestroySurface(nint display, nint surface);

    /// <summary>Invokes eglMakeCurrent.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="draw">The native draw argument.</param>
    /// <param name="read">The native read argument.</param>
    /// <param name="context">The native context argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglMakeCurrent")]
    internal static partial int MakeCurrent(nint display, nint draw, nint read, nint context);

    /// <summary>Invokes eglSwapBuffers.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="surface">The native surface argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglSwapBuffers")]
    internal static partial int SwapBuffers(nint display, nint surface);

    /// <summary>Invokes eglGetError.</summary>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglGetError")]
    internal static partial int GetError();

    /// <summary>Invokes eglQueryString.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="name">The native name argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglQueryString")]
    internal static partial nint QueryString(nint display, int name);

    /// <summary>Invokes eglGetProcAddress.</summary>
    /// <param name="name">The native name argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libEGL.so.1", EntryPoint = "eglGetProcAddress", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetProcAddress(string name);
}
