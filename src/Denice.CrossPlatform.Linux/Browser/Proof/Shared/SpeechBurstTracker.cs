// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Browser;

/// <summary>
/// Times each sounding burst of a captured speech source at two points: where its first sounding frame arrives from the
/// capture (with the queue's pending frames and overflows at that moment) and where the mixer first mixes that frame. A
/// burst starts at the first sounding frame after at least <see cref="SilenceFrames"/> silent ones. The capture thread
/// writes the first half, the mixer's real-time thread the second; neither locks or allocates.
/// </summary>
internal sealed class SpeechBurstTracker
{
    /// <summary>The silent frames (50 ms) that separate two bursts.</summary>
    internal const int SilenceFrames = 2400;

    /// <summary>The level below which a sample counts as silent.</summary>
    internal const float Threshold = 1e-3f;

    private readonly Burst[] _bursts;
    private long _silent = SilenceFrames;
    private int _captured;
    private int _mixed;

    /// <summary>Initializes a new instance of the <see cref="SpeechBurstTracker"/> class.</summary>
    /// <param name="capacity">The bursts kept; later bursts are counted and dropped.</param>
    internal SpeechBurstTracker(int capacity) => _bursts = new Burst[capacity];

    /// <summary>Gets the bursts the capacity could not hold.</summary>
    internal int Dropped { get; private set; }

    /// <summary>Gets the bursts recorded so far, in order.</summary>
    internal IReadOnlyList<Burst> Bursts => _bursts.AsSpan(0, Volatile.Read(ref _captured)).ToArray();

    /// <summary>Capture thread: notes one queued block.</summary>
    /// <param name="samples">The block, already queued.</param>
    /// <param name="queuedFrom">The queue's written-frame count before this block.</param>
    /// <param name="pendingBefore">The queue's pending frames before this block.</param>
    /// <param name="overflows">The queue's overflow frames so far.</param>
    /// <param name="hostNanoseconds">The capture time on the host clock.</param>
    internal void OnCaptured(ReadOnlySpan<float> samples, long queuedFrom, long pendingBefore, long overflows, long hostNanoseconds)
    {
        for (int index = 0; index < samples.Length; index++)
        {
            if (Math.Abs(samples[index]) < Threshold)
            {
                _silent++;
                continue;
            }

            if (_silent >= SilenceFrames)
            {
                Start(new Burst(queuedFrom + index, hostNanoseconds, pendingBefore, overflows, -1));
            }

            _silent = 0;
        }
    }

    /// <summary>Mixer thread: notes how far the queue has been mixed after one cycle.</summary>
    /// <param name="mixedFrames">The queue's mixed-frame count, which counts the same frames as its written count.</param>
    /// <param name="hostNanoseconds">The cycle's host time.</param>
    internal void OnMixed(long mixedFrames, long hostNanoseconds)
    {
        int captured = Volatile.Read(ref _captured);
        while (_mixed < captured && _bursts[_mixed].Frame < mixedFrames)
        {
            _bursts[_mixed] = _bursts[_mixed] with { MixedHostNanoseconds = hostNanoseconds };
            _mixed++;
        }
    }

    private void Start(Burst burst)
    {
        int captured = _captured;
        if (captured >= _bursts.Length)
        {
            Dropped++;
            return;
        }

        _bursts[captured] = burst;
        Volatile.Write(ref _captured, captured + 1);
    }

    /// <summary>One sounding burst.</summary>
    /// <param name="Frame">Its first sounding frame's index in the queue's written frames.</param>
    /// <param name="CapturedHostNanoseconds">When that frame arrived from the capture.</param>
    /// <param name="PendingFrames">The queue's pending frames at that moment.</param>
    /// <param name="OverflowFrames">The queue's overflow frames at that moment.</param>
    /// <param name="MixedHostNanoseconds">The host time of the cycle that first mixed it, or -1.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Burst(long Frame, long CapturedHostNanoseconds, long PendingFrames, long OverflowFrames, long MixedHostNanoseconds);
}
