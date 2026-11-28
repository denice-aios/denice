// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Denice.Analyzers;

/// <summary>Separates desktop media interop from filesystem calls without exempting arbitrary native libraries.</summary>
internal static class DesktopNativeInterop
{
    /// <summary>Determines whether a native use belongs to a per-OS desktop capability.</summary>
    /// <param name="context">The operation context.</param>
    /// <param name="member">The invoked native member.</param>
    /// <param name="type">The native member's declaring type.</param>
    /// <returns>Whether the bounded desktop media exemption applies.</returns>
    internal static bool IsAllowed(OperationAnalysisContext context, ISymbol member, INamedTypeSymbol type)
    {
        string assembly = context.Compilation.AssemblyName ?? string.Empty;
        if (!CrossPlatformBoundary.IsAnyPerOsImplementationLibrary(context.Compilation))
        {
            return false;
        }

        string area = context.ContainingSymbol.ContainingNamespace.ToDisplayString();
        if (!string.Equals(area, assembly + ".Desktop", StringComparison.Ordinal)
            && !string.Equals(area, assembly + ".Browser", StringComparison.Ordinal)
            && !string.Equals(area, assembly + ".Recording", StringComparison.Ordinal))
        {
            return false;
        }

        if (NativeInteropUse.IsMarshalUse(type))
        {
            return member.Name is "ReadIntPtr" or "ReadInt32" or "PtrToStringUTF8" or "Copy" or "SizeOf"
                or "AllocHGlobal" or "FreeHGlobal"
                or "PtrToStructure" or "StructureToPtr" or "GetFunctionPointerForDelegate";
        }

        if (member is not IMethodSymbol method)
        {
            return false;
        }

        AttributeData? import = method.GetAttributes().FirstOrDefault(attribute => WellKnownType.IsAnyOf(attribute.AttributeClass, PInvoke.InteropAttributes));
        if (import is null || import.ConstructorArguments.Length == 0 || import.ConstructorArguments[0].Value is not string library)
        {
            return false;
        }

        string entryPoint = EntryPointOf(method, import);
        if (IsBrowserAudioImport(area, assembly, library, entryPoint) || IsAudioRedirectionProofImport(context, area, assembly, library, entryPoint))
        {
            return true;
        }

        if (string.Equals(assembly, "Denice.CrossPlatform.Windows", StringComparison.Ordinal))
        {
            return IsWindowsEntryPoint(library, entryPoint);
        }

        if (string.Equals(assembly, "Denice.CrossPlatform.Linux", StringComparison.Ordinal))
        {
            return IsLinuxEntryPoint(library, entryPoint);
        }

        return string.Equals(assembly, "Denice.CrossPlatform.MacOS", StringComparison.Ordinal)
            && ((library is "/usr/lib/libSystem.B.dylib" && entryPoint is "memmove")
                || library is "/usr/lib/libobjc.A.dylib"
                || IsAppleFramework(library));
    }

    private static string EntryPointOf(IMethodSymbol method, AttributeData import)
    {
        foreach (var argument in import.NamedArguments)
        {
            if (argument.Key is "EntryPoint" && argument.Value.Value is string name)
            {
                return name;
            }
        }

        return method.Name;
    }

    // Browser-audio allowance: approved for the proof by document-0b78de34…; widened to the normal build by the browser audio
    // integration Tim requested, whose canonical amendment document-e8a1790a… (revision 48d44038…) is not yet delivered or
    // approved. Only the per-OS Browser area may import AudioToolbox, the dyld image walk and vm_protect, to redirect the
    // browser engine's audio output into the application's own mixer.
    private static bool IsBrowserAudioImport(string area, string assembly, string library, string entryPoint)
    {
        if (!string.Equals(area, assembly + ".Browser", StringComparison.Ordinal))
        {
            return false;
        }

        return library is "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox"
            || (library is "/usr/lib/libSystem.B.dylib"
                && entryPoint is "_dyld_image_count" or "_dyld_get_image_name" or "_dyld_get_image_header" or "_dyld_get_image_vmaddr_slide" or "vm_protect");
    }

