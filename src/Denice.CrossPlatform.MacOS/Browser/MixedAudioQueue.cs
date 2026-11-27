// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.CrossPlatform.MacOS.Browser;

/// <summary>
/// Hands mixed stereo blocks from the audio render thread to the main thread. One writer and one reader; the writer never
/// blocks or allocates, and drops a block it has no room for, counting it.
/// </summary>
internal sealed class MixedAudioQueue
{
    private readonly float[] _left;
    private readonly float[] _right;
    private readonly int[] _blockFrames;
    private readonly long[] _blockStart;
    private readonly long[] _blockHost;
    private long _framesWritten;
    private long _framesRead;
    private long _blocksWritten;
    private long _blocksRead;
    private long _dropped;

    /// <summary>Initializes a new instance of the <see cref="MixedAudioQueue"/> class.</summary>
    /// <param name="capacityFrames">Frames held per channel before the writer drops.</param>
    /// <param name="capacityBlocks">Blocks held before the writer drops.</param>
    internal MixedAudioQueue(int capacityFrames, int capacityBlocks)
    {
        _left = new float[capacityFrames];
        _right = new float[capacityFrames];
        _blockFrames = new int[capacityBlocks];
        _blockStart = new long[capacityBlocks];
        _blockHost = new long[capacityBlocks];
    }

    /// <summary>Gets the number of blocks the writer dropped because the reader fell behind.</summary>
    internal long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Copies one block in; called only by the writer.</summary>
    /// <param name="left">The left channel.</param>
    /// <param name="right">The right channel.</param>
    /// <param name="hostNanoseconds">The block's output time on the host clock.</param>
    /// <returns>Whether the block was queued.</returns>
    internal bool TryWrite(ReadOnlySpan<float> left, ReadOnlySpan<float> right, long hostNanoseconds)
    {
        int frames = left.Length;
        long blocks = Volatile.Read(ref _blocksWritten);
        long start = Volatile.Read(ref _framesWritten);
        if (blocks - Volatile.Read(ref _blocksRead) >= _blockFrames.Length || start + frames - Volatile.Read(ref _framesRead) > _left.Length)
        {
            Interlocked.Increment(ref _dropped);
            return false;
        }

        Copy(left, _left, start);
        Copy(right, _right, start);
        int slot = (int)(blocks % _blockFrames.Length);
        _blockFrames[slot] = frames;
        _blockStart[slot] = start;
        _blockHost[slot] = hostNanoseconds;
        Volatile.Write(ref _framesWritten, start + frames);
        Volatile.Write(ref _blocksWritten, blocks + 1);
        return true;
    }

    /// <summary>Copies the oldest block out; called only by the reader.</summary>
    /// <param name="left">Receives the left channel; at least as long as any block.</param>
    /// <param name="right">Receives the right channel.</param>
    /// <param name="frames">The block's frame count.</param>
    /// <param name="hostNanoseconds">The block's output time on the host clock.</param>
    /// <returns>Whether a block was read.</returns>
    internal bool TryRead(Span<float> left, Span<float> right, out int frames, out long hostNanoseconds)
    {
        long blocks = Volatile.Read(ref _blocksRead);
        if (blocks == Volatile.Read(ref _blocksWritten))
        {
            frames = 0;
            hostNanoseconds = 0;
            return false;
        }

        int slot = (int)(blocks % _blockFrames.Length);
        frames = _blockFrames[slot];
        hostNanoseconds = _blockHost[slot];
        long start = _blockStart[slot];
        CopyOut(_left, start, left[..frames]);
        CopyOut(_right, start, right[..frames]);
        Volatile.Write(ref _framesRead, start + frames);
        Volatile.Write(ref _blocksRead, blocks + 1);
        return true;
    }

    private static void Copy(ReadOnlySpan<float> source, float[] ring, long start)
    {
        int offset = (int)(start % ring.Length);
        int first = Math.Min(source.Length, ring.Length - offset);
        source[..first].CopyTo(ring.AsSpan(offset));
        source[first..].CopyTo(ring);
    }

    private static void CopyOut(float[] ring, long start, Span<float> destination)
    {
        int offset = (int)(start % ring.Length);
        int first = Math.Min(destination.Length, ring.Length - offset);
        ring.AsSpan(offset, first).CopyTo(destination);
        ring.AsSpan(0, destination.Length - first).CopyTo(destination[first..]);
    }
}
