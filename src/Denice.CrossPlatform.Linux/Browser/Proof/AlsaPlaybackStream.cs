// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Linux.Browser;

/// <summary>
/// One Chromium ALSA playback handle opened through libcef.so's redirected snd_pcm_open. The handle is a DENICE token that
/// never reaches libasound; Chromium's interleaved writes are converted to 48 kHz stereo float and queued for the mixer,
/// and the handle reports free space and delay from that queue, as a device of the requested buffer size would.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: Chromium's ALSA buffers are unmanaged.")]
internal sealed unsafe class AlsaPlaybackStream : IRedirectedSource, IDisposable
{
    /// <summary>SND_PCM_STATE_SETUP.</summary>
    internal const int Setup = 1;

    /// <summary>SND_PCM_STATE_PREPARED.</summary>
    internal const int Prepared = 2;

    /// <summary>SND_PCM_STATE_RUNNING.</summary>
    internal const int Running = 3;

    /// <summary>SND_PCM_FORMAT_S16_LE.</summary>
    internal const int Signed16 = 2;

    /// <summary>SND_PCM_FORMAT_FLOAT_LE.</summary>
    internal const int Float32 = 14;

    /// <summary>The mixer period the delay adds.</summary>
    internal const int PeriodFrames = 240;

    private readonly RedirectedRing _ring = new(MixerCore.SampleRate * 2);
    private LinearResampler? _leftResampler;
    private LinearResampler? _rightResampler;
    private volatile int _state = Setup;

    /// <summary>Initializes a new instance of the <see cref="AlsaPlaybackStream"/> class.</summary>
    /// <param name="device">The device name Chromium opened.</param>
    internal AlsaPlaybackStream(string device)
    {
        Device = device;
        Handle = Marshal.AllocHGlobal(16);
    }

    /// <summary>Gets the token Chromium holds as its snd_pcm_t*.</summary>
    internal nint Handle { get; }

    /// <summary>Gets the device name Chromium opened.</summary>
    internal string Device { get; }

    /// <summary>Gets the sample format (SND_PCM_FORMAT_*).</summary>
    internal int Format { get; private set; }

    /// <summary>Gets the channels per frame.</summary>
    internal int Channels { get; private set; }

    /// <summary>Gets the rate.</summary>
    internal int Rate { get; private set; }

    /// <summary>Gets the buffer size the handle reports.</summary>
    internal long BufferFrames { get; private set; }

    /// <summary>Gets or sets the ALSA state the handle reports.</summary>
    internal int State
    {
        get => _state;
        set => _state = value;
    }

    /// <summary>Gets the handle's queue.</summary>
    internal RedirectedRing Ring => _ring;

    /// <summary>Gets the frames Chromium wrote.</summary>
    internal long FramesWritten { get; private set; }

    /// <summary>Gets the frames the handle accepts now (snd_pcm_avail_update).</summary>
    internal long Available => PcmMath.Available(BufferFrames, _ring.Pending);

    /// <summary>Gets the delay the handle reports (snd_pcm_delay).</summary>
    internal long Delay => PcmMath.Delay(_ring.Pending, PeriodFrames);

    /// <inheritdoc />
    public void MixInto(Span<float> left, Span<float> right, long hostNanoseconds, long cycle)
    {
        if (_state == Running)
        {
            _ = _ring.MixInto(left, right);
        }
    }

    /// <inheritdoc />
    public void Dispose() => Marshal.FreeHGlobal(Handle);

    /// <summary>Applies snd_pcm_set_params; only interleaved 16-bit or float is accepted.</summary>
    /// <param name="format">The SND_PCM_FORMAT_*.</param>
    /// <param name="access">The SND_PCM_ACCESS_*.</param>
    /// <param name="channels">The channels.</param>
    /// <param name="rate">The rate.</param>
    /// <param name="latencyMicroseconds">The requested latency.</param>
    /// <returns>Whether the parameters are supported.</returns>
    internal bool Configure(int format, int access, uint channels, uint rate, uint latencyMicroseconds)
    {
        if (format is not (Signed16 or Float32) || access != 3 || channels is 0 or > 8 || rate == 0)
        {
            return false;
        }

        Format = format;
        Channels = (int)channels;
        Rate = (int)rate;
        BufferFrames = PcmMath.BufferFrames(latencyMicroseconds, rate, PeriodFrames);
        _ring.PrebufferFrames = (int)Math.Min(BufferFrames / 2, MixerCore.SampleRate / 10);
        _leftResampler = rate == MixerCore.SampleRate ? null : new LinearResampler(rate, MixerCore.SampleRate);
        _rightResampler = rate == MixerCore.SampleRate ? null : new LinearResampler(rate, MixerCore.SampleRate);
        State = Prepared;
        return true;
    }

    /// <summary>Chromium's thread: snd_pcm_writei; takes at most the free frames, as a non-blocking device would.</summary>
    /// <param name="buffer">The interleaved frames.</param>
    /// <param name="frames">The frames offered.</param>
    /// <returns>The frames taken.</returns>
    internal long Write(nint buffer, long frames)
    {
        int count = (int)Math.Min(frames, Available);
        if (count <= 0)
        {
            return 0;
        }

        float[] left = new float[count];
        float[] right = new float[count];
        if (Format == Signed16)
        {
            PcmMath.S16ToStereo(new ReadOnlySpan<short>((void*)buffer, count * Channels), Channels, left, right);
        }
        else
        {
            PcmMath.FloatToStereo(new ReadOnlySpan<float>((void*)buffer, count * Channels), Channels, left, right);
        }

        if (_leftResampler is not null && _rightResampler is not null)
        {
            left = _leftResampler.Process(left);
            right = _rightResampler.Process(right);
        }

        _ = _ring.Write(left, right);
        FramesWritten += count;
        State = Running;
        return count;
    }
}