    // Browser-audio redirection proof allowance: approved with windows-linux-audio-proof.md ("the proof-scoped analyzer
    // allowances", remaining-decisions.md, Approved 2026-11-27). It applies only where the compile-selected proof symbol
    // DENICE_AUDIO_REDIRECTION_PROOF is defined, so normal builds keep the restriction, and only in the per-OS Browser area.
    private static bool IsAudioRedirectionProofImport(OperationAnalysisContext context, string area, string assembly, string library, string entryPoint)
    {
        if (!string.Equals(area, assembly + ".Browser", StringComparison.Ordinal)
            || !context.Operation.Syntax.SyntaxTree.Options.PreprocessorSymbolNames.Contains("DENICE_AUDIO_REDIRECTION_PROOF", StringComparer.Ordinal))
        {
            return false;
        }

        if (string.Equals(assembly, "Denice.CrossPlatform.Linux", StringComparison.Ordinal))
        {
            return (library is "libc.so.6" && entryPoint is "dl_iterate_phdr" or "mprotect" or "getpagesize" or "setenv")
                || (library is "libpulse.so.0" && entryPoint.StartsWith("pa_", StringComparison.Ordinal));
        }

        return string.Equals(assembly, "Denice.CrossPlatform.Windows", StringComparison.Ordinal)
            && ((library is "kernel32" && entryPoint is "VirtualProtect" or "GetProcAddress")
                || (library is "ole32" && entryPoint is "CoCreateInstance" or "CoTaskMemAlloc"));
    }

    private static bool IsWindowsEntryPoint(string library, string entryPoint) =>
        (library is "av_libglesv2" && IsAngleEntryPoint(entryPoint))
        || (library is "ole32" && entryPoint is "CoInitializeEx" or "CoUninitialize")
        || (library is "shcore" && entryPoint is "SetProcessDpiAwareness" or "GetProcessDpiAwareness")
        || (library is "kernel32" && entryPoint is "GetModuleHandleW")
        || (library is "user32" && entryPoint is "RegisterClassExW" or "CreateWindowExW" or "DefWindowProcW"
            or "DestroyWindow" or "ShowWindow" or "PeekMessageW" or "TranslateMessage" or "DispatchMessageW"
            or "GetClientRect" or "IsWindow" or "SetCapture" or "ReleaseCapture" or "TrackMouseEvent"
            or "ScreenToClient" or "SetWindowPos");

    private static bool IsAngleEntryPoint(string entryPoint) => entryPoint is "EGL_CreateDeviceANGLE" or "EGL_ReleaseDeviceANGLE" or "EGL_GetPlatformDisplayEXT"
        or "EGL_Initialize" or "EGL_Terminate" or "EGL_BindAPI" or "EGL_ChooseConfig"
        or "EGL_CreateContext" or "EGL_DestroyContext" or "EGL_CreatePbufferSurface"
        or "EGL_CreatePbufferFromClientBuffer" or "EGL_CreateImageKHR" or "EGL_DestroyImageKHR" or "glGenTextures" or "glDeleteTextures"
        or "glBindTexture" or "glTexParameteri" or "glEGLImageTargetTexture2DOES" or "glFinish" or "glGetError"
        or "EGL_CreateWindowSurface" or "EGL_SwapBuffers" or "EGL_DestroySurface" or "EGL_MakeCurrent" or "EGL_GetError" or "EGL_GetProcAddress";

    private static bool IsLinuxEntryPoint(string library, string entryPoint) =>
        IsLinuxMediaEntryPoint(library, entryPoint)
        || (library is "libEGL.so.1" && entryPoint is "eglGetDisplay"
            or "eglInitialize"
            or "eglTerminate"
            or "eglBindAPI"
            or "eglChooseConfig"
            or "eglGetConfigAttrib"
            or "eglCreateContext"
            or "eglDestroyContext"
            or "eglCreateWindowSurface"
            or "eglDestroySurface"
            or "eglMakeCurrent"
            or "eglSwapBuffers"
            or "eglGetError"
            or "eglQueryString"
            or "eglGetProcAddress")
        || (library is "libGLESv2.so.2" && entryPoint is "glGetString"
            or "glGetError"
            or "glGenTextures"
            or "glDeleteTextures"
            or "glBindTexture"
            or "glTexParameteri"
            or "glFinish" or "glGetIntegerv" or "glGetStringi" or "glTexStorage2D")
        || (library is "libX11.so.6" && entryPoint is "XOpenDisplay"
            or "XCloseDisplay"
            or "XRootWindow"
            or "XGetVisualInfo"
            or "XFree"
            or "XCreateColormap"
            or "XFreeColormap"
            or "XCreateWindow"
            or "XDestroyWindow"
            or "XMapWindow"
            or "XStoreName"
            or "XInternAtom"
            or "XSetWMProtocols"
            or "XPending"
            or "XNextEvent"
            or "XFlush" or "XSetErrorHandler" or "XSync");

