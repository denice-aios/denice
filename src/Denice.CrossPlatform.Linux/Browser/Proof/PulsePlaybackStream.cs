// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Linux.Browser;

/// <summary>
/// One Chromium PulseAudio playback stream taken over at pa_stream_connect_playback. Chromium's handle stays the real,
/// never-connected pa_stream it created, so record streams and every unredirected call keep working on the real object.
/// Chromium writes interleaved samples through the redirected pa_stream_begin_write/pa_stream_write; they are folded to
/// stereo, converted to 48 kHz if needed and queued in a <see cref="RedirectedRing"/> that the mixer reads. Write
/// requests are delivered on Chromium's own PulseAudio mainloop thread by a time event on that mainloop's API, so
/// Chromium's write callback runs under its mainloop lock, as with the real library.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: exchanges Chromium's PulseAudio buffers and callbacks.")]
internal sealed unsafe class PulsePlaybackStream : IRedirectedSource, IDisposable
{
    /// <summary>PA_SAMPLE_FLOAT32LE.</summary>
    internal const int Float32 = 5;

    /// <summary>PA_SAMPLE_S16LE.</summary>
    internal const int Signed16 = 3;

    private const int RingFrames = MixerCore.SampleRate * 2;
    private readonly RedirectedRing _ring = new(RingFrames);
    private readonly LinearResampler? _leftResampler;
    private readonly LinearResampler? _rightResampler;
    private nint _writeBuffer;
    private int _writeBufferBytes;
    private GCHandle _self;
    private volatile bool _corked;

    /// <summary>Initializes a new instance of the <see cref="PulsePlaybackStream"/> class.</summary>
    /// <param name="handle">Chromium's pa_stream handle (the real, unconnected stream).</param>
    /// <param name="format">The pa_sample_spec format.</param>
    /// <param name="rate">The sample rate.</param>
    /// <param name="channels">The channel count.</param>
    /// <param name="requestBytes">The bytes asked for per write request (Chromium's buffer).</param>
    internal PulsePlaybackStream(nint handle, int format, int rate, int channels, int requestBytes)
    {
        Handle = handle;
        Format = format;
        Rate = rate;
        Channels = Math.Max(1, channels);
        RequestBytes = requestBytes;
        if (rate != MixerCore.SampleRate)
        {
            _leftResampler = new LinearResampler(rate, MixerCore.SampleRate);
            _rightResampler = new LinearResampler(rate, MixerCore.SampleRate);
        }

        int requestFrames = requestBytes / BytesPerFrame;
        _ring.PrebufferFrames = Math.Max(256, (int)((long)requestFrames * 2 * MixerCore.SampleRate / Math.Max(1, rate)));
        _self = GCHandle.Alloc(this);
    }

    /// <summary>Gets Chromium's handle.</summary>
    internal nint Handle { get; }

    /// <summary>Gets the stream's sample format.</summary>
    internal int Format { get; }

    /// <summary>Gets the stream's rate.</summary>
    internal int Rate { get; }

    /// <summary>Gets the stream's channel count.</summary>
    internal int Channels { get; }

    /// <summary>Gets the bytes per write request.</summary>
    internal int RequestBytes { get; }

    /// <summary>Gets the bytes per interleaved frame.</summary>
    internal int BytesPerFrame => Channels * (Format == Signed16 ? 2 : 4);

    /// <summary>Gets the GCHandle that identifies this stream to its time event.</summary>
    internal nint Context => GCHandle.ToIntPtr(_self);

    /// <summary>Gets or sets the device Chromium asked for, or null for the default.</summary>
    internal string? Device { get; set; }

    /// <summary>Gets or sets the stream state reported to Chromium (pa_stream_state_t).</summary>
    internal int State { get; set; }

    /// <summary>Gets or sets Chromium's state callback.</summary>
    internal Callback StateCallback { get; set; }

