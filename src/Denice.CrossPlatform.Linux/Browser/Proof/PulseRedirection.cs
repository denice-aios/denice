// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Linux.Browser;

/// <summary>
/// The DENICE implementations libcef.so's redirected dlsym hands out for Chromium's PulseAudio playback names
/// (windows-linux-audio-proof.md, Linux interception boundary). pa_stream_new still creates Chromium's real stream;
/// pa_stream_connect_playback turns it into a <see cref="PulsePlaybackStream"/> pulled by the mixer, and
/// pa_stream_connect_record leaves it real. Every call on a stream DENICE did not take over goes to the real function.
/// pa_context_get_server_info is passed through with its sample rate set to 48000; pa_context_new is passed through and
/// its mainloop API kept for the stream's time event.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: UnmanagedCallersOnly replacements exchange Chromium's PulseAudio structures and callbacks.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross into Chromium; the first one is recorded in the mixer.")]
internal static unsafe partial class PulseRedirection
{
    private const int StreamReady = 2;
    private const int StreamTerminated = 4;
    private const int OperationDone = 1;
    private const uint InvalidIndex = uint.MaxValue;
    private const uint StartCorked = 0x1;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<nint, PulsePlaybackStream> Playback = [];
    private static readonly Dictionary<nint, PendingStream> Pending = [];
    private static readonly HashSet<nint> Operations = [];
    private static readonly Dictionary<nint, nint> ContextApis = [];
    private static MixerCore? mixer;

    /// <summary>Gets the replacement function for each redirected name.</summary>
    internal static IReadOnlyDictionary<string, nint> Replacements { get; } = new Dictionary<string, nint>(StringComparer.Ordinal)
    {
        ["pa_context_new"] = (nint)(delegate* unmanaged<nint, nint, nint>)&ContextNew,
        ["pa_context_get_server_info"] = (nint)(delegate* unmanaged<nint, nint, nint, nint>)&GetServerInfo,
        ["pa_stream_new"] = (nint)(delegate* unmanaged<nint, nint, nint, nint, nint>)&StreamNew,
        ["pa_stream_new_with_proplist"] = (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, nint>)&StreamNewWithProplist,
        ["pa_stream_set_state_callback"] = (nint)(delegate* unmanaged<nint, nint, nint, void>)&SetStateCallback,
        ["pa_stream_set_write_callback"] = (nint)(delegate* unmanaged<nint, nint, nint, void>)&SetWriteCallback,
        ["pa_stream_connect_playback"] = (nint)(delegate* unmanaged<nint, nint, nint, uint, nint, nint, int>)&ConnectPlayback,
        ["pa_stream_connect_record"] = (nint)(delegate* unmanaged<nint, nint, nint, uint, int>)&ConnectRecord,
        ["pa_stream_get_state"] = (nint)(delegate* unmanaged<nint, int>)&GetState,
        ["pa_stream_begin_write"] = (nint)(delegate* unmanaged<nint, nint*, nuint*, int>)&BeginWrite,
        ["pa_stream_write"] = (nint)(delegate* unmanaged<nint, nint, nuint, nint, long, int, int>)&Write,
        ["pa_stream_cork"] = (nint)(delegate* unmanaged<nint, int, nint, nint, nint>)&Cork,
        ["pa_stream_flush"] = (nint)(delegate* unmanaged<nint, nint, nint, nint>)&Flush,
        ["pa_stream_get_latency"] = (nint)(delegate* unmanaged<nint, ulong*, int*, int>)&GetLatency,
        ["pa_stream_get_device_index"] = (nint)(delegate* unmanaged<nint, uint>)&GetDeviceIndex,
        ["pa_stream_disconnect"] = (nint)(delegate* unmanaged<nint, int>)&Disconnect,
        ["pa_stream_unref"] = (nint)(delegate* unmanaged<nint, void>)&Unref,
        ["pa_operation_get_state"] = (nint)(delegate* unmanaged<nint, int>)&OperationGetState,
        ["pa_operation_unref"] = (nint)(delegate* unmanaged<nint, void>)&OperationUnref,
        ["pa_operation_cancel"] = (nint)(delegate* unmanaged<nint, void>)&OperationCancel,
    };

    /// <summary>Gets the playback streams DENICE holds now.</summary>
    internal static int OpenStreams
    {
        get
        {
            lock (Gate)
            {
                return Playback.Count;
            }
        }
    }

