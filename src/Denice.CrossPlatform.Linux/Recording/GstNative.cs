// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Recording;

/// <summary>Binds the packaged GStreamer initialization API.</summary>
internal static partial class GstNative
{
    /// <summary>Initializes GStreamer's CUDA driver dispatch table.</summary>
    /// <returns>Whether the host driver exports the required API.</returns>
    [LibraryImport("libgstcuda-1.0.so.0", EntryPoint = "gst_cuda_load_library")]
    internal static partial int LoadCudaDriver();

    /// <summary>Invokes gst_cuda_context_new.</summary>
    /// <param name="device">The native device argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstcuda-1.0.so.0", EntryPoint = "gst_cuda_context_new")]
    internal static partial nint NewCudaContext(uint device);

    /// <summary>Invokes gst_cuda_context_push.</summary>
    /// <param name="context">The native context argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstcuda-1.0.so.0", EntryPoint = "gst_cuda_context_push")]
    internal static partial int PushCudaContext(nint context);

    /// <summary>Invokes gst_cuda_context_pop.</summary>
    /// <param name="context">The native context argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstcuda-1.0.so.0", EntryPoint = "gst_cuda_context_pop")]
    internal static partial int PopCudaContext(out nint context);

    /// <summary>Gets the native CUDA context owned by GStreamer.</summary>
    /// <param name="context">The GstCudaContext.</param>
    /// <returns>The borrowed CUDA context.</returns>
    [LibraryImport("libgstcuda-1.0.so.0", EntryPoint = "gst_cuda_context_get_handle")]
    internal static partial nint GetCudaContextHandle(nint context);

    /// <summary>Invokes gst_cuda_allocator_alloc_wrapped.</summary>
    /// <param name="allocator">The native allocator argument.</param>
    /// <param name="context">The native context argument.</param>
    /// <param name="stream">The native stream argument.</param>
    /// <param name="info">The native info argument.</param>
    /// <param name="pointer">The native pointer argument.</param>
    /// <param name="userData">The native userData argument.</param>
    /// <param name="notify">The native notify argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstcuda-1.0.so.0", EntryPoint = "gst_cuda_allocator_alloc_wrapped")]
    internal static partial nint WrapCudaMemory(nint allocator, nint context, nint stream, nint info, ulong pointer, nint userData, nint notify);

    /// <summary>Invokes gst_video_info_new.</summary>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstvideo-1.0.so.0", EntryPoint = "gst_video_info_new")]
    internal static partial nint NewVideoInfo();

    /// <summary>Invokes gst_video_info_free.</summary>
    /// <param name="info">The native info argument.</param>
    [LibraryImport("libgstvideo-1.0.so.0", EntryPoint = "gst_video_info_free")]
    internal static partial void FreeVideoInfo(nint info);

    /// <summary>Invokes gst_video_info_set_format.</summary>
    /// <param name="info">The native info argument.</param>
    /// <param name="format">The native format argument.</param>
    /// <param name="width">The native width argument.</param>
    /// <param name="height">The native height argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstvideo-1.0.so.0", EntryPoint = "gst_video_info_set_format")]
    internal static partial int SetVideoFormat(nint info, int format, uint width, uint height);

    /// <summary>Invokes gst_video_format_from_string.</summary>
    /// <param name="format">The native format argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstvideo-1.0.so.0", EntryPoint = "gst_video_format_from_string", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int ParseVideoFormat(string format);

    /// <summary>Invokes gst_registry_get_feature_list_by_plugin.</summary>
    /// <param name="registry">The native registry argument.</param>
    /// <param name="plugin">The native plugin argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_registry_get_feature_list_by_plugin", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetPluginFeatures(nint registry, string plugin);

    /// <summary>Invokes gst_plugin_feature_list_free.</summary>
    /// <param name="list">The native list argument.</param>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_plugin_feature_list_free")]
    internal static partial void FreePluginFeatures(nint list);

    /// <summary>Invokes gst_element_factory_get_type.</summary>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_element_factory_get_type")]
    internal static partial nuint ElementFactoryType();

    /// <summary>Invokes gst_object_get_name.</summary>
    /// <param name="instance">The native instance argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_object_get_name")]
    internal static partial nint GetObjectName(nint instance);

    /// <summary>Invokes gst_caps_from_string.</summary>
    /// <param name="caps">The native caps argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_caps_from_string", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint CapsFromString(string caps);

    /// <summary>Invokes gst_element_factory_can_sink_all_caps.</summary>
    /// <param name="factory">The native factory argument.</param>
    /// <param name="caps">The native caps argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_element_factory_can_sink_all_caps")]
    internal static partial int FactoryAcceptsInput(nint factory, nint caps);

