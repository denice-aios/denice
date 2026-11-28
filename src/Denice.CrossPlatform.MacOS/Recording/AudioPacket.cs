// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.CrossPlatform.MacOS.Recording;

/// <summary>Owns copied interleaved PCM until the encoder accepts it.</summary>
internal sealed class AudioPacket
{
    private AudioPacket(float[] samples, int frames, long position)
    {
        Samples = samples;
        Frames = frames;
        Position = position;
    }

    /// <summary>Gets the interleaved stereo samples.</summary>
    internal float[] Samples { get; }

    /// <summary>Gets the sample frames per channel.</summary>
    internal int Frames { get; }

    /// <summary>Gets the first sample's recording timestamp.</summary>
    internal long Position { get; }

    /// <summary>Creates an owned PCM packet.</summary>
    /// <param name="samples">The copied stereo samples.</param>
    /// <param name="frames">The number of sample frames.</param>
    /// <param name="position">The first sample timestamp.</param>
    /// <returns>The owned packet.</returns>
    internal static AudioPacket Create(float[] samples, int frames, long position) => new(samples, frames, position);
}
