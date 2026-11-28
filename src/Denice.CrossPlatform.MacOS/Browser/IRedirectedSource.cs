// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.CrossPlatform.Browser;

/// <summary>
/// One redirected browser stream: a Chromium AUHAL unit (macOS), a DENICE IAudioClient or wave-out handle (Windows), or
/// a redirected PulseAudio or ALSA stream (Linux).
/// </summary>
internal interface IRedirectedSource
{
    /// <summary>
    /// Real-time thread, called only while the mixer holds this source's claim: adds this cycle's 48 kHz stereo frames
    /// into the buffers. A source with nothing to give adds nothing.
    /// </summary>
    /// <param name="left">The left channel to add into.</param>
    /// <param name="right">The right channel to add into.</param>
    /// <param name="hostNanoseconds">The cycle's output time on the host clock.</param>
    /// <param name="cycle">The mixer cycle index.</param>
    void MixInto(Span<float> left, Span<float> right, long hostNanoseconds, long cycle);
}
