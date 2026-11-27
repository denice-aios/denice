// Copyright (c) Denice.ai. All rights reserved.

using System.Collections.Concurrent;

namespace Denice.CrossPlatform.MacOS.Browser;

/// <summary>
/// Page speech waiting to be mixed. The main thread queues and clears blocks; the render thread mixes them. Positions
/// are counted in mixer frames: <see cref="Queued"/> only grows, and <see cref="Done"/> counts frames that were either
/// mixed or discarded by a clear, so an utterance queued at <c>Queued</c> = q is fully played exactly when
/// <c>Done</c> reaches q plus its length, whether or not earlier speech was cancelled.
/// </summary>
internal sealed class SpeechQueue
{
    private readonly ConcurrentQueue<float[]> _blocks = new();
    private float[]? _current;
    private int _offset;
    private long _queued;
    private long _done;
    private long _mixed;
    private volatile bool _held;
    private volatile bool _cancel;

    /// <summary>Gets every frame ever queued.</summary>
    internal long Queued => Interlocked.Read(ref _queued);

    /// <summary>Gets every queued frame that has been mixed or discarded.</summary>
    internal long Done => Interlocked.Read(ref _done);

    /// <summary>Gets every frame mixed into the output.</summary>
    internal long Mixed => Interlocked.Read(ref _mixed);

    /// <summary>Queues one mono block at the mixer rate; main thread.</summary>
    /// <param name="samples">The samples.</param>
    internal void Enqueue(float[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        _blocks.Enqueue(samples);
        Interlocked.Add(ref _queued, samples.Length);
    }

    /// <summary>Discards every block not yet mixed; the rest of the block being mixed is discarded on the next mix.</summary>
    internal void Clear()
    {
        while (_blocks.TryDequeue(out float[]? dropped))
        {
            Interlocked.Add(ref _done, dropped.Length);
        }

        _cancel = true;
    }

    /// <summary>Holds speech at the next block boundary, or resumes it.</summary>
    /// <param name="held">Whether speech is held.</param>
    internal void Hold(bool held) => _held = held;

    /// <summary>Adds queued speech to both channels; render thread only, no locks or allocation.</summary>
    /// <param name="left">The left channel to add to.</param>
    /// <param name="right">The right channel to add to.</param>
    /// <returns>The frames of speech mixed in this call.</returns>
    internal int MixInto(Span<float> left, Span<float> right)
    {
        if (_cancel)
        {
            _cancel = false;
            if (_current is not null)
            {
                Interlocked.Add(ref _done, _current.Length - _offset);
                _current = null;
            }
        }

        int frames = Math.Min(left.Length, right.Length);
        int frame = 0;
        while (frame < frames)
        {
            if (_current is null || _offset >= _current.Length)
            {
                if (_held || !_blocks.TryDequeue(out _current))
                {
                    _current = null;
                    return frame;
                }

                _offset = 0;
            }

            int take = Math.Min(frames - frame, _current.Length - _offset);
            for (int index = 0; index < take; index++)
            {
                float sample = _current[_offset + index];
                left[frame + index] += sample;
                right[frame + index] += sample;
            }

            _offset += take;
            frame += take;
            Interlocked.Add(ref _mixed, take);
            Interlocked.Add(ref _done, take);
        }

        return frame;
    }
}
