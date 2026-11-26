// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>Receives a synchronous capture tap of the browser's sole output channel.</summary>
public interface IPlatformBrowserAudioSink
{
    /// <summary>Receives the stream format.</summary>
    /// <param name="channels">The channel count.</param>
    /// <param name="sampleRate">Samples per second.</param>
    void OnAudioStarted(int channels, int sampleRate);

    /// <summary>Copies planar float samples before returning. Timestamp is CEF milliseconds.</summary>
    /// <param name="planarFloatPointers">Borrowed pointers to planar float channel samples.</param>
    /// <param name="frames">The frame consumer or sample count.</param>
    /// <param name="pts">CEF presentation timestamp in milliseconds.</param>
    void OnAudioPacket(nint planarFloatPointers, int frames, long pts);

    /// <summary>Finishes the stream.</summary>
    void OnAudioStopped();

    /// <summary>Reports an audio capture failure.</summary>
    /// <param name="message">The error description.</param>
    void OnAudioError(string message);
}
