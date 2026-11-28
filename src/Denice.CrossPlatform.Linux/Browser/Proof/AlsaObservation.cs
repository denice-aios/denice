// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Text;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Linux.Browser;

/// <summary>
/// The ALSA stage's measurement: libcef.so's snd_pcm_* JUMP_SLOTs for opening, configuring and writing a PCM are replaced
/// by pass-throughs that record Chromium's call sequence per PCM (run-length encoded), its parameters and the frames it
/// writes. Calls still reach the real libasound, so this stage observes Chromium's ALSA output; it does not route it.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: ALSA pass-throughs exchange unmanaged PCM handles and buffers.")]
internal static unsafe class AlsaObservation
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, nint> Originals = new(StringComparer.Ordinal);
    private static readonly Dictionary<nint, StringBuilder> Sequences = [];
    private static readonly Dictionary<nint, string> LastCall = [];
    private static readonly Dictionary<nint, int> Repeats = [];

    /// <summary>Gets each PCM's call sequence.</summary>
    internal static string Report
    {
        get
        {
            lock (Gate)
            {
                var text = new StringBuilder();
                foreach (KeyValuePair<nint, StringBuilder> pcm in Sequences)
                {
                    text.Append("pcm 0x").Append(pcm.Key.ToString("x", System.Globalization.CultureInfo.InvariantCulture)).Append(": ").Append(pcm.Value).Append(Close(pcm.Key)).Append('\n');
                }

                return text.ToString();
            }
        }
    }

    /// <summary>Replaces the observed slots.</summary>
    /// <param name="cef">libcef.so's slots.</param>
    internal static void Install(ElfJumpSlots cef)
    {
        ArgumentNullException.ThrowIfNull(cef);
        var replacements = new Dictionary<string, nint>(StringComparer.Ordinal)
        {
            ["snd_pcm_open"] = (nint)(delegate* unmanaged<nint*, byte*, int, int, int>)&Open,
            ["snd_pcm_set_params"] = (nint)(delegate* unmanaged<nint, int, int, uint, uint, int, uint, int>)&SetParams,
            ["snd_pcm_hw_params"] = (nint)(delegate* unmanaged<nint, nint, int>)&HwParams,
            ["snd_pcm_hw_params_set_rate_near"] = (nint)(delegate* unmanaged<nint, nint, uint*, int*, int>)&SetRateNear,
            ["snd_pcm_hw_params_set_format"] = (nint)(delegate* unmanaged<nint, nint, int, int>)&SetFormat,
            ["snd_pcm_hw_params_set_channels"] = (nint)(delegate* unmanaged<nint, nint, uint, int>)&SetChannels,
            ["snd_pcm_writei"] = (nint)(delegate* unmanaged<nint, nint, ulong, long>)&WriteInterleaved,
            ["snd_pcm_avail_update"] = (nint)(delegate* unmanaged<nint, long>)&AvailUpdate,
            ["snd_pcm_delay"] = (nint)(delegate* unmanaged<nint, long*, int>)&Delay,
            ["snd_pcm_state"] = (nint)(delegate* unmanaged<nint, int>)&State,
            ["snd_pcm_prepare"] = (nint)(delegate* unmanaged<nint, int>)&Prepare,
            ["snd_pcm_start"] = (nint)(delegate* unmanaged<nint, int>)&Start,
            ["snd_pcm_drop"] = (nint)(delegate* unmanaged<nint, int>)&Drop,
            ["snd_pcm_drain"] = (nint)(delegate* unmanaged<nint, int>)&Drain,
            ["snd_pcm_recover"] = (nint)(delegate* unmanaged<nint, int, int, int>)&Recover,
            ["snd_pcm_close"] = (nint)(delegate* unmanaged<nint, int>)&CloseNative,
        };
        foreach (KeyValuePair<string, nint> replacement in replacements.Where(replacement => cef.Slot(replacement.Key) != 0))
        {
            Originals[replacement.Key] = cef.Patch(replacement.Key, replacement.Value);
        }
    }

    private static nint Original(string name) => Originals[name];

    private static string Close(nint pcm) => Repeats.TryGetValue(pcm, out int count) && count > 1 ? $"x{count}" : string.Empty;

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
                sequence.Append(Close(pcm)).Append(sequence.Length > 0 ? " " : string.Empty).Append(call);
                if (detail is not null)
                {
                    sequence.Append('(').Append(detail).Append(')');
                }
            }

            LastCall[pcm] = detail is null ? call : string.Empty;
            Repeats[pcm] = 1;
        }
    }

    [UnmanagedCallersOnly]
    private static int Open(nint* pcm, byte* name, int stream, int mode)
    {
        int result = ((delegate* unmanaged<nint*, byte*, int, int, int>)Original("snd_pcm_open"))(pcm, name, stream, mode);
        string device = Marshal.PtrToStringUTF8((nint)name) ?? "(null)";
        AudioProofLog.Note($"snd_pcm_open {device} stream={(stream == 0 ? "playback" : "capture")} mode={mode} -> {result} pcm 0x{(result == 0 ? *pcm : 0):x}");
        if (result == 0)
        {
            Note(*pcm, "open", $"{device},{(stream == 0 ? "playback" : "capture")}");
        }

        return result;
    }

    [UnmanagedCallersOnly]
    private static int SetParams(nint pcm, int format, int access, uint channels, uint rate, int resample, uint latency)
    {
        Note(pcm, "set_params", $"format={format},access={access},channels={channels},rate={rate},resample={resample},latency={latency}us");
        return ((delegate* unmanaged<nint, int, int, uint, uint, int, uint, int>)Original("snd_pcm_set_params"))(pcm, format, access, channels, rate, resample, latency);
    }

    [UnmanagedCallersOnly]
    private static int HwParams(nint pcm, nint parameters)
    {
        Note(pcm, "hw_params");
        return ((delegate* unmanaged<nint, nint, int>)Original("snd_pcm_hw_params"))(pcm, parameters);
    }

    [UnmanagedCallersOnly]
    private static int SetRateNear(nint pcm, nint parameters, uint* rate, int* direction)
    {
        uint asked = *rate;
        int result = ((delegate* unmanaged<nint, nint, uint*, int*, int>)Original("snd_pcm_hw_params_set_rate_near"))(pcm, parameters, rate, direction);
        Note(pcm, "hw_set_rate_near", $"{asked}->{*rate}");
        return result;
    }

    [UnmanagedCallersOnly]
    private static int SetFormat(nint pcm, nint parameters, int format)
    {
        Note(pcm, "hw_set_format", format.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return ((delegate* unmanaged<nint, nint, int, int>)Original("snd_pcm_hw_params_set_format"))(pcm, parameters, format);
    }

    [UnmanagedCallersOnly]
    private static int SetChannels(nint pcm, nint parameters, uint channels)
    {
        Note(pcm, "hw_set_channels", channels.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return ((delegate* unmanaged<nint, nint, uint, int>)Original("snd_pcm_hw_params_set_channels"))(pcm, parameters, channels);
    }

    [UnmanagedCallersOnly]
    private static long WriteInterleaved(nint pcm, nint buffer, ulong frames)
    {
        Note(pcm, "writei");
        AudioProofLog.Count("alsa frames written", (long)frames);
        return ((delegate* unmanaged<nint, nint, ulong, long>)Original("snd_pcm_writei"))(pcm, buffer, frames);
    }

    [UnmanagedCallersOnly]
    private static long AvailUpdate(nint pcm)
    {
        Note(pcm, "avail_update");
        return ((delegate* unmanaged<nint, long>)Original("snd_pcm_avail_update"))(pcm);
    }

    [UnmanagedCallersOnly]
    private static int Delay(nint pcm, long* delay)
    {
        Note(pcm, "delay");
        return ((delegate* unmanaged<nint, long*, int>)Original("snd_pcm_delay"))(pcm, delay);
    }

    [UnmanagedCallersOnly]
    private static int State(nint pcm)
    {
        Note(pcm, "state");
        return ((delegate* unmanaged<nint, int>)Original("snd_pcm_state"))(pcm);
    }

    [UnmanagedCallersOnly]
    private static int Prepare(nint pcm)
    {
        Note(pcm, "prepare");
        return ((delegate* unmanaged<nint, int>)Original("snd_pcm_prepare"))(pcm);
    }

    [UnmanagedCallersOnly]
    private static int Start(nint pcm)
    {
        Note(pcm, "start");
        return ((delegate* unmanaged<nint, int>)Original("snd_pcm_start"))(pcm);
    }

    [UnmanagedCallersOnly]
    private static int Drop(nint pcm)
    {
        Note(pcm, "drop");
        return ((delegate* unmanaged<nint, int>)Original("snd_pcm_drop"))(pcm);
    }

    [UnmanagedCallersOnly]
    private static int Drain(nint pcm)
    {
        Note(pcm, "drain");
        return ((delegate* unmanaged<nint, int>)Original("snd_pcm_drain"))(pcm);
    }

    [UnmanagedCallersOnly]
    private static int Recover(nint pcm, int error, int silent)
    {
        Note(pcm, "recover", error.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return ((delegate* unmanaged<nint, int, int, int>)Original("snd_pcm_recover"))(pcm, error, silent);
    }

    [UnmanagedCallersOnly]
    private static int CloseNative(nint pcm)
    {
        Note(pcm, "close", string.Empty);
        return ((delegate* unmanaged<nint, int>)Original("snd_pcm_close"))(pcm);
    }
}
