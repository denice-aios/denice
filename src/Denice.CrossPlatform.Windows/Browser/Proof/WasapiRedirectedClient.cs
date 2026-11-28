// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>
/// The DENICE IAudioClient, IAudioRenderClient and IAudioClock a wrapped render endpoint activates for Chromium. It
/// reports a 48 kHz float stereo mix format, accepts shared event-driven Initialize, signals Chromium's event once per
/// mixer cycle, reports frames not yet mixed as padding and queues what Chromium releases for the mixer. Its clock
/// reports frames mixed against the QPC time of the latest cycle.
/// </summary>
[GeneratedComClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: WASAPI buffers and formats are unmanaged.")]
internal sealed unsafe partial class WasapiRedirectedClient : IAudioClient, IAudioRenderClient, IAudioClock, IRedirectedSource, IDisposable
{
    private const uint EventCallback = 0x00040000;
    private const uint SilentBuffer = 0x2;
    private const int UnsupportedFormat = unchecked((int)0x88890008);
    private const int NotInitialized = unchecked((int)0x88890001);
    private const int MinimumBufferFrames = MixerCore.SampleRate / 25;
    private readonly RedirectedRing _ring = new(MixerCore.SampleRate * 2);
    private readonly TimeProvider _clock;
    private readonly string _device;
    private nint _buffer;
    private uint _bufferFrames;
    private int _channels;
    private int _bits;
    private bool _floating;
    private BorrowedEvent? _event;
    private volatile bool _started;
    private long _released;
    private long _releasedSounding;
    private long _counter;

    /// <summary>Initializes a new instance of the <see cref="WasapiRedirectedClient"/> class.</summary>
    /// <param name="clock">The injected clock (QPC timestamps on Windows).</param>
    /// <param name="device">The endpoint id Chromium asked for.</param>
    internal WasapiRedirectedClient(TimeProvider clock, string device)
    {
        _clock = clock;
        _device = device;
    }

    /// <summary>Gets or sets the mixer the client registers with when Chromium initializes it.</summary>
    internal static MixerCore? Mixer { get; set; }

    /// <inheritdoc />
    public int Initialize(int shareMode, uint flags, long bufferDuration, long periodicity, nint format, Guid* session)
    {
        AudioProofLog.Count("IAudioClient.Initialize");
        if (shareMode != 0 || !ReadFormat(format))
        {
            AudioProofLog.Note($"IAudioClient.Initialize refused: share mode {shareMode}, {DescribeFormat(format)}");
            return UnsupportedFormat;
        }

        // At least 40 ms: Chromium refills in whole 10 ms packets only when one fits, and a mixer clock that wakes coarsely
        // (about 15.6 ms on a default Windows timer) renders two or three 5 ms cycles at once. A 20 ms buffer could not hold
        // that burst plus the unfillable part of a packet and underran (run 46103627848); 40 ms absorbs it.
        _bufferFrames = (uint)Math.Clamp(bufferDuration * MixerCore.SampleRate / 10_000_000, MinimumBufferFrames, MixerCore.SampleRate);
        _ring.PrebufferFrames = MinimumBufferFrames / 2;
        _buffer = Marshal.AllocHGlobal((int)_bufferFrames * _channels * (_bits / 8));
        AudioProofLog.Note($"IAudioClient.Initialize on {_device}: flags 0x{flags:x} (event-driven={(flags & EventCallback) != 0}), buffer {bufferDuration} -> {_bufferFrames} frames, period {periodicity}, {DescribeFormat(format)}");
        Mixer?.Register(this);
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int GetBufferSize(uint* frames)
    {
        *frames = _bufferFrames;
        return _bufferFrames == 0 ? NotInitialized : ComNative.Ok;
    }

    /// <inheritdoc />
    public int GetStreamLatency(long* latency)
    {
        *latency = 100_000;
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int GetCurrentPadding(uint* frames)
    {
        *frames = (uint)Math.Min(_ring.Pending, _bufferFrames);
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int IsFormatSupported(int shareMode, nint format, nint* closest)
    {
        if (closest != null)
        {
            *closest = 0;
        }

        bool supported = shareMode == 0 && IsMixable(format);
        AudioProofLog.Note($"IAudioClient.IsFormatSupported {DescribeFormat(format)} -> {supported}");
        return supported ? ComNative.Ok : UnsupportedFormat;
    }

    /// <inheritdoc />
    public int GetMixFormat(nint* format)
    {
        *format = ComNative.TaskFormat(MixerCore.SampleRate, 2, 32, true);
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int GetDevicePeriod(long* defaultPeriod, long* minimumPeriod)
    {
        if (defaultPeriod != null)
        {
            *defaultPeriod = 100_000;
        }

        if (minimumPeriod != null)
        {
            *minimumPeriod = 30_000;
        }

        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int Start()
    {
        _started = true;
        AudioProofLog.Note("IAudioClient.Start on " + _device);
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int Stop()
    {
        _started = false;
        AudioProofLog.Note($"IAudioClient.Stop on {_device} at {HostMilliseconds():F1} ms: {Accounting()}, underruns {_ring.Underruns}");
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int Reset()
    {
        long discarded = _ring.Pending;
        long discardedSounding = _ring.PendingSounding;
        _ring.RequestFlush();
        AudioProofLog.Note($"IAudioClient.Reset on {_device} at {HostMilliseconds():F1} ms: discarded {discarded} queued frames ({discardedSounding} sounding); {Accounting()}");
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int SetEventHandle(nint handle)
    {
        _event?.Dispose();
        _event = BorrowedEvent.Create(handle);
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int GetService(Guid* iid, nint* service)
    {
        *service = 0;
        Guid asked = *iid;
        AudioProofLog.Count("IAudioClient.GetService " + asked);
        if (asked == typeof(IAudioRenderClient).GUID || asked == typeof(IAudioClock).GUID)
        {
            *service = ComNative.Expose(this, asked);
            return ComNative.Ok;
        }

        AudioProofLog.Note("IAudioClient.GetService E_NOINTERFACE for " + asked);
        return ComNative.NoInterface;
    }

    /// <inheritdoc />
    public int GetBuffer(uint frames, nint* data)
    {
        *data = _buffer;
        return frames <= _bufferFrames ? ComNative.Ok : unchecked((int)0x88890006);
    }

    /// <inheritdoc />
    public int ReleaseBuffer(uint frames, uint flags)
    {
        int count = (int)frames;
        float[] left = new float[count];
        float[] right = new float[count];
        if ((flags & SilentBuffer) == 0)
        {
            for (int frame = 0; frame < count; frame++)
            {
                left[frame] = Sample(frame * _channels);
                right[frame] = Sample((frame * _channels) + Math.Min(1, _channels - 1));
            }
        }
        else
        {
            AudioProofLog.Count("ReleaseBuffer AUDCLNT_BUFFERFLAGS_SILENT");
        }

        long sounding = 0;
        for (int frame = 0; frame < count; frame++)
        {
            sounding += left[frame] != 0 || right[frame] != 0 ? 1 : 0;
        }

        Interlocked.Add(ref _released, count);
        Interlocked.Add(ref _releasedSounding, sounding);
        _ = _ring.Write(left, right);
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int GetFrequency(ulong* frequency)
    {
        *frequency = MixerCore.SampleRate;
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int GetPosition(ulong* position, ulong* counter)
    {
        *position = (ulong)_ring.Mixed;
        if (counter != null)
        {
            *counter = (ulong)Interlocked.Read(ref _counter);
        }

        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int GetCharacteristics(uint* characteristics)
    {
        *characteristics = 0;
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public void MixInto(Span<float> left, Span<float> right, long hostNanoseconds, long cycle)
    {
        if (!_started)
        {
            return;
        }

        _ = _ring.MixInto(left, right);
        Interlocked.Exchange(ref _counter, (long)ComNative.Counter100ns(_clock));
        _event?.Set();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Mixer?.Unregister(this);
        _event?.Dispose();
        _event = null;
        if (_buffer != 0)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = 0;
        }
    }

    private static bool IsMixable(nint format)
    {
        WaveFormat wave = WaveFormat.Read(format);
        return wave.Rate == MixerCore.SampleRate && wave.Channels is >= 1 and <= 8 && ((wave.Floating && wave.Bits == 32) || (!wave.Floating && wave.Bits == 16));
    }

    private static string DescribeFormat(nint format)
    {
        if (format == 0)
        {
            return "no format";
        }

        WaveFormat wave = WaveFormat.Read(format);
        return $"format tag 0x{wave.Tag:x}, {wave.Rate} Hz, {wave.Channels} ch, {wave.Bits} bit, float={wave.Floating}";
    }

    private bool ReadFormat(nint format)
    {
        if (format == 0 || !IsMixable(format))
        {
            return false;
        }

        WaveFormat wave = WaveFormat.Read(format);
        _channels = wave.Channels;
        _bits = wave.Bits;
        _floating = wave.Floating;
        return true;
    }

    private float Sample(int index) => _floating ? ((float*)_buffer)[index] : ((short*)_buffer)[index] / 32768f;

    /// <summary>Released, mixed and queued frames of this stream, each with its sounding count.</summary>
    private string Accounting() =>
        $"released {Interlocked.Read(ref _released)} ({Interlocked.Read(ref _releasedSounding)} sounding), mixed {_ring.Mixed} ({_ring.MixedSounding} sounding), queued {_ring.Pending} ({_ring.PendingSounding} sounding)";

    private double HostMilliseconds() => _clock.GetTimestamp() * 1000.0 / _clock.TimestampFrequency;

    /// <summary>The fields of a WAVEFORMATEX or WAVEFORMATEXTENSIBLE the client needs.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct WaveFormat(int Tag, int Channels, int Rate, int Bits, bool Floating)
    {
        internal static WaveFormat Read(nint format)
        {
            byte* data = (byte*)format;
            int tag = *(ushort*)data;
            bool floating = tag == 3 || (tag == 0xFFFE && (*(Guid*)(data + 24)).ToString().StartsWith("00000003", StringComparison.Ordinal));
            return new WaveFormat(tag, *(ushort*)(data + 2), (int)*(uint*)(data + 4), *(ushort*)(data + 14), floating);
        }
    }
}
