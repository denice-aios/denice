// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Text;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Linux.Browser;

/// <summary>
/// The ALSA stage (windows-linux-audio-proof.md: "The same GOT patch redirects those slots for playback handles into an
/// ALSA IRedirectedSource"). libcef.so's snd_pcm_* JUMP_SLOTs are replaced: snd_pcm_open for playback returns a DENICE
/// <see cref="AlsaPlaybackStream"/> token that never reaches libasound, and the measured sequence (set_params, drop,
/// prepare, avail_update, writei, state, delay, recover, drain, close) is answered from its queue. Capture handles and every
/// call on a handle DENICE did not open go to the real libasound; device listing is not patched. Every call is also
/// recorded per handle, run-length encoded.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: ALSA replacements exchange unmanaged PCM handles and buffers.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross into Chromium; the first one is recorded in the mixer.")]
internal static unsafe class AlsaRedirection
{
    private const int Invalid = -22;
    private const int Again = -11;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, nint> Originals = new(StringComparer.Ordinal);
    private static readonly Dictionary<nint, AlsaPlaybackStream> Streams = [];
    private static readonly Dictionary<nint, StringBuilder> Sequences = [];
    private static readonly Dictionary<nint, string> LastCall = [];
    private static readonly Dictionary<nint, int> Repeats = [];
    private static readonly byte[] DefaultName = "default\0"u8.ToArray();
    private static MixerCore? mixer;
    private static GCHandle defaultName;

    /// <summary>Gets each handle's call sequence; DENICE handles are marked "denice".</summary>
    internal static string Report
    {
        get
        {
            lock (Gate)
            {
                var text = new StringBuilder();
                foreach (KeyValuePair<nint, StringBuilder> pcm in Sequences)
                {
                    text.Append(Streams.ContainsKey(pcm.Key) ? "denice pcm 0x" : "pcm 0x").Append(pcm.Key.ToString("x", System.Globalization.CultureInfo.InvariantCulture)).Append(": ").Append(pcm.Value).Append(RunLength(pcm.Key)).Append('\n');
                }

                return text.ToString();
            }
        }
    }

    /// <summary>Gets the DENICE playback handles open now.</summary>
    internal static int OpenStreams
    {
        get
        {
            lock (Gate)
            {
                return Streams.Count;
            }
        }
    }

    /// <summary>Replaces libcef.so's snd_pcm_* slots that are present.</summary>
    /// <param name="cef">libcef.so's slots.</param>
    /// <param name="target">The mixer that pulls every redirected playback handle.</param>
    internal static void Install(ElfJumpSlots cef, MixerCore target)
    {
        ArgumentNullException.ThrowIfNull(cef);
        mixer = target;
        defaultName = GCHandle.Alloc(DefaultName, GCHandleType.Pinned);
        foreach (KeyValuePair<string, nint> replacement in Replacements().Where(replacement => cef.Slot(replacement.Key) != 0))
        {
            Originals[replacement.Key] = cef.Patch(replacement.Key, replacement.Value);
        }
    }