    /// <summary>Invokes gst_element_factory_can_src_any_caps.</summary>
    /// <param name="factory">The native factory argument.</param>
    /// <param name="caps">The native caps argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_element_factory_can_src_any_caps")]
    internal static partial int FactoryProducesOutput(nint factory, nint caps);

    /// <summary>Invokes gst_mini_object_unref.</summary>
    /// <param name="instance">The native instance argument.</param>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_mini_object_unref")]
    internal static partial void UnrefMiniObject(nint instance);

    /// <summary>Invokes gst_object_ref_sink.</summary>
    /// <param name="instance">The native instance argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_object_ref_sink")]
    internal static partial nint RefSink(nint instance);

    /// <summary>Invokes gst_element_set_context.</summary>
    /// <param name="element">The native element argument.</param>
    /// <param name="context">The native context argument.</param>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_element_set_context")]
    internal static partial void SetContext(nint element, nint context);

    /// <summary>Invokes gst_query_new_context.</summary>
    /// <param name="type">The native type argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_query_new_context", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint NewContextQuery(string type);

    /// <summary>Invokes gst_element_query.</summary>
    /// <param name="element">The native element argument.</param>
    /// <param name="query">The native query argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_element_query")]
    internal static partial int QueryElement(nint element, nint query);

    /// <summary>Invokes gst_query_parse_context.</summary>
    /// <param name="query">The native query argument.</param>
    /// <param name="context">The native context argument.</param>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_query_parse_context")]
    internal static partial void ParseContextQuery(nint query, out nint context);

    /// <summary>Invokes gst_context_get_structure.</summary>
    /// <param name="context">The native context argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_context_get_structure")]
    internal static partial nint ContextStructure(nint context);

    /// <summary>Invokes gst_structure_get_value.</summary>
    /// <param name="structure">The native structure argument.</param>
    /// <param name="field">The native field argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_structure_get_value", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint StructureValue(nint structure, string field);

    /// <summary>Creates a context message carrying the exact CUDA context.</summary>
    /// <param name="context">The borrowed GstCudaContext.</param>
    /// <returns>The owned GstContext.</returns>
    [LibraryImport("libgstcuda-1.0.so.0", EntryPoint = "gst_context_new_cuda_context")]
    internal static partial nint NewCudaContextMessage(nint context);

    /// <summary>Releases GLib-allocated storage.</summary>
    /// <param name="memory">The allocation.</param>
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_free")]
    internal static partial void Free(nint memory);

    /// <summary>Invokes gst_init_check.</summary>
    /// <param name="argc">The native argc argument.</param>
    /// <param name="argv">The native argv argument.</param>
    /// <param name="error">The native error argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_init_check")]
    internal static partial int Initialize(nint argc, nint argv, out nint error);

    /// <summary>Invokes gst_registry_fork_set_enabled.</summary>
    /// <param name="enabled">The native enabled argument.</param>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_registry_fork_set_enabled")]
    internal static partial void SetRegistryFork(int enabled);

    /// <summary>Invokes gst_registry_get.</summary>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_registry_get")]
    internal static partial nint GetRegistry();

    /// <summary>Invokes gst_registry_scan_path.</summary>
    /// <param name="registry">The native registry argument.</param>
    /// <param name="path">The native path argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_registry_scan_path", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int ScanRegistry(nint registry, string path);

    /// <summary>Invokes gst_element_factory_find.</summary>
    /// <param name="name">The native name argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_element_factory_find", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint FindElementFactory(string name);

    /// <summary>Loads a factory and returns a separately owned reference.</summary>
    /// <param name="feature">The borrowed factory.</param>
    /// <returns>The loaded factory, or zero.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_plugin_feature_load")]
    internal static partial nint LoadFeature(nint feature);

    /// <summary>Gets an owned reference to a feature's plugin.</summary>
    /// <param name="feature">The borrowed factory.</param>
    /// <returns>The owned plugin, or zero.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_plugin_feature_get_plugin")]
    internal static partial nint GetFeaturePlugin(nint feature);

    /// <summary>Gets a filename borrowed from a plugin.</summary>
    /// <param name="plugin">The plugin.</param>
    /// <returns>The borrowed UTF-8 filename.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_plugin_get_filename")]
    internal static partial nint GetPluginFilename(nint plugin);

    /// <summary>Creates an element from a selected factory.</summary>
    /// <param name="factory">The loaded factory.</param>
    /// <param name="name">The instance name.</param>
    /// <returns>The owned floating element reference, or zero.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_element_factory_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint CreateElement(nint factory, string name);

    /// <summary>Invokes gst_object_unref.</summary>
    /// <param name="instance">The native instance argument.</param>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_object_unref")]
    internal static partial void Unref(nint instance);

    /// <summary>Invokes g_error_free.</summary>
    /// <param name="error">The native error argument.</param>
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_error_free")]
    internal static partial void FreeError(nint error);
}
