// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Browser;

/// <summary>
/// DENICE's own libpulse imports for the mixer output and the speech sink. They resolve through the runtime's own
/// library loading, not through libcef.so's redirected dlsym, so they always reach the real library.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: libpulse takes unmanaged structures and callbacks.")]
internal static unsafe partial class PulseNative
{
    /// <summary>PA_CONTEXT_READY.</summary>
    internal const int ContextReady = 4;

    /// <summary>PA_STREAM_READY.</summary>
    internal const int StreamReady = 2;

    /// <summary>PA_OPERATION_RUNNING.</summary>
    internal const int OperationRunning = 0;

    /// <summary>PA_STREAM_ADJUST_LATENCY.</summary>
    internal const uint AdjustLatency = 0x2000;

    private const string Pulse = "libpulse.so.0";

    /// <summary>Gets the current wall-clock time as libpulse's mainloop expects it, optionally later by some microseconds.</summary>
    /// <param name="later">Microseconds to add.</param>
    /// <returns>The time.</returns>
    internal static TimeValue Now(ulong later = 0)
    {
        TimeValue time;
        _ = GetTimeOfDay(&time);
        if (later > 0)
        {
            _ = TimeValueAdd(&time, later);
        }

        return time;
    }

    /// <summary>Calls pa_gettimeofday in DENICE's own libpulse.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_gettimeofday")]
    internal static partial nint GetTimeOfDay(TimeValue* time);

    /// <summary>Calls pa_timeval_add in DENICE's own libpulse.</summary>
    /// <param name="time">The time.</param>
    /// <param name="microseconds">The microseconds.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_timeval_add")]
    internal static partial nint TimeValueAdd(TimeValue* time, ulong microseconds);

    /// <summary>Calls pa_threaded_mainloop_new in DENICE's own libpulse.</summary>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_threaded_mainloop_new")]
    internal static partial nint MainloopNew();

    /// <summary>Calls pa_threaded_mainloop_free in DENICE's own libpulse.</summary>
    /// <param name="mainloop">The mainloop.</param>
    [LibraryImport(Pulse, EntryPoint = "pa_threaded_mainloop_free")]
    internal static partial void MainloopFree(nint mainloop);

    /// <summary>Calls pa_threaded_mainloop_start in DENICE's own libpulse.</summary>
    /// <param name="mainloop">The mainloop.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_threaded_mainloop_start")]
    internal static partial int MainloopStart(nint mainloop);

    /// <summary>Calls pa_threaded_mainloop_stop in DENICE's own libpulse.</summary>
    /// <param name="mainloop">The mainloop.</param>
    [LibraryImport(Pulse, EntryPoint = "pa_threaded_mainloop_stop")]
    internal static partial void MainloopStop(nint mainloop);

    /// <summary>Calls pa_threaded_mainloop_lock in DENICE's own libpulse.</summary>
    /// <param name="mainloop">The mainloop.</param>
    [LibraryImport(Pulse, EntryPoint = "pa_threaded_mainloop_lock")]
    internal static partial void MainloopLock(nint mainloop);

    /// <summary>Calls pa_threaded_mainloop_unlock in DENICE's own libpulse.</summary>
    /// <param name="mainloop">The mainloop.</param>
    [LibraryImport(Pulse, EntryPoint = "pa_threaded_mainloop_unlock")]
    internal static partial void MainloopUnlock(nint mainloop);

    /// <summary>Calls pa_threaded_mainloop_wait in DENICE's own libpulse.</summary>
    /// <param name="mainloop">The mainloop.</param>
    [LibraryImport(Pulse, EntryPoint = "pa_threaded_mainloop_wait")]
    internal static partial void MainloopWait(nint mainloop);

    /// <summary>Calls pa_threaded_mainloop_signal in DENICE's own libpulse.</summary>
    /// <param name="mainloop">The mainloop.</param>
    /// <param name="waitForAccept">The waitForAccept.</param>
    [LibraryImport(Pulse, EntryPoint = "pa_threaded_mainloop_signal")]
    internal static partial void MainloopSignal(nint mainloop, int waitForAccept);

