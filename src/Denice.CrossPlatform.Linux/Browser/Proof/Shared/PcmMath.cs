// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.CrossPlatform.Browser;

/// <summary>
/// The pure sample and buffer arithmetic of the proofs' redirected PCM streams: interleaved 16-bit or float frames to the
/// mixer's planar stereo, the 16-bit quantization Chromium applies on its ALSA path, and the free-space and delay figures
/// a redirected ALSA handle reports from its queue.
/// </summary>
internal static class PcmMath
{
    /// <summary>The scale DENICE divides a 16-bit sample by.</summary>
    internal const float S16Scale = 32768f;

    /// <summary>
    /// Converts one float to 16-bit as Chromium's SignedInt16 sample traits do (media/base/audio_sample_types.h, read from
    /// source and to be confirmed by the ALSA pass): positive values scale by 32767, negative by 32768, rounded to nearest
    /// with ties to even, and clamped.
    /// </summary>
    /// <param name="value">The float sample.</param>
    /// <returns>The 16-bit sample.</returns>
    internal static short ChromiumFloatToS16(float value)
    {
        float scaled = value < 0 ? value * 32768f : value * 32767f;
        return (short)Math.Clamp(MathF.Round(scaled, MidpointRounding.ToEven), short.MinValue, short.MaxValue);
    }

    /// <summary>Converts one 16-bit sample to the mixer's float.</summary>
    /// <param name="value">The 16-bit sample.</param>
    /// <returns>The float.</returns>
    internal static float S16ToFloat(short value) => value / S16Scale;

    /// <summary>Folds interleaved 16-bit frames to planar stereo: mono is copied to both sides, more channels keep the first two.</summary>
    /// <param name="interleaved">The interleaved samples.</param>
    /// <param name="channels">The channels per frame.</param>
    /// <param name="left">Receives the left channel; its length is the frame count.</param>
    /// <param name="right">Receives the right channel.</param>
    internal static void S16ToStereo(ReadOnlySpan<short> interleaved, int channels, Span<float> left, Span<float> right)
    {
        int count = Math.Max(1, channels);
        int second = Math.Min(1, count - 1);
        for (int frame = 0; frame < left.Length; frame++)
        {
            left[frame] = S16ToFloat(interleaved[frame * count]);
            right[frame] = S16ToFloat(interleaved[(frame * count) + second]);
        }
    }

    /// <summary>Folds interleaved float frames to planar stereo, as <see cref="S16ToStereo"/> does.</summary>
    /// <param name="interleaved">The interleaved samples.</param>
    /// <param name="channels">The channels per frame.</param>
    /// <param name="left">Receives the left channel; its length is the frame count.</param>
    /// <param name="right">Receives the right channel.</param>
    internal static void FloatToStereo(ReadOnlySpan<float> interleaved, int channels, Span<float> left, Span<float> right)
    {
        int count = Math.Max(1, channels);
        int second = Math.Min(1, count - 1);
        for (int frame = 0; frame < left.Length; frame++)
        {
            left[frame] = interleaved[frame * count];
            right[frame] = interleaved[(frame * count) + second];
        }
    }

    /// <summary>The frames a redirected handle can accept now (snd_pcm_avail_update): its buffer less what is queued.</summary>
    /// <param name="bufferFrames">The buffer size the handle reports.</param>
    /// <param name="pendingFrames">The frames queued and not yet mixed.</param>
    /// <returns>The free frames, never negative.</returns>
    internal static long Available(long bufferFrames, long pendingFrames) => Math.Max(0, bufferFrames - pendingFrames);

    /// <summary>The frames between the next write and the output (snd_pcm_delay): what is queued plus one mixer period.</summary>
    /// <param name="pendingFrames">The frames queued and not yet mixed.</param>
    /// <param name="periodFrames">The mixer period.</param>
    /// <returns>The delay in frames.</returns>
    internal static long Delay(long pendingFrames, long periodFrames) => pendingFrames + periodFrames;

    /// <summary>The buffer a handle opened with snd_pcm_set_params reports: its latency at its rate, at least two periods.</summary>
    /// <param name="latencyMicroseconds">The requested latency.</param>
    /// <param name="rate">The rate.</param>
    /// <param name="periodFrames">The mixer period.</param>
    /// <returns>The buffer in frames.</returns>
    internal static long BufferFrames(uint latencyMicroseconds, uint rate, long periodFrames) =>
        Math.Max(2 * periodFrames, (long)latencyMicroseconds * rate / 1_000_000);
}