    private static Dictionary<string, nint> Replacements() => new(StringComparer.Ordinal)
    {
        ["snd_pcm_open"] = (nint)(delegate* unmanaged<nint*, byte*, int, int, int>)&Open,
        ["snd_pcm_set_params"] = (nint)(delegate* unmanaged<nint, int, int, uint, uint, int, uint, int>)&SetParams,
        ["snd_pcm_prepare"] = (nint)(delegate* unmanaged<nint, int>)&Prepare,
        ["snd_pcm_drop"] = (nint)(delegate* unmanaged<nint, int>)&Drop,
        ["snd_pcm_drain"] = (nint)(delegate* unmanaged<nint, int>)&Drain,
        ["snd_pcm_start"] = (nint)(delegate* unmanaged<nint, int>)&Start,
        ["snd_pcm_resume"] = (nint)(delegate* unmanaged<nint, int>)&Resume,
        ["snd_pcm_close"] = (nint)(delegate* unmanaged<nint, int>)&CloseHandle,
        ["snd_pcm_state"] = (nint)(delegate* unmanaged<nint, int>)&State,
        ["snd_pcm_avail_update"] = (nint)(delegate* unmanaged<nint, long>)&AvailUpdate,
        ["snd_pcm_writei"] = (nint)(delegate* unmanaged<nint, nint, ulong, long>)&WriteInterleaved,
        ["snd_pcm_readi"] = (nint)(delegate* unmanaged<nint, nint, ulong, long>)&ReadInterleaved,
        ["snd_pcm_delay"] = (nint)(delegate* unmanaged<nint, long*, int>)&Delay,
        ["snd_pcm_recover"] = (nint)(delegate* unmanaged<nint, int, int, int>)&Recover,
        ["snd_pcm_name"] = (nint)(delegate* unmanaged<nint, byte*>)&Name,
        ["snd_pcm_get_params"] = (nint)(delegate* unmanaged<nint, ulong*, ulong*, int>)&GetParams,
        ["snd_pcm_hw_params_any"] = (nint)(delegate* unmanaged<nint, nint, int>)&HwParamsAny,
        ["snd_pcm_hw_params"] = (nint)(delegate* unmanaged<nint, nint, int>)&HwParams,
        ["snd_pcm_sw_params_current"] = (nint)(delegate* unmanaged<nint, nint, int>)&SwParamsCurrent,
        ["snd_pcm_sw_params"] = (nint)(delegate* unmanaged<nint, nint, int>)&SwParams,
        ["snd_pcm_hw_params_set_access"] = (nint)(delegate* unmanaged<nint, nint, nint, int>)&SetAccess,
        ["snd_pcm_hw_params_set_format"] = (nint)(delegate* unmanaged<nint, nint, nint, int>)&SetFormat,
        ["snd_pcm_hw_params_set_channels"] = (nint)(delegate* unmanaged<nint, nint, nint, int>)&SetChannels,
        ["snd_pcm_hw_params_set_rate_resample"] = (nint)(delegate* unmanaged<nint, nint, nint, int>)&SetRateResample,
        ["snd_pcm_hw_params_test_format"] = (nint)(delegate* unmanaged<nint, nint, nint, int>)&TestFormat,
        ["snd_pcm_hw_params_set_buffer_size_near"] = (nint)(delegate* unmanaged<nint, nint, nint, int>)&SetBufferSizeNear,
        ["snd_pcm_sw_params_set_avail_min"] = (nint)(delegate* unmanaged<nint, nint, nint, int>)&SetAvailMin,
        ["snd_pcm_sw_params_set_start_threshold"] = (nint)(delegate* unmanaged<nint, nint, nint, int>)&SetStartThreshold,
        ["snd_pcm_hw_params_set_rate_near"] = (nint)(delegate* unmanaged<nint, nint, nint, nint, int>)&SetRateNear,
        ["snd_pcm_hw_params_set_period_size_near"] = (nint)(delegate* unmanaged<nint, nint, nint, nint, int>)&SetPeriodSizeNear,
    };

    private static nint Original(string name) => Originals[name];

    private static AlsaPlaybackStream? Find(nint pcm)
    {
        lock (Gate)
        {
            return Streams.GetValueOrDefault(pcm);
        }
    }

    private static string RunLength(nint pcm) => Repeats.TryGetValue(pcm, out int count) && count > 1 ? $"x{count}" : string.Empty;

    private static void Note(nint pcm, string call, string? detail = null)
    {
        AudioProofLog.Count("alsa " + call);
        lock (Gate)
        {
            if (!Sequences.TryGetValue(pcm, out StringBuilder? sequence))
            {
                sequence = new StringBuilder();
                Sequences[pcm] = sequence;
            }

            if (detail is null && string.Equals(LastCall.GetValueOrDefault(pcm), call, StringComparison.Ordinal))
            {
                Repeats[pcm] = Repeats.GetValueOrDefault(pcm) + 1;
                return;
            }

            if (sequence.Length < 6000)
            {
                sequence.Append(RunLength(pcm)).Append(sequence.Length > 0 ? " " : string.Empty).Append(call);
                if (detail is not null)
                {
                    sequence.Append('(').Append(detail).Append(')');
                }
            }

            LastCall[pcm] = detail is null ? call : string.Empty;
            Repeats[pcm] = 1;
        }
    }

    private static int Refuse(nint pcm, string call)
    {
        // The hw/sw parameter path was not in Chromium's measured sequence; on a DENICE handle it is refused, not emulated.
        Note(pcm, call + " refused");
        mixer?.Fail($"Chromium called {call} on a redirected ALSA handle; the proof answers only the measured snd_pcm_set_params sequence.");
        return Invalid;
    }

    private static int Control(nint pcm, string call, Func<AlsaPlaybackStream, int> own)
    {
        Note(pcm, call);
        return Find(pcm) is { } stream ? own(stream) : ((delegate* unmanaged<nint, int>)Original("snd_pcm_" + call))(pcm);
    }

    private static int Guarded2(string name, nint pcm, nint first) =>
        Find(pcm) is null ? ((delegate* unmanaged<nint, nint, int>)Original(name))(pcm, first) : Refuse(pcm, name);

    private static int Guarded3(string name, nint pcm, nint first, nint second) =>
        Find(pcm) is null ? ((delegate* unmanaged<nint, nint, nint, int>)Original(name))(pcm, first, second) : Refuse(pcm, name);