    /// <summary>Calls pa_threaded_mainloop_get_api in DENICE's own libpulse.</summary>
    /// <param name="mainloop">The mainloop.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_threaded_mainloop_get_api")]
    internal static partial nint MainloopGetApi(nint mainloop);

    /// <summary>Calls pa_context_new in DENICE's own libpulse.</summary>
    /// <param name="api">The api.</param>
    /// <param name="name">The name.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_context_new", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint ContextNew(nint api, string name);

    /// <summary>Calls pa_context_set_state_callback in DENICE's own libpulse.</summary>
    /// <param name="context">The context.</param>
    /// <param name="callback">The callback.</param>
    /// <param name="userdata">The userdata.</param>
    [LibraryImport(Pulse, EntryPoint = "pa_context_set_state_callback")]
    internal static partial void ContextSetStateCallback(nint context, nint callback, nint userdata);

    /// <summary>Calls pa_context_connect in DENICE's own libpulse.</summary>
    /// <param name="context">The context.</param>
    /// <param name="server">The server.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="api">The api.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_context_connect")]
    internal static partial int ContextConnect(nint context, nint server, int flags, nint api);

    /// <summary>Calls pa_context_get_state in DENICE's own libpulse.</summary>
    /// <param name="context">The context.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_context_get_state")]
    internal static partial int ContextGetState(nint context);

    /// <summary>Calls pa_context_errno in DENICE's own libpulse.</summary>
    /// <param name="context">The context.</param>
    /// <returns>The last error.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_context_errno")]
    internal static partial int ContextErrno(nint context);

    /// <summary>Calls pa_context_disconnect in DENICE's own libpulse.</summary>
    /// <param name="context">The context.</param>
    [LibraryImport(Pulse, EntryPoint = "pa_context_disconnect")]
    internal static partial void ContextDisconnect(nint context);

    /// <summary>Calls pa_context_unref in DENICE's own libpulse.</summary>
    /// <param name="context">The context.</param>
    [LibraryImport(Pulse, EntryPoint = "pa_context_unref")]
    internal static partial void ContextUnref(nint context);

    /// <summary>Calls pa_context_load_module in DENICE's own libpulse.</summary>
    /// <param name="context">The context.</param>
    /// <param name="name">The name.</param>
    /// <param name="argument">The argument.</param>
    /// <param name="callback">The callback.</param>
    /// <param name="userdata">The userdata.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_context_load_module", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint ContextLoadModule(nint context, string name, string argument, nint callback, nint userdata);

    /// <summary>Calls pa_context_unload_module in DENICE's own libpulse.</summary>
    /// <param name="context">The context.</param>
    /// <param name="index">The index.</param>
    /// <param name="callback">The callback.</param>
    /// <param name="userdata">The userdata.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_context_unload_module")]
    internal static partial nint ContextUnloadModule(nint context, uint index, nint callback, nint userdata);

    /// <summary>Calls pa_context_get_module_info_list in DENICE's own libpulse.</summary>
    /// <param name="context">The context.</param>
    /// <param name="callback">The callback.</param>
    /// <param name="userdata">The userdata.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_context_get_module_info_list")]
    internal static partial nint ContextGetModuleInfoList(nint context, nint callback, nint userdata);

    /// <summary>Calls pa_stream_new in DENICE's own libpulse.</summary>
    /// <param name="context">The context.</param>
    /// <param name="name">The name.</param>
    /// <param name="spec">The spec.</param>
    /// <param name="map">The map.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_stream_new", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint StreamNew(nint context, string name, SampleSpec* spec, nint map);

    /// <summary>Calls pa_stream_set_state_callback in DENICE's own libpulse.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="callback">The callback.</param>
    /// <param name="userdata">The userdata.</param>
    [LibraryImport(Pulse, EntryPoint = "pa_stream_set_state_callback")]
    internal static partial void StreamSetStateCallback(nint stream, nint callback, nint userdata);

    /// <summary>Calls pa_stream_set_write_callback in DENICE's own libpulse.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="callback">The callback.</param>
    /// <param name="userdata">The userdata.</param>
    [LibraryImport(Pulse, EntryPoint = "pa_stream_set_write_callback")]
    internal static partial void StreamSetWriteCallback(nint stream, nint callback, nint userdata);