    /// <summary>Gets or sets Chromium's write callback.</summary>
    internal Callback WriteCallback { get; set; }

    /// <summary>Gets or sets the mainloop API whose time event delivers write requests.</summary>
    internal nint Api { get; set; }

    /// <summary>Gets or sets the time event, or zero.</summary>
    internal nint TimeEvent { get; set; }

    /// <summary>Gets or sets a value indicating whether Chromium has corked the stream.</summary>
    internal bool Corked
    {
        get => _corked;
        set => _corked = value;
    }

    /// <summary>Gets the stream's queue.</summary>
    internal RedirectedRing Ring => _ring;

    /// <summary>Gets the frames to keep queued before another write request is made.</summary>
    internal long TargetFrames => _ring.PrebufferFrames * 2L;

    /// <summary>Gets the write requests delivered.</summary>
    internal long Requests { get; private set; }

    /// <summary>Gets the bytes Chromium wrote.</summary>
    internal long BytesWritten { get; private set; }

    /// <summary>Gets the latency to report to Chromium: queued frames plus one mixer period, in microseconds.</summary>
    internal ulong LatencyMicroseconds => (ulong)((_ring.Pending + 240) * 1_000_000 / MixerCore.SampleRate);

    /// <inheritdoc />
    public void MixInto(Span<float> left, Span<float> right, long hostNanoseconds, long cycle)
    {
        if (!_corked)
        {
            _ = _ring.MixInto(left, right);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_self.IsAllocated)
        {
            _self.Free();
        }

        if (_writeBuffer != 0)
        {
            Marshal.FreeHGlobal(_writeBuffer);
            _writeBuffer = 0;
        }
    }

    /// <summary>Chromium's thread: the buffer pa_stream_begin_write hands out.</summary>
    /// <param name="bytes">The bytes asked for.</param>
    /// <returns>The buffer.</returns>
    internal nint WriteBuffer(int bytes)
    {
        if (bytes > _writeBufferBytes)
        {
            if (_writeBuffer != 0)
            {
                Marshal.FreeHGlobal(_writeBuffer);
            }

            _writeBuffer = Marshal.AllocHGlobal(bytes);
            _writeBufferBytes = bytes;
        }

        return _writeBuffer;
    }

    /// <summary>Chromium's thread: queues interleaved samples as 48 kHz stereo.</summary>
    /// <param name="data">The samples.</param>
    /// <param name="bytes">Their size.</param>
    internal void Write(nint data, int bytes)
    {
        BytesWritten += bytes;
        int frames = bytes / BytesPerFrame;
        float[] left = new float[frames];
        float[] right = new float[frames];
        for (int frame = 0; frame < frames; frame++)
        {
            left[frame] = Sample(data, (frame * Channels) + 0);
            right[frame] = Sample(data, (frame * Channels) + Math.Min(1, Channels - 1));
        }

        if (_leftResampler is not null && _rightResampler is not null)
        {
            left = _leftResampler.Process(left);
            right = _rightResampler.Process(right);
        }

        _ = _ring.Write(left, right);
    }

    /// <summary>Chromium's mainloop thread, under its lock: asks Chromium for data until the queue holds its target.</summary>
    internal void ServiceRequests()
    {
        Callback write = WriteCallback;
        for (int round = 0; round < 8 && !_corked && write.Procedure != 0 && State == 2 && _ring.Pending < TargetFrames; round++)
        {
            Requests++;
            ((delegate* unmanaged<nint, nuint, nint, void>)write.Procedure)(Handle, (nuint)RequestBytes, write.Context);
        }
    }

    private float Sample(nint data, int index) => Format == Signed16 ? ((short*)data)[index] / 32768f : ((float*)data)[index];

    /// <summary>A Chromium callback and its userdata.</summary>
    /// <param name="Procedure">The function.</param>
    /// <param name="Context">The userdata.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Callback(nint Procedure, nint Context);
}