    private static bool IsLinuxMediaEntryPoint(string library, string entryPoint) =>
        (library is "libgstreamer-1.0.so.0" && entryPoint is "gst_init_check" or "gst_registry_fork_set_enabled"
            or "gst_registry_get" or "gst_registry_scan_path" or "gst_element_factory_find" or "gst_object_unref"
            or "gst_plugin_feature_load" or "gst_plugin_feature_get_plugin" or "gst_plugin_get_filename" or "gst_element_factory_create"
            or "gst_registry_get_feature_list_by_plugin" or "gst_plugin_feature_list_free" or "gst_element_factory_get_type"
            or "gst_object_get_name" or "gst_caps_from_string" or "gst_element_factory_can_sink_all_caps" or "gst_element_factory_can_src_any_caps"
            or "gst_mini_object_unref" or "gst_object_ref_sink" or "gst_element_set_context" or "gst_query_new_context"
            or "gst_element_query" or "gst_query_parse_context" or "gst_context_get_structure" or "gst_structure_get_value"
            or "gst_pipeline_new" or "gst_bin_add" or "gst_element_link" or "gst_element_set_state" or "gst_element_get_state"
            or "gst_element_get_bus" or "gst_bus_timed_pop_filtered" or "gst_message_parse_error" or "gst_buffer_new" or "gst_buffer_append_memory")
        || (library is "libgstapp-1.0.so.0" && entryPoint is "gst_app_src_set_caps" or "gst_app_src_get_current_level_buffers"
            or "gst_app_src_push_buffer" or "gst_app_src_end_of_stream")
        || (library is "libgstgl-1.0.so.0" && entryPoint is "gst_gl_display_egl_new_with_egl_display"
            or "gst_gl_display_filter_gl_api" or "gst_gl_context_new_wrapped" or "gst_gl_context_activate" or "gst_gl_context_fill_info"
            or "gst_gl_context_new" or "gst_gl_context_create" or "gst_gl_context_can_share")
        || (library is "libglib-2.0.so.0" && entryPoint is "g_error_free" or "g_free")
        || (library is "libgstcuda-1.0.so.0" && entryPoint is "gst_cuda_context_new" or "gst_cuda_context_push" or "gst_cuda_context_pop"
            or "gst_cuda_context_get_handle" or "gst_cuda_load_library" or "gst_cuda_allocator_alloc_wrapped" or "gst_context_new_cuda_context")
        || (library is "libgobject-2.0.so.0" && entryPoint is "g_type_check_instance_is_a" or "g_object_class_find_property"
            or "g_value_init" or "g_value_unset" or "g_object_get_property" or "g_value_get_uint" or "g_value_get_object"
            or "g_type_fundamental" or "g_object_set_property" or "g_value_set_int" or "g_value_set_uint" or "g_value_set_uint64"
            or "g_value_set_boolean" or "g_value_set_enum" or "g_param_value_validate" or "g_param_values_cmp")
        || (library is "libgstvideo-1.0.so.0" && entryPoint is "gst_video_info_new" or "gst_video_info_free"
            or "gst_video_info_set_format" or "gst_video_format_from_string")
        || (library is "libcuda.so.1" && entryPoint is "cuInit" or "cuGLGetDevices" or "cuGraphicsGLRegisterImage"
            or "cuGraphicsMapResources" or "cuGraphicsSubResourceGetMappedArray" or "cuMemcpy2D_v2" or "cuMemcpy2DUnaligned_v2"
            or "cuGraphicsUnmapResources" or "cuGraphicsUnregisterResource" or "cuMemAlloc_v2" or "cuMemFree_v2" or "cuCtxSynchronize");

    private static bool IsAppleFramework(string library)
    {
        const string root = "/System/Library/Frameworks/";
        if (!library.StartsWith(root, StringComparison.Ordinal))
        {
            return false;
        }

        string framework = library.Substring(root.Length);
        return framework is "AppKit.framework/AppKit" or "Foundation.framework/Foundation"
            or "QuartzCore.framework/QuartzCore" or "Metal.framework/Metal"
            or "CoreGraphics.framework/CoreGraphics" or "IOSurface.framework/IOSurface"
            or "AVFoundation.framework/AVFoundation" or "CoreMedia.framework/CoreMedia"
            or "CoreVideo.framework/CoreVideo" or "CoreFoundation.framework/CoreFoundation";
    }
}
