// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Recording;

/// <summary>Binds the core and app APIs used to own a bounded native encoder pipeline.</summary>
internal static partial class GstPipelineNative
{
    /// <summary>Invokes gst_pipeline_new.</summary>
    /// <param name="name">The native name argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_pipeline_new", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint NewPipeline(string name);

    /// <summary>Invokes gst_bin_add.</summary>
    /// <param name="pipeline">The native pipeline argument.</param>
    /// <param name="element">The native element argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_bin_add")]
    internal static partial int AddElement(nint pipeline, nint element);

    /// <summary>Invokes gst_element_link.</summary>
    /// <param name="source">The native source argument.</param>
    /// <param name="destination">The native destination argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_element_link")]
    internal static partial int Link(nint source, nint destination);

    /// <summary>Invokes gst_element_set_state.</summary>
    /// <param name="element">The native element argument.</param>
    /// <param name="state">The native state argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_element_set_state")]
    internal static partial int SetState(nint element, int state);

    /// <summary>Invokes gst_element_get_state.</summary>
    /// <param name="element">The native element argument.</param>
    /// <param name="state">The native state argument.</param>
    /// <param name="pending">The native pending argument.</param>
    /// <param name="timeout">The native timeout argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_element_get_state")]
    internal static partial int GetState(nint element, out int state, out int pending, ulong timeout);

    /// <summary>Invokes gst_element_get_bus.</summary>
    /// <param name="element">The native element argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_element_get_bus")]
    internal static partial nint GetBus(nint element);

    /// <summary>Invokes gst_bus_timed_pop_filtered.</summary>
    /// <param name="bus">The native bus argument.</param>
    /// <param name="timeout">The native timeout argument.</param>
    /// <param name="types">The native types argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_bus_timed_pop_filtered")]
    internal static partial nint PopBus(nint bus, ulong timeout, uint types);

    /// <summary>Invokes gst_message_parse_error.</summary>
    /// <param name="message">The native message argument.</param>
    /// <param name="error">The native error argument.</param>
    /// <param name="debug">The native debug argument.</param>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_message_parse_error")]
    internal static partial void ParseError(nint message, out nint error, out nint debug);

    /// <summary>Invokes gst_buffer_new.</summary>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_buffer_new")]
    internal static partial nint NewBuffer();

    /// <summary>Invokes gst_buffer_append_memory.</summary>
    /// <param name="buffer">The native buffer argument.</param>
    /// <param name="memory">The native memory argument.</param>
    [LibraryImport("libgstreamer-1.0.so.0", EntryPoint = "gst_buffer_append_memory")]
    internal static partial void AppendMemory(nint buffer, nint memory);

    /// <summary>Invokes gst_app_src_set_caps.</summary>
    /// <param name="source">The native source argument.</param>
    /// <param name="caps">The native caps argument.</param>
    [LibraryImport("libgstapp-1.0.so.0", EntryPoint = "gst_app_src_set_caps")]
    internal static partial void SetSourceCaps(nint source, nint caps);

    /// <summary>Invokes gst_app_src_get_current_level_buffers.</summary>
    /// <param name="source">The native source argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstapp-1.0.so.0", EntryPoint = "gst_app_src_get_current_level_buffers")]
    internal static partial ulong QueuedBuffers(nint source);

    /// <summary>Invokes gst_app_src_push_buffer.</summary>
    /// <param name="source">The native source argument.</param>
    /// <param name="buffer">The native buffer argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstapp-1.0.so.0", EntryPoint = "gst_app_src_push_buffer")]
    internal static partial int PushBuffer(nint source, nint buffer);

    /// <summary>Invokes gst_app_src_end_of_stream.</summary>
    /// <param name="source">The native source argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgstapp-1.0.so.0", EntryPoint = "gst_app_src_end_of_stream")]
    internal static partial int EndStream(nint source);

    /// <summary>Writes only the public timestamp fields of a newly owned, writable GstBuffer.</summary>
    /// <param name="buffer">The new unshared buffer.</param>
    /// <param name="timestamp">Its original presentation time in nanoseconds.</param>
    /// <param name="duration">Its duration in nanoseconds.</param>
    [SuppressMessage("Security", "S6640", Justification = "GStreamer exposes buffer timestamps as public struct fields; the tested 64-bit ABI is written only before ownership transfer.")]
    internal static unsafe void SetTiming(nint buffer, ulong timestamp, ulong duration)
    {
        var header = (BufferHeader*)buffer;
        header->PresentationTime = timestamp;
        header->DecodeTime = ulong.MaxValue;
        header->Duration = duration;
    }

    /// <summary>Reads the public GstMessage type immediately after its 64-byte miniobject header.</summary>
    /// <param name="message">The owned message.</param>
    /// <returns>The GstMessageType flags.</returns>
    internal static uint MessageType(nint message) => unchecked((uint)Marshal.ReadInt32(message, 64));

    /// <summary>Matches the public 64-bit GstBuffer layout while leaving private fields opaque.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 112)]
    internal struct BufferHeader
    {
        /// <summary>The original presentation timestamp.</summary>
        [FieldOffset(72)]
        internal ulong PresentationTime;

        /// <summary>The decoding timestamp, or GST_CLOCK_TIME_NONE.</summary>
        [FieldOffset(80)]
        internal ulong DecodeTime;

        /// <summary>The sample duration.</summary>
        [FieldOffset(88)]
        internal ulong Duration;
    }
}
