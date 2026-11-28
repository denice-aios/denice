// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Recording;

/// <summary>Binds the packaged GStreamer GL context API.</summary>
internal static partial class GstGlNative
{
    /// <summary>Creates a context owned by GStreamer.</summary>
    /// <param name="display">The borrowed display.</param>
    /// <returns>The owned context.</returns>
    [LibraryImport("libgstgl-1.0.so.0", EntryPoint = "gst_gl_context_new")]
    internal static partial nint NewContext(nint display);

    /// <summary>Starts the context thread with a shared GL object namespace.</summary>
    /// <param name="context">The new context.</param>
    /// <param name="otherContext">The compositor context wrapper.</param>
    /// <param name="error">The owned error on failure.</param>
    /// <returns>Whether creation succeeded.</returns>
    [LibraryImport("libgstgl-1.0.so.0", EntryPoint = "gst_gl_context_create")]
    internal static partial int CreateContext(nint context, nint otherContext, out nint error);

    /// <summary>Checks whether two contexts share GL objects.</summary>
    /// <param name="context">The consumer context.</param>
    /// <param name="otherContext">The compositor context wrapper.</param>
    /// <returns>Whether objects are shared.</returns>
    [LibraryImport("libgstgl-1.0.so.0", EntryPoint = "gst_gl_context_can_share")]
    internal static partial int CanShare(nint context, nint otherContext);

    /// <summary>Invokes gst_gl_display_egl_new_with_egl_display.</summary>
    /// <param name="display">The native display argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstgl-1.0.so.0", EntryPoint = "gst_gl_display_egl_new_with_egl_display")]
    internal static partial nint WrapDisplay(nint display);

    /// <summary>Invokes gst_gl_display_filter_gl_api.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="api">The native api argument.</param>
    [LibraryImport("libgstgl-1.0.so.0", EntryPoint = "gst_gl_display_filter_gl_api")]
    internal static partial void FilterApi(nint display, uint api);

    /// <summary>Invokes gst_gl_context_new_wrapped.</summary>
    /// <param name="display">The native display argument.</param>
    /// <param name="handle">The native handle argument.</param>
    /// <param name="platform">The native platform argument.</param>
    /// <param name="api">The native api argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstgl-1.0.so.0", EntryPoint = "gst_gl_context_new_wrapped")]
    internal static partial nint WrapContext(nint display, nuint handle, int platform, uint api);

    /// <summary>Invokes gst_gl_context_activate.</summary>
    /// <param name="context">The native context argument.</param>
    /// <param name="active">The native active argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstgl-1.0.so.0", EntryPoint = "gst_gl_context_activate")]
    internal static partial int Activate(nint context, int active);

    /// <summary>Invokes gst_gl_context_fill_info.</summary>
    /// <param name="context">The native context argument.</param>
    /// <param name="error">The native error argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstgl-1.0.so.0", EntryPoint = "gst_gl_context_fill_info")]
    internal static partial int FillInfo(nint context, out nint error);
}
