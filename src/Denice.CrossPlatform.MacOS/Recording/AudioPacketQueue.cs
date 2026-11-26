// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.CrossPlatform.MacOS.Recording;

/// <summary>Bounds captured PCM and keeps packets until their encoder accepts them.</summary>
internal sealed class AudioPacketQueue
{
    private readonly Queue<AudioPacket> _packets = new();
    private readonly int _maximumFrames;
    private int _frames;

    private AudioPacketQueue(int maximumFrames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFrames);
        _maximumFrames = maximumFrames;
    }

    /// <summary>Gets the number of packets waiting for their encoder.</summary>
    internal int Count => _packets.Count;

    /// <summary>Creates a queue with a fixed sample-frame capacity.</summary>
    /// <param name="maximumFrames">The maximum queued sample frames.</param>
    /// <returns>The bounded queue.</returns>
    internal static AudioPacketQueue Create(int maximumFrames) => new(maximumFrames);

    /// <summary>Retains a copied packet or reports overflow without discarding earlier packets.</summary>
    /// <param name="packet">The owned PCM packet.</param>
    internal void Enqueue(AudioPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Frames > _maximumFrames - _frames)
        {
            throw new IOException("The audio encoder exceeded its capture queue capacity; captured audio is not discarded.");
        }

        _packets.Enqueue(packet);
        _frames += packet.Frames;
    }

    /// <summary>Removes packets only after the encoder accepts them.</summary>
    /// <param name="tryEncode">Returns true only when the packet has been accepted.</param>
    internal void Drain(Func<AudioPacket, bool> tryEncode)
    {
        ArgumentNullException.ThrowIfNull(tryEncode);
        while (_packets.TryPeek(out AudioPacket? packet) && tryEncode(packet))
        {
            _packets.Dequeue();
            _frames -= packet.Frames;
        }
    }

    /// <summary>Releases the queue after the recorder has ended.</summary>
    internal void Clear()
    {
        _packets.Clear();
        _frames = 0;
    }
}