    private static int Guarded4(string name, nint pcm, nint first, nint second, nint third) =>
        Find(pcm) is null ? ((delegate* unmanaged<nint, nint, nint, nint, int>)Original(name))(pcm, first, second, third) : Refuse(pcm, name);

    [UnmanagedCallersOnly]
    private static int Open(nint* pcm, byte* name, int stream, int mode)
    {
        string device = Marshal.PtrToStringUTF8((nint)name) ?? "(null)";
        if (stream != 0)
        {
            int real = ((delegate* unmanaged<nint*, byte*, int, int, int>)Original("snd_pcm_open"))(pcm, name, stream, mode);
            AudioProofLog.Note($"snd_pcm_open {device} capture mode={mode} -> {real} (real libasound)");
            if (real == 0)
            {
                Note(*pcm, "open", device + ",capture");
            }

            return real;
        }

        var playback = new AlsaPlaybackStream(device);
        lock (Gate)
        {
            Streams[playback.Handle] = playback;
        }

        *pcm = playback.Handle;
        Note(playback.Handle, "open", $"{device},playback,mode={mode}");
        AudioProofLog.Count("redirected ALSA playback opens");
        AudioProofLog.Note($"snd_pcm_open {device} playback mode={mode} -> DENICE handle 0x{playback.Handle:x}");
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int SetParams(nint pcm, int format, int access, uint channels, uint rate, int resample, uint latency)
    {
        Note(pcm, "set_params", $"format={format},access={access},channels={channels},rate={rate},resample={resample},latency={latency}us");
        if (Find(pcm) is not { } stream)
        {
            return ((delegate* unmanaged<nint, int, int, uint, uint, int, uint, int>)Original("snd_pcm_set_params"))(pcm, format, access, channels, rate, resample, latency);
        }

        if (!stream.Configure(format, access, channels, rate, latency))
        {
            mixer?.Fail($"A redirected ALSA stream asked for format {format}, access {access}, {channels} channels at {rate} Hz, which the proof does not convert.");
            return Invalid;
        }

        mixer?.Register(stream);
        AudioProofLog.Note($"redirected ALSA stream 0x{pcm:x}: buffer {stream.BufferFrames} frames");
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int Prepare(nint pcm) => Control(pcm, "prepare", stream =>
    {
        stream.State = AlsaPlaybackStream.Prepared;
        return 0;
    });

    [UnmanagedCallersOnly]
    private static int Drop(nint pcm) => Control(pcm, "drop", stream =>
    {
        stream.Ring.RequestFlush();
        stream.State = AlsaPlaybackStream.Setup;
        return 0;
    });

    [UnmanagedCallersOnly]
    private static int Drain(nint pcm) => Control(pcm, "drain", stream =>
    {
        // Non-blocking: what is queued still plays, as the mixer keeps pulling a running handle.
        return 0;
    });

    [UnmanagedCallersOnly]
    private static int Start(nint pcm) => Control(pcm, "start", stream =>
    {
        stream.State = AlsaPlaybackStream.Running;
        return 0;
    });

    [UnmanagedCallersOnly]
    private static int Resume(nint pcm) => Control(pcm, "resume", stream => 0);

    [UnmanagedCallersOnly]
    private static int State(nint pcm) => Control(pcm, "state", stream => stream.State);

    [UnmanagedCallersOnly]
    private static int CloseHandle(nint pcm)
    {
        Note(pcm, "close", string.Empty);
        AlsaPlaybackStream? stream = null;
        try
        {
            lock (Gate)
            {
                _ = Streams.Remove(pcm, out stream);
            }

            if (stream is null)
            {
                return ((delegate* unmanaged<nint, int>)Original("snd_pcm_close"))(pcm);
            }

            mixer?.Unregister(stream);
            AudioProofLog.Note($"closed redirected ALSA stream 0x{pcm:x}: frames written {stream.FramesWritten}, mixed {stream.Ring.Mixed}, underruns {stream.Ring.Underruns}");
            return 0;
        }
        catch (Exception error)
        {
            mixer?.Fail("Closing a redirected ALSA stream failed: " + error.Message);
            return 0;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    [UnmanagedCallersOnly]
    private static long AvailUpdate(nint pcm)
    {
        Note(pcm, "avail_update");
        return Find(pcm) is { } stream ? stream.Available : ((delegate* unmanaged<nint, long>)Original("snd_pcm_avail_update"))(pcm);
    }

    [UnmanagedCallersOnly]
    private static long WriteInterleaved(nint pcm, nint buffer, ulong frames)
    {
        Note(pcm, "writei");
        if (Find(pcm) is not { } stream)
        {
            return ((delegate* unmanaged<nint, nint, ulong, long>)Original("snd_pcm_writei"))(pcm, buffer, frames);
        }

        try
        {
            long taken = stream.Write(buffer, (long)frames);
            AudioProofLog.Count("alsa frames redirected", taken);
            return taken > 0 ? taken : Again;
        }
        catch (Exception error)
        {
            mixer?.Fail("A redirected ALSA write failed: " + error.Message);
            return Invalid;
        }
    }

    [UnmanagedCallersOnly]
    private static long ReadInterleaved(nint pcm, nint buffer, ulong frames) =>
        Find(pcm) is null ? ((delegate* unmanaged<nint, nint, ulong, long>)Original("snd_pcm_readi"))(pcm, buffer, frames) : Refuse(pcm, "snd_pcm_readi");

    [UnmanagedCallersOnly]
    private static int Delay(nint pcm, long* delay)
    {
        Note(pcm, "delay");
        if (Find(pcm) is not { } stream)
        {
            return ((delegate* unmanaged<nint, long*, int>)Original("snd_pcm_delay"))(pcm, delay);
        }

        *delay = stream.Delay;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int Recover(nint pcm, int error, int silent)
    {
        Note(pcm, "recover", error.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Find(pcm) is not { } stream)
        {
            return ((delegate* unmanaged<nint, int, int, int>)Original("snd_pcm_recover"))(pcm, error, silent);
        }

        stream.State = AlsaPlaybackStream.Prepared;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static byte* Name(nint pcm) =>
        Find(pcm) is null ? ((delegate* unmanaged<nint, byte*>)Original("snd_pcm_name"))(pcm) : (byte*)defaultName.AddrOfPinnedObject();

    [UnmanagedCallersOnly]
    private static int GetParams(nint pcm, ulong* buffer, ulong* period)
    {
        if (Find(pcm) is not { } stream)
        {
            return ((delegate* unmanaged<nint, ulong*, ulong*, int>)Original("snd_pcm_get_params"))(pcm, buffer, period);
        }

        *buffer = (ulong)stream.BufferFrames;
        *period = AlsaPlaybackStream.PeriodFrames;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int HwParamsAny(nint pcm, nint parameters) => Guarded2("snd_pcm_hw_params_any", pcm, parameters);

    [UnmanagedCallersOnly]
    private static int HwParams(nint pcm, nint parameters) => Guarded2("snd_pcm_hw_params", pcm, parameters);

    [UnmanagedCallersOnly]
    private static int SwParamsCurrent(nint pcm, nint parameters) => Guarded2("snd_pcm_sw_params_current", pcm, parameters);

    [UnmanagedCallersOnly]
    private static int SwParams(nint pcm, nint parameters) => Guarded2("snd_pcm_sw_params", pcm, parameters);

    [UnmanagedCallersOnly]
    private static int SetAccess(nint pcm, nint parameters, nint value) => Guarded3("snd_pcm_hw_params_set_access", pcm, parameters, value);

    [UnmanagedCallersOnly]
    private static int SetFormat(nint pcm, nint parameters, nint value) => Guarded3("snd_pcm_hw_params_set_format", pcm, parameters, value);

    [UnmanagedCallersOnly]
    private static int SetChannels(nint pcm, nint parameters, nint value) => Guarded3("snd_pcm_hw_params_set_channels", pcm, parameters, value);

    [UnmanagedCallersOnly]
    private static int SetRateResample(nint pcm, nint parameters, nint value) => Guarded3("snd_pcm_hw_params_set_rate_resample", pcm, parameters, value);

    [UnmanagedCallersOnly]
    private static int TestFormat(nint pcm, nint parameters, nint value) => Guarded3("snd_pcm_hw_params_test_format", pcm, parameters, value);

    [UnmanagedCallersOnly]
    private static int SetBufferSizeNear(nint pcm, nint parameters, nint value) => Guarded3("snd_pcm_hw_params_set_buffer_size_near", pcm, parameters, value);

    [UnmanagedCallersOnly]
    private static int SetAvailMin(nint pcm, nint parameters, nint value) => Guarded3("snd_pcm_sw_params_set_avail_min", pcm, parameters, value);

    [UnmanagedCallersOnly]
    private static int SetStartThreshold(nint pcm, nint parameters, nint value) => Guarded3("snd_pcm_sw_params_set_start_threshold", pcm, parameters, value);

    [UnmanagedCallersOnly]
    private static int SetRateNear(nint pcm, nint parameters, nint value, nint direction) => Guarded4("snd_pcm_hw_params_set_rate_near", pcm, parameters, value, direction);

    [UnmanagedCallersOnly]
    private static int SetPeriodSizeNear(nint pcm, nint parameters, nint value, nint direction) => Guarded4("snd_pcm_hw_params_set_period_size_near", pcm, parameters, value, direction);
}