    /// <summary>Sets the mixer that pulls every redirected playback stream.</summary>
    /// <param name="target">The started mixer core.</param>
    internal static void UseMixer(MixerCore target) => mixer = target;

    private static nint Real(string name) => LinuxAudioRedirection.Real(name);

    private static PulsePlaybackStream? Find(nint stream)
    {
        lock (Gate)
        {
            return Playback.GetValueOrDefault(stream);
        }
    }

    private static void Fail(string what, Exception error)
    {
        AudioProofLog.Note($"FAILED {what}: {error}");
        mixer?.Fail($"Browser audio redirection {what} failed: {error.Message}");
    }

    private static nint NewOperation()
    {
        nint operation = Marshal.AllocHGlobal(16);
        lock (Gate)
        {
            _ = Operations.Add(operation);
        }

        return operation;
    }

    private static bool IsOwnOperation(nint operation)
    {
        lock (Gate)
        {
            return Operations.Contains(operation);
        }
    }

    [UnmanagedCallersOnly]
    private static nint ContextNew(nint api, nint name)
    {
        nint context = ((delegate* unmanaged<nint, nint, nint>)Real("pa_context_new"))(api, name);
        lock (Gate)
        {
            ContextApis[context] = api;
        }

        AudioProofLog.Note($"pa_context_new {Marshal.PtrToStringUTF8(name)} -> 0x{context:x}");
        return context;
    }

    [UnmanagedCallersOnly]
    private static nint GetServerInfo(nint context, nint callback, nint userdata)
    {
        var box = GCHandle.Alloc(new PulsePlaybackStream.Callback(callback, userdata));
        AudioProofLog.Count("pa_context_get_server_info");
        return ((delegate* unmanaged<nint, nint, nint, nint>)Real("pa_context_get_server_info"))(context, (nint)(delegate* unmanaged<nint, nint, nint, void>)&ServerInfoArrived, GCHandle.ToIntPtr(box));
    }

    [UnmanagedCallersOnly]
    private static void ServerInfoArrived(nint context, nint info, nint userdata)
    {
        var box = GCHandle.FromIntPtr(userdata);
        var original = (PulsePlaybackStream.Callback)box.Target!;
        box.Free();
        if (info == 0)
        {
            ((delegate* unmanaged<nint, nint, nint, void>)original.Procedure)(context, info, original.Context);
            return;
        }

        // pa_server_info: four strings, then pa_sample_spec { format, rate, channels } at 32, ..., 200 bytes in all.
        byte* copy = stackalloc byte[256];
        new ReadOnlySpan<byte>((void*)info, 200).CopyTo(new Span<byte>(copy, 200));
        uint rate = *(uint*)(copy + 36);
        *(uint*)(copy + 36) = MixerCore.SampleRate;
        AudioProofLog.Note($"pa_context_get_server_info: server rate {rate} reported to Chromium as {MixerCore.SampleRate}, channels {copy[40]}, default sink {Marshal.PtrToStringUTF8(*(nint*)(copy + 48))}");
        ((delegate* unmanaged<nint, nint, nint, void>)original.Procedure)(context, (nint)copy, original.Context);
    }

    [UnmanagedCallersOnly]
    private static nint StreamNew(nint context, nint name, nint spec, nint map)
    {
        nint stream = ((delegate* unmanaged<nint, nint, nint, nint, nint>)Real("pa_stream_new"))(context, name, spec, map);
        Track(stream, context, name, spec);
        return stream;
    }

    [UnmanagedCallersOnly]
    private static nint StreamNewWithProplist(nint context, nint name, nint spec, nint map, nint properties)
    {
        nint stream = ((delegate* unmanaged<nint, nint, nint, nint, nint, nint>)Real("pa_stream_new_with_proplist"))(context, name, spec, map, properties);
        Track(stream, context, name, spec);
        return stream;
    }

    private static void Track(nint stream, nint context, nint name, nint spec)
    {
        if (stream == 0)
        {
            return;
        }

        var pending = new PendingStream(context, *(int*)spec, *(int*)(spec + 4), *(byte*)(spec + 8));
        lock (Gate)
        {
            Pending[stream] = pending;
        }

        AudioProofLog.Note($"pa_stream_new {Marshal.PtrToStringUTF8(name)} -> 0x{stream:x} format {pending.Format} rate {pending.Rate} channels {pending.Channels}");
    }