    /// <summary>Calls pa_stream_set_read_callback in DENICE's own libpulse.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="callback">The callback.</param>
    /// <param name="userdata">The userdata.</param>
    [LibraryImport(Pulse, EntryPoint = "pa_stream_set_read_callback")]
    internal static partial void StreamSetReadCallback(nint stream, nint callback, nint userdata);

    /// <summary>Calls pa_stream_connect_playback in DENICE's own libpulse.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="device">The device.</param>
    /// <param name="attributes">The attributes.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="volume">The volume.</param>
    /// <param name="sync">The sync.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_stream_connect_playback")]
    internal static partial int StreamConnectPlayback(nint stream, nint device, BufferAttributes* attributes, uint flags, nint volume, nint sync);

    /// <summary>Calls pa_stream_connect_record in DENICE's own libpulse.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="device">The device.</param>
    /// <param name="attributes">The attributes.</param>
    /// <param name="flags">The flags.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_stream_connect_record", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int StreamConnectRecord(nint stream, string device, BufferAttributes* attributes, uint flags);

    /// <summary>Calls pa_stream_get_state in DENICE's own libpulse.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_stream_get_state")]
    internal static partial int StreamGetState(nint stream);

    /// <summary>Calls pa_stream_begin_write in DENICE's own libpulse.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="data">The data.</param>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_stream_begin_write")]
    internal static partial int StreamBeginWrite(nint stream, nint* data, nuint* bytes);

    /// <summary>Calls pa_stream_write in DENICE's own libpulse.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="data">The data.</param>
    /// <param name="bytes">The bytes.</param>
    /// <param name="free">The free.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="seek">The seek.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_stream_write")]
    internal static partial int StreamWrite(nint stream, nint data, nuint bytes, nint free, long offset, int seek);

    /// <summary>Calls pa_stream_peek in DENICE's own libpulse.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="data">The data.</param>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_stream_peek")]
    internal static partial int StreamPeek(nint stream, nint* data, nuint* bytes);

    /// <summary>Calls pa_stream_drop in DENICE's own libpulse.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_stream_drop")]
    internal static partial int StreamDrop(nint stream);

    /// <summary>Calls pa_stream_disconnect in DENICE's own libpulse.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_stream_disconnect")]
    internal static partial int StreamDisconnect(nint stream);

    /// <summary>Calls pa_stream_unref in DENICE's own libpulse.</summary>
    /// <param name="stream">The stream.</param>
    [LibraryImport(Pulse, EntryPoint = "pa_stream_unref")]
    internal static partial void StreamUnref(nint stream);

    /// <summary>Calls pa_operation_get_state in DENICE's own libpulse.</summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Pulse, EntryPoint = "pa_operation_get_state")]
    internal static partial int OperationGetState(nint operation);

    /// <summary>Calls pa_operation_unref in DENICE's own libpulse.</summary>
    /// <param name="operation">The operation.</param>
    [LibraryImport(Pulse, EntryPoint = "pa_operation_unref")]
    internal static partial void OperationUnref(nint operation);

    /// <summary>struct timeval.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TimeValue
    {
        /// <summary>Seconds.</summary>
        public long Seconds;

        /// <summary>Microseconds.</summary>
        public long Microseconds;
    }

    /// <summary>pa_sample_spec.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SampleSpec
    {
        /// <summary>pa_sample_format_t.</summary>
        public int Format;

        /// <summary>The rate.</summary>
        public uint Rate;

        /// <summary>The channels.</summary>
        public byte Channels;
    }

    /// <summary>pa_buffer_attr.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BufferAttributes
    {
        /// <summary>maxlength.</summary>
        public uint MaximumLength;

        /// <summary>tlength.</summary>
        public uint TargetLength;

        /// <summary>prebuf.</summary>
        public uint Prebuffer;

        /// <summary>minreq.</summary>
        public uint MinimumRequest;

        /// <summary>fragsize.</summary>
        public uint FragmentSize;
    }
}
