// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices.Marshalling;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>
/// The ISpStreamFormat page speech is rendered into: Chromium's real SAPI voice receives it through
/// ISpVoice::SetOutput(stream, FALSE). Its format is 48 kHz 16-bit mono, so the samples enter the mixer's speech queue
/// without conversion. Write returns only once the mixer has played all but one period of what is queued, so SAPI's
/// word and end events trail playout by at most one mixer period.
/// </summary>
[GeneratedComClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: SAPI writes unmanaged PCM.")]
internal sealed unsafe partial class SapiOutputStream : ISpStreamFormat
{
    private const int PeriodFrames = 480;
    private static readonly Guid WaveFormatEx = new("C31ADBAE-527F-4ff5-A230-F62BB61FF70C");
    private readonly MixerCore _mixer;
    private readonly TimeProvider _clock;
    private ulong _position;

    /// <summary>Initializes a new instance of the <see cref="SapiOutputStream"/> class.</summary>
    /// <param name="mixer">The mixer whose speech queue receives the samples.</param>
    /// <param name="clock">The injected clock that paces the backpressure.</param>
    internal SapiOutputStream(MixerCore mixer, TimeProvider clock)
    {
        _mixer = mixer;
        _clock = clock;
    }

    /// <inheritdoc />
    public int GetFormat(Guid* formatId, nint* format)
    {
        *formatId = WaveFormatEx;
        *format = ComNative.TaskFormat(MixerCore.SampleRate, 1, 16, false);
        AudioProofLog.Count("ISpStreamFormat.GetFormat");
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int Write(void* data, uint bytes, uint* written)
    {
        int frames = (int)bytes / 2;
        float[] samples = new float[frames];
        for (int frame = 0; frame < frames; frame++)
        {
            samples[frame] = ((short*)data)[frame] / 32768f;
        }

        long end = _mixer.SpeechFramesQueued + frames;
        _mixer.QueueSpeech(samples);
        AudioProofLog.Count("SAPI speech frames written", frames);
        long started = _clock.GetTimestamp();
        while (_mixer.SpeechFramesDone < end - PeriodFrames && _clock.GetElapsedTime(started) < TimeSpan.FromSeconds(5))
        {
            Task.Delay(TimeSpan.FromMilliseconds(1), _clock).GetAwaiter().GetResult();
        }

        _position += bytes;
        if (written != null)
        {
            *written = bytes;
        }

        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int Read(void* data, uint bytes, uint* read) => ComNative.NotImplemented;

    /// <inheritdoc />
    public int Seek(long move, uint origin, ulong* position)
    {
        if (position != null)
        {
            *position = _position;
        }

        return move == 0 ? ComNative.Ok : ComNative.NotImplemented;
    }

    /// <inheritdoc />
    public int SetSize(ulong size) => ComNative.Ok;

    /// <inheritdoc />
    public int CopyTo(nint target, ulong bytes, ulong* read, ulong* written) => ComNative.NotImplemented;

    /// <inheritdoc />
    public int Commit(uint flags) => ComNative.Ok;

    /// <inheritdoc />
    public int Revert() => ComNative.NotImplemented;

    /// <inheritdoc />
    public int LockRegion(ulong offset, ulong bytes, uint kind) => ComNative.NotImplemented;

    /// <inheritdoc />
    public int UnlockRegion(ulong offset, ulong bytes, uint kind) => ComNative.NotImplemented;

    /// <inheritdoc />
    public int Stat(void* statistics, uint flags)
    {
        // STATSTG: the size field (cbSize) is at offset 8 on 64-bit.
        new Span<byte>(statistics, 80).Clear();
        *(ulong*)((byte*)statistics + 8) = _position;
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int Clone(nint* clone)
    {
        *clone = 0;
        return ComNative.NotImplemented;
    }
}