    [UnmanagedCallersOnly]
    private static void SetStateCallback(nint stream, nint callback, nint userdata) => SetCallback(stream, new PulsePlaybackStream.Callback(callback, userdata), false);

    [UnmanagedCallersOnly]
    private static void SetWriteCallback(nint stream, nint callback, nint userdata) => SetCallback(stream, new PulsePlaybackStream.Callback(callback, userdata), true);

    private static void SetCallback(nint stream, PulsePlaybackStream.Callback callback, bool write)
    {
        if (Find(stream) is { } playback)
        {
            if (write)
            {
                playback.WriteCallback = callback;
            }
            else
            {
                playback.StateCallback = callback;
            }

            return;
        }

        lock (Gate)
        {
            if (Pending.TryGetValue(stream, out PendingStream pending))
            {
                Pending[stream] = write ? pending with { WriteCallback = callback } : pending with { StateCallback = callback };
            }
        }

        // Until the stream connects its direction is unknown, so the real stream gets the callback too.
        string name = write ? "pa_stream_set_write_callback" : "pa_stream_set_state_callback";
        ((delegate* unmanaged<nint, nint, nint, void>)Real(name))(stream, callback.Procedure, callback.Context);
    }

    [UnmanagedCallersOnly]
    private static int ConnectPlayback(nint stream, nint device, nint attributes, uint flags, nint volume, nint sync)
    {
        try
        {
            PendingStream pending;
            lock (Gate)
            {
                if (!Pending.Remove(stream, out pending))
                {
                    AudioProofLog.Count("real playback connects");
                    AudioProofLog.Note($"UNREDIRECTED pa_stream_connect_playback 0x{stream:x}");
                    return ((delegate* unmanaged<nint, nint, nint, uint, nint, nint, int>)Real("pa_stream_connect_playback"))(stream, device, attributes, flags, volume, sync);
                }
            }

            return Redirect(stream, pending, device, attributes, flags);
        }
        catch (Exception error)
        {
            Fail("pa_stream_connect_playback", error);
            return -1;
        }
    }

