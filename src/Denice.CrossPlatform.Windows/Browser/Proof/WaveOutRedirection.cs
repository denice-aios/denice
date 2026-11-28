// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>
/// libcef.dll's WINMM wave-out imports, redirected: every wave-out handle Chromium opens is a DENICE source the mixer
/// pulls, so Chromium's linear wave-out fallback never reaches a device. A written header is marked done, and the
/// handle's event signalled, once the source has room for about two more buffers. Expected opens: 0.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: WAVEHDR and WAVEFORMATEX are unmanaged.")]
internal static unsafe class WaveOutRedirection
{
    private const uint Ok = 0;
    private const uint FormatQuery = 0x1;
    private const uint CallbackTypeMask = 0x70000;
    private const uint CallbackEvent = 0x50000;
    private const uint Done = 0x1;
    private const uint Prepared = 0x2;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<nint, WaveOutSource> Handles = [];
    private static ProofContext? context;

    /// <summary>Gets the replacement for each wave-out import.</summary>
    internal static IReadOnlyDictionary<string, nint> Replacements { get; } = new Dictionary<string, nint>(StringComparer.Ordinal)
    {
        ["winmm.dll!waveOutGetNumDevs"] = (nint)(delegate* unmanaged<uint>)&GetNumDevs,
        ["winmm.dll!waveOutOpen"] = (nint)(delegate* unmanaged<nint*, uint, nint, nint, nint, uint, uint>)&Open,
        ["winmm.dll!waveOutPrepareHeader"] = (nint)(delegate* unmanaged<nint, nint, uint, uint>)&PrepareHeader,
        ["winmm.dll!waveOutUnprepareHeader"] = (nint)(delegate* unmanaged<nint, nint, uint, uint>)&UnprepareHeader,
        ["winmm.dll!waveOutWrite"] = (nint)(delegate* unmanaged<nint, nint, uint, uint>)&Write,
        ["winmm.dll!waveOutReset"] = (nint)(delegate* unmanaged<nint, uint>)&Reset,
        ["winmm.dll!waveOutPause"] = (nint)(delegate* unmanaged<nint, uint>)&Pause,
        ["winmm.dll!waveOutRestart"] = (nint)(delegate* unmanaged<nint, uint>)&Restart,
        ["winmm.dll!waveOutClose"] = (nint)(delegate* unmanaged<nint, uint>)&Close,
    };

    /// <summary>Sets the mixer and clock the redirected handles use.</summary>
    /// <param name="target">The mixer.</param>
    /// <param name="time">The injected clock.</param>
    internal static void Use(MixerCore target, TimeProvider time) => context = new ProofContext(target, time);

    private static WaveOutSource? Find(nint handle)
    {
        lock (Gate)
        {
            return Handles.GetValueOrDefault(handle);
        }
    }

    [UnmanagedCallersOnly]
    private static uint GetNumDevs()
    {
        AudioProofLog.Count("waveOutGetNumDevs");
        return 1;
    }

    [UnmanagedCallersOnly]
    private static uint Open(nint* handle, uint device, nint format, nint callback, nint instance, uint flags)
    {
        int channels = *(ushort*)(format + 2);
        int bits = *(ushort*)(format + 14);
        AudioProofLog.Note($"waveOutOpen device {device} flags 0x{flags:x}: tag 0x{*(ushort*)format:x}, {*(uint*)(format + 4)} Hz, {channels} ch, {bits} bit");
        if ((flags & FormatQuery) != 0)
        {
            return Ok;
        }

        AudioProofLog.Count("waveOutOpen (redirected)");
        var source = new WaveOutSource(channels, bits, (flags & CallbackTypeMask) == CallbackEvent ? callback : 0);
        nint id = Marshal.AllocHGlobal(8);
        lock (Gate)
        {
            Handles[id] = source;
        }

        context?.Mixer.Register(source);
        *handle = id;
        return Ok;
    }

    [UnmanagedCallersOnly]
    private static uint PrepareHeader(nint handle, nint header, uint size)
    {
        *(uint*)(header + 24) |= Prepared;
        return Ok;
    }

    [UnmanagedCallersOnly]
    private static uint UnprepareHeader(nint handle, nint header, uint size)
    {
        *(uint*)(header + 24) &= ~Prepared;
        return Ok;
    }

    [UnmanagedCallersOnly]
    private static uint Write(nint handle, nint header, uint size)
    {
        if (Find(handle) is not { } source || context is null)
        {
            return 5;
        }

        source.Queue(*(nint*)header, (int)*(uint*)(header + 8), context.Clock);
        *(uint*)(header + 24) |= Done;
        source.Signal();
        return Ok;
    }

    [UnmanagedCallersOnly]
    private static uint Reset(nint handle)
    {
        Find(handle)?.Ring.RequestFlush();
        return Ok;
    }

    [UnmanagedCallersOnly]
    private static uint Pause(nint handle)
    {
        if (Find(handle) is { } source)
        {
            source.Paused = true;
        }

        return Ok;
    }

    [UnmanagedCallersOnly]
    private static uint Restart(nint handle)
    {
        if (Find(handle) is { } source)
        {
            source.Paused = false;
        }

        return Ok;
    }

    [UnmanagedCallersOnly]
    private static uint Close(nint handle)
    {
        WaveOutSource? source = null;
        try
        {
            lock (Gate)
            {
                _ = Handles.Remove(handle, out source);
            }

            if (source is not null)
            {
                context?.Mixer.Unregister(source);
                Marshal.FreeHGlobal(handle);
                AudioProofLog.Note($"waveOutClose: frames mixed {source.Ring.Mixed}");
            }
        }
        finally
        {
            source?.Dispose();
        }

        return Ok;
    }

    /// <summary>One redirected wave-out handle.</summary>
    private sealed class WaveOutSource(int channels, int bits, nint completion) : IRedirectedSource, IDisposable
    {
        private readonly BorrowedEvent? _completion = BorrowedEvent.Create(completion);

        internal RedirectedRing Ring { get; } = new(MixerCore.SampleRate * 2) { PrebufferFrames = 960 };

        internal bool Paused { get; set; }

        public void Dispose() => _completion?.Dispose();

        public void MixInto(Span<float> left, Span<float> right, long hostNanoseconds, long cycle)
        {
            if (!Paused)
            {
                _ = Ring.MixInto(left, right);
            }
        }

        internal void Queue(nint data, int bytes, TimeProvider time)
        {
            int frames = bytes / (channels * (bits / 8));
            float[] left = new float[frames];
            float[] right = new float[frames];
            for (int frame = 0; frame < frames; frame++)
            {
                left[frame] = Sample(data, frame * channels);
                right[frame] = Sample(data, (frame * channels) + Math.Min(1, channels - 1));
            }

            _ = Ring.Write(left, right);
            long started = time.GetTimestamp();
            while (Ring.Pending > frames * 2L && time.GetElapsedTime(started) < TimeSpan.FromSeconds(2))
            {
                Task.Delay(TimeSpan.FromMilliseconds(1), time).GetAwaiter().GetResult();
            }
        }

        internal void Signal() => _completion?.Set();

        private float Sample(nint data, int index) => bits == 16 ? ((short*)data)[index] / 32768f : ((float*)data)[index];
    }
}
