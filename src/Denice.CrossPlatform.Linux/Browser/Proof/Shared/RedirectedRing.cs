// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.CrossPlatform.Browser;

/// <summary>
/// The 48 kHz stereo frames one redirected stream has written and the mixer has not yet mixed. One writer (the
/// browser's audio thread) and one reader (the mixer's real-time thread); neither blocks or allocates. The reader starts
/// a stream only once a prebuffer has arrived and starts it again after an underrun, so a stream's samples reach the mix
/// contiguously. A flush asked for by the writer is applied by the reader.
/// </summary>
internal sealed class RedirectedRing
{
    private readonly float[] _left;
    private readonly float[] _right;
    private long _written;
    private long _read;
    private long _flushTo;
    private long _underruns;
    private long _overflows;
    private long _mixed;
    private long _mixedSounding;
    private bool _playing;

    /// <summary>Initializes a new instance of the <see cref="RedirectedRing"/> class.</summary>
    /// <param name="capacityFrames">The frames held before the writer drops.</param>
    internal RedirectedRing(int capacityFrames)
    {
        _left = new float[capacityFrames];
        _right = new float[capacityFrames];
    }

    /// <summary>Gets or sets the frames that must be waiting before the reader starts or restarts the stream.</summary>
    internal int PrebufferFrames { get; set; }

    /// <summary>Gets the frames written and not yet mixed, as the writer sees it.</summary>
    internal long Pending => Volatile.Read(ref _written) - Math.Max(Volatile.Read(ref _read), Volatile.Read(ref _flushTo));

    /// <summary>Gets every frame written.</summary>
    internal long Written => Volatile.Read(ref _written);

    /// <summary>Gets every frame mixed.</summary>
    internal long Mixed => Interlocked.Read(ref _mixed);

    /// <summary>Gets every mixed frame with a non-zero sample in either channel.</summary>
    internal long MixedSounding => Interlocked.Read(ref _mixedSounding);

    /// <summary>Gets the frames written and not yet mixed or flushed that have a non-zero sample in either channel.</summary>
    internal long PendingSounding
    {
        get
        {
            long from = Math.Max(Volatile.Read(ref _read), Volatile.Read(ref _flushTo));
            return CountSounding(from, Volatile.Read(ref _written));
        }
    }

    /// <summary>Gets the cycles in which a playing stream ran out of frames.</summary>
    internal long Underruns => Interlocked.Read(ref _underruns);

    /// <summary>Gets the frames the writer dropped because the reader fell behind.</summary>
    internal long Overflows => Interlocked.Read(ref _overflows);

    /// <summary>Writer: copies frames in; frames that do not fit are dropped and counted.</summary>
    /// <param name="left">The left channel.</param>
    /// <param name="right">The right channel.</param>
    /// <returns>The frames kept.</returns>
    internal int Write(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        long written = Volatile.Read(ref _written);
        long free = _left.Length - (written - Volatile.Read(ref _read));
        int frames = (int)Math.Min(Math.Min(left.Length, right.Length), free);
        int offset = (int)(written % _left.Length);
        int first = Math.Min(frames, _left.Length - offset);
        left[..first].CopyTo(_left.AsSpan(offset));
        right[..first].CopyTo(_right.AsSpan(offset));
        left[first..frames].CopyTo(_left);
        right[first..frames].CopyTo(_right);
        Volatile.Write(ref _written, written + frames);
        int dropped = Math.Min(left.Length, right.Length) - frames;
        if (dropped > 0)
        {
            Interlocked.Add(ref _overflows, dropped);
        }

        return frames;
    }

    /// <summary>Counts the frames in [from, to) with a non-zero sample in either channel.</summary>
    /// <param name="from">The first frame position.</param>
    /// <param name="to">The position after the last frame.</param>
    /// <returns>The sounding frames.</returns>
    internal long CountSounding(long from, long to)
    {
        long count = 0;
        for (long position = Math.Max(from, to - _left.Length); position < to; position++)
        {
            int slot = (int)(position % _left.Length);
            count += _left[slot] != 0 || _right[slot] != 0 ? 1 : 0;
        }

        return count;
    }

    /// <summary>Writer: discards every frame written so far that the reader has not mixed.</summary>
    internal void RequestFlush() => Volatile.Write(ref _flushTo, Volatile.Read(ref _written));

    /// <summary>Reader: adds the next frames into the buffers.</summary>
    /// <param name="left">The left channel to add into.</param>
    /// <param name="right">The right channel to add into.</param>
    /// <returns>The frames mixed.</returns>
    internal int MixInto(Span<float> left, Span<float> right)
    {
        long read = Math.Max(Volatile.Read(ref _read), Volatile.Read(ref _flushTo));
        long available = Volatile.Read(ref _written) - read;
        if (!_playing && available < Math.Max(1, PrebufferFrames))
        {
            Volatile.Write(ref _read, read);
            return 0;
        }

        _playing = true;
        int frames = (int)Math.Min(Math.Min(left.Length, right.Length), available);
        int offset = (int)(read % _left.Length);
        int sounding = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            int slot = (offset + frame) % _left.Length;
            left[frame] += _left[slot];
            right[frame] += _right[slot];
            sounding += _left[slot] != 0 || _right[slot] != 0 ? 1 : 0;
        }

        Interlocked.Add(ref _mixedSounding, sounding);

        Volatile.Write(ref _read, read + frames);
        Interlocked.Add(ref _mixed, frames);
        if (frames < Math.Min(left.Length, right.Length))
        {
            _playing = false;
            Interlocked.Increment(ref _underruns);
        }

        return frames;
    }
}