    private static int Redirect(nint stream, PendingStream pending, nint device, nint attributes, uint flags)
    {
        // Chromium's real stream is never connected, so its own callbacks can never fire.
        ((delegate* unmanaged<nint, nint, nint, void>)Real("pa_stream_set_state_callback"))(stream, 0, 0);
        ((delegate* unmanaged<nint, nint, nint, void>)Real("pa_stream_set_write_callback"))(stream, 0, 0);
        uint tlength = attributes == 0 ? uint.MaxValue : *(uint*)(attributes + 4);
        uint minreq = attributes == 0 ? uint.MaxValue : *(uint*)(attributes + 12);
        int bytesPerFrame = pending.Channels * (pending.Format == PulsePlaybackStream.Signed16 ? 2 : 4);
        int request = minreq is 0 or uint.MaxValue ? pending.Rate / 100 * bytesPerFrame : (int)minreq;
        var playback = new PulsePlaybackStream(stream, pending.Format, pending.Rate, pending.Channels, request)
        {
            Device = Marshal.PtrToStringUTF8(device),
            Corked = (flags & StartCorked) != 0,
            WriteCallback = pending.WriteCallback,
            StateCallback = pending.StateCallback,
            State = StreamReady,
        };
        if (pending.Format is not (PulsePlaybackStream.Float32 or PulsePlaybackStream.Signed16))
        {
            mixer?.Fail($"A browser PulseAudio stream uses sample format {pending.Format}, which the proof does not convert.");
        }

        lock (Gate)
        {
            Playback[stream] = playback;
            playback.Api = ContextApis.GetValueOrDefault(pending.Context);
        }

        mixer?.Register(playback);
        StartTimer(playback);
        if (playback.StateCallback.Procedure != 0)
        {
            ((delegate* unmanaged<nint, nint, void>)playback.StateCallback.Procedure)(stream, playback.StateCallback.Context);
        }

        AudioProofLog.Count("redirected playback connects");
        AudioProofLog.Note($"redirected pa_stream_connect_playback 0x{stream:x} device={playback.Device ?? "(default)"} format {pending.Format} rate {pending.Rate} channels {pending.Channels} flags 0x{flags:x} tlength {tlength} minreq {minreq} request {request} bytes, api 0x{playback.Api:x}");
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int ConnectRecord(nint stream, nint device, nint attributes, uint flags)
    {
        lock (Gate)
        {
            _ = Pending.Remove(stream);
        }

        AudioProofLog.Count("record connects forwarded");
        AudioProofLog.Note($"pa_stream_connect_record 0x{stream:x} device={Marshal.PtrToStringUTF8(device) ?? "(default)"} forwarded to the real library");
        return ((delegate* unmanaged<nint, nint, nint, uint, int>)Real("pa_stream_connect_record"))(stream, device, attributes, flags);
    }

    [UnmanagedCallersOnly]
    private static int GetState(nint stream) =>
        Find(stream) is { } playback ? playback.State : ((delegate* unmanaged<nint, int>)Real("pa_stream_get_state"))(stream);

    [UnmanagedCallersOnly]
    private static int BeginWrite(nint stream, nint* data, nuint* bytes)
    {
        if (Find(stream) is not { } playback)
        {
            return ((delegate* unmanaged<nint, nint*, nuint*, int>)Real("pa_stream_begin_write"))(stream, data, bytes);
        }

        int size = *bytes == nuint.MaxValue ? playback.RequestBytes : (int)*bytes;
        *data = playback.WriteBuffer(size);
        *bytes = (nuint)size;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int Write(nint stream, nint data, nuint bytes, nint free, long offset, int seek)
    {
        if (Find(stream) is not { } playback)
        {
            return ((delegate* unmanaged<nint, nint, nuint, nint, long, int, int>)Real("pa_stream_write"))(stream, data, bytes, free, offset, seek);
        }

        try
        {
            if (offset != 0 || seek != 0)
            {
                AudioProofLog.Count("writes with a seek");
            }

            playback.Write(data, (int)bytes);
            if (free != 0)
            {
                ((delegate* unmanaged<nint, void>)free)(data);
            }

            return 0;
        }
        catch (Exception error)
        {
            Fail("pa_stream_write", error);
            return -1;
        }
    }

    [UnmanagedCallersOnly]
    private static nint Cork(nint stream, int corked, nint callback, nint userdata)
    {
        if (Find(stream) is not { } playback)
        {
            return ((delegate* unmanaged<nint, int, nint, nint, nint>)Real("pa_stream_cork"))(stream, corked, callback, userdata);
        }

        playback.Corked = corked != 0;
        AudioProofLog.Note($"pa_stream_cork 0x{stream:x} {corked} (queued {playback.Ring.Pending} frames)");
        return Complete(stream, callback, userdata);
    }

    [UnmanagedCallersOnly]
    private static nint Flush(nint stream, nint callback, nint userdata)
    {
        if (Find(stream) is not { } playback)
        {
            return ((delegate* unmanaged<nint, nint, nint, nint>)Real("pa_stream_flush"))(stream, callback, userdata);
        }

        AudioProofLog.Note($"pa_stream_flush 0x{stream:x} discards {playback.Ring.Pending} queued frames");
        playback.Ring.RequestFlush();
        return Complete(stream, callback, userdata);
    }

    private static nint Complete(nint stream, nint callback, nint userdata)
    {
        // The operation is complete before it is returned; Chromium's success callback only signals its mainloop.
        if (callback != 0)
        {
            ((delegate* unmanaged<nint, int, nint, void>)callback)(stream, 1, userdata);
        }

        return NewOperation();
    }

    [UnmanagedCallersOnly]
    private static int GetLatency(nint stream, ulong* microseconds, int* negative)
    {
        if (Find(stream) is not { } playback)
        {
            return ((delegate* unmanaged<nint, ulong*, int*, int>)Real("pa_stream_get_latency"))(stream, microseconds, negative);
        }

        AudioProofLog.Count("pa_stream_get_latency (redirected)");
        *microseconds = playback.LatencyMicroseconds;
        if (negative != null)
        {
            *negative = 0;
        }

        return 0;
    }

    [UnmanagedCallersOnly]
    private static uint GetDeviceIndex(nint stream)
    {
        if (Find(stream) is null)
        {
            return ((delegate* unmanaged<nint, uint>)Real("pa_stream_get_device_index"))(stream);
        }

        AudioProofLog.Count("pa_stream_get_device_index (redirected)");
        return InvalidIndex;
    }

    [UnmanagedCallersOnly]
    private static int Disconnect(nint stream)
    {
        if (Find(stream) is not { } playback)
        {
            return ((delegate* unmanaged<nint, int>)Real("pa_stream_disconnect"))(stream);
        }

        try
        {
            StopTimer(playback);
            mixer?.Unregister(playback);
            playback.State = StreamTerminated;
            AudioProofLog.Note($"pa_stream_disconnect 0x{stream:x}: requests {playback.Requests}, bytes written {playback.BytesWritten}, frames mixed {playback.Ring.Mixed}, underruns {playback.Ring.Underruns}, overflow frames {playback.Ring.Overflows}");
            AudioProofLog.Count("redirected playback disconnects");
        }
        catch (Exception error)
        {
            Fail("pa_stream_disconnect", error);
        }

        return 0;
    }

    [UnmanagedCallersOnly]
    private static void Unref(nint stream)
    {
        PulsePlaybackStream? playback = null;
        try
        {
            lock (Gate)
            {
                _ = Playback.Remove(stream, out playback);
                _ = Pending.Remove(stream);
            }

            if (playback is not null)
            {
                StopTimer(playback);
                mixer?.Unregister(playback);
            }
        }
        finally
        {
            playback?.Dispose();
        }

        ((delegate* unmanaged<nint, void>)Real("pa_stream_unref"))(stream);
    }

    [UnmanagedCallersOnly]
    private static int OperationGetState(nint operation) =>
        IsOwnOperation(operation) ? OperationDone : ((delegate* unmanaged<nint, int>)Real("pa_operation_get_state"))(operation);

    [UnmanagedCallersOnly]
    private static void OperationUnref(nint operation)
    {
        bool own;
        lock (Gate)
        {
            own = Operations.Remove(operation);
        }

        if (own)
        {
            Marshal.FreeHGlobal(operation);
            return;
        }

        ((delegate* unmanaged<nint, void>)Real("pa_operation_unref"))(operation);
    }

    [UnmanagedCallersOnly]
    private static void OperationCancel(nint operation)
    {
        if (!IsOwnOperation(operation))
        {
            ((delegate* unmanaged<nint, void>)Real("pa_operation_cancel"))(operation);
        }
    }

    private static void StartTimer(PulsePlaybackStream playback)
    {
        if (playback.Api == 0)
        {
            mixer?.Fail("A browser PulseAudio stream's mainloop API is unknown, so no write request can be delivered.");
            return;
        }

        // pa_mainloop_api: userdata, io_new, io_enable, io_free, io_set_destroy, time_new (40), time_restart (48), time_free (56).
        PulseNative.TimeValue due = PulseNative.Now();
        nint timeNew = *(nint*)(playback.Api + 40);
        playback.TimeEvent = ((delegate* unmanaged<nint, PulseNative.TimeValue*, nint, nint, nint>)timeNew)(playback.Api, &due, (nint)(delegate* unmanaged<nint, nint, PulseNative.TimeValue*, nint, void>)&TimerFired, playback.Context);
    }

    private static void StopTimer(PulsePlaybackStream playback)
    {
        if (playback.TimeEvent == 0)
        {
            return;
        }

        ((delegate* unmanaged<nint, void>)(*(nint*)(playback.Api + 56)))(playback.TimeEvent);
        playback.TimeEvent = 0;
    }

    [UnmanagedCallersOnly]
    private static void TimerFired(nint api, nint timeEvent, PulseNative.TimeValue* time, nint userdata)
    {
        try
        {
            if (GCHandle.FromIntPtr(userdata).Target is not PulsePlaybackStream playback || playback.TimeEvent != timeEvent)
            {
                return;
            }

            playback.ServiceRequests();
            PulseNative.TimeValue next = PulseNative.Now(2500);
            ((delegate* unmanaged<nint, PulseNative.TimeValue*, void>)(*(nint*)(api + 48)))(timeEvent, &next);
        }
        catch (Exception error)
        {
            Fail("write request", error);
        }
    }

    /// <summary>A stream created by Chromium whose direction is not known yet.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct PendingStream(nint Context, int Format, int Rate, int Channels)
    {
        /// <summary>Gets the write callback set before the stream connected.</summary>
        internal PulsePlaybackStream.Callback WriteCallback { get; init; }

        /// <summary>Gets the state callback set before the stream connected.</summary>
        internal PulsePlaybackStream.Callback StateCallback { get; init; }
    }
}
