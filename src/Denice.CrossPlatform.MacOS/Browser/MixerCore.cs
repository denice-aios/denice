// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform.Browser;

/// <summary>
/// The OS-agnostic browser mixer. Each render cycle of the mixer's own output sums every registered redirected source
/// and queued page speech at unity gain into 48 kHz stereo, and queues the same samples for the browser's audio sink,
/// which receives them on the main thread, so playback and recording carry identical audio.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio interop: the browser audio sink takes planar float pointers to the mixed samples.")]
internal sealed class MixerCore : IMixerRender
{
    /// <summary>The mixer's sample rate.</summary>
    internal const int SampleRate = 48000;

    /// <summary>The most frames one cycle mixes; a longer cycle is cleared beyond this and left silent there.</summary>
    internal const int MaximumCycleFrames = 4096;

    private const int OutputChannels = 2;
    private readonly Lock _gate = new();
    private readonly MixedAudioQueue _mixed = new(SampleRate * 2, 1024);
    private readonly SpeechQueue _speech = new();
    private readonly float[] _drainLeft = new float[MaximumCycleFrames];
    private readonly float[] _drainRight = new float[MaximumCycleFrames];
    private TimeProvider? _clock;
    private MixerClaim[] _claims = [];
    private long _cycles;
    private bool _sinkStarted;
    private bool _failureReported;
    private long _droppedReported;
    private string? _failure;

    /// <summary>Gets the number of completed render cycles.</summary>
    internal long Cycles => Interlocked.Read(ref _cycles);

    /// <summary>Gets every speech frame ever queued; an utterance's position starts here.</summary>
    internal long SpeechFramesQueued => _speech.Queued;

    /// <summary>Gets every queued speech frame that has been mixed or discarded.</summary>
    internal long SpeechFramesDone => _speech.Done;

    /// <summary>Gets the speech frames mixed into the output so far.</summary>
    internal long SpeechFramesMixed => _speech.Mixed;

    /// <summary>Gets or sets the work run on the main thread each time mixed audio is drained (speech events).</summary>
    internal Action? MainThreadTick { get; set; }

    /// <summary>Gets the number of sources currently registered.</summary>
    internal int RegisteredSources => Volatile.Read(ref _claims).Length;

    /// <summary>Gets the first audio failure, from the render thread or a bounded wait, if any.</summary>
    internal string? Failure => Volatile.Read(ref _failure);

    /// <summary>Gets the frames per device cycle seen most recently.</summary>
    internal int LastCycleFrames { get; private set; }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross the device's render callback; the first one is recorded and reported on the main thread.")]
    public void RenderCycle(Span<float> left, Span<float> right, long hostNanoseconds)
    {
        // Real-time thread: no locks, no allocation; sources are read from an immutable snapshot.
        long cycle = Interlocked.Read(ref _cycles);
        try
        {
            int frames = Math.Min(left.Length, right.Length);
            LastCycleFrames = frames;
            left.Clear();
            right.Clear();
            int count = Math.Min(frames, MaximumCycleFrames);
            Span<float> mixLeft = left[..count];
            Span<float> mixRight = right[..count];
            MixerClaim[] claims = Volatile.Read(ref _claims);
            foreach (MixerClaim claim in claims)
            {
                MixSource(claim, mixLeft, mixRight, hostNanoseconds, cycle);
            }

            int speech = _speech.MixInto(mixLeft, mixRight);

            // A cycle with no source and no speech is silence; the sink records gaps as silence, so it is not queued. This
            // keeps the queue empty while CEF starts up and the main thread cannot drain it.
            if (claims.Length > 0 || speech > 0)
            {
                _ = _mixed.TryWrite(mixLeft, mixRight, hostNanoseconds);
            }
        }
        catch (Exception error)
        {
            Fail(error.Message);
        }
        finally
        {
            Interlocked.Increment(ref _cycles);
        }
    }

    /// <summary>Sets the injected clock that bounds the mixer's waits.</summary>
    /// <param name="clock">The clock.</param>
    internal void UseClock(TimeProvider clock) => _clock = clock;

    /// <summary>Adds a source to the snapshot the render thread reads; registering a registered source changes nothing.</summary>
    /// <param name="source">The source.</param>
    /// <returns>The source's registration.</returns>
    internal MixerClaim Register(IRedirectedSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            MixerClaim? existing = Array.Find(_claims, claim => ReferenceEquals(claim.Source, source));
            if (existing is not null)
            {
                return existing;
            }

            var added = new MixerClaim(source);
            Volatile.Write(ref _claims, [.. _claims, added]);
            return added;
        }
    }

    /// <summary>
    /// Takes the source out of the mix and returns only when the render thread is not inside its mix call and can never
    /// enter it again. The browser frees what the call reads once its own stop returns, so there is no timeout: the wait
    /// lasts exactly as long as the source's own running call. Waiting longer than two seconds is reported while the wait
    /// continues.
    /// </summary>
    /// <param name="source">The source.</param>
    internal void Unregister(IRedirectedSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        MixerClaim? removed;
        lock (_gate)
        {
            removed = Array.Find(_claims, claim => ReferenceEquals(claim.Source, source));
            if (removed is not null)
            {
                Volatile.Write(ref _claims, [.. _claims.Where(claim => !ReferenceEquals(claim, removed))]);
            }
        }

        if (removed is null)
        {
            return;
        }

        removed.MarkRemoved();
        WaitWhileInCallback(removed);
    }

    /// <summary>Queues speech already converted to the mixer rate, as one mono block.</summary>
    /// <param name="samples">The 48 kHz mono samples.</param>
    internal void QueueSpeech(float[] samples) => _speech.Enqueue(samples);

    /// <summary>Discards speech that has not been mixed yet, including the rest of the block being mixed.</summary>
    internal void ClearSpeech() => _speech.Clear();

    /// <summary>Holds speech at the next buffer boundary, or resumes it.</summary>
    /// <param name="held">Whether speech is held.</param>
    internal void HoldSpeech(bool held) => _speech.Hold(held);

    /// <summary>Records the first audio failure; later ones are ignored.</summary>
    /// <param name="message">The failure.</param>
    internal void Fail(string message) => Interlocked.CompareExchange(ref _failure, message, null);

    /// <summary>Hands mixed blocks, and any first failure, to the sink on the main thread.</summary>
    /// <param name="sink">The browser's audio sink.</param>
    internal unsafe void DrainTo(IPlatformBrowserAudioSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        nint* planes = stackalloc nint[OutputChannels];
        while (_mixed.TryRead(_drainLeft, _drainRight, out int frames, out long host))
        {
            if (!_sinkStarted)
            {
                sink.OnAudioStarted(OutputChannels, SampleRate);
                _sinkStarted = true;
            }

            fixed (float* left = _drainLeft)
            {
                fixed (float* right = _drainRight)
                {
                    planes[0] = (nint)left;
                    planes[1] = (nint)right;
                    sink.OnAudioPacket((nint)planes, frames, host / 1_000_000);
                }
            }
        }

        long dropped = _mixed.Dropped;
        if (dropped != _droppedReported)
        {
            _droppedReported = dropped;
            Fail($"{dropped} mixed audio blocks were dropped because the main thread fell behind.");
        }

        if (!_failureReported && Failure is { } failure)
        {
            _failureReported = true;
            sink.OnAudioError(failure);
        }

        MainThreadTick?.Invoke();
    }

    private static void MixSource(MixerClaim claim, Span<float> left, Span<float> right, long hostNanoseconds, long cycle)
    {
        if (!claim.TryEnter())
        {
            // Removed after this cycle read its snapshot: the browser may already have freed what the source reads.
            return;
        }

        try
        {
            claim.Source.MixInto(left, right, hostNanoseconds, cycle);
        }
        finally
        {
            claim.Exit();
        }
    }

    private void WaitWhileInCallback(MixerClaim claim)
    {
        long started = _clock?.GetTimestamp() ?? 0;
        bool reported = false;
        var spin = default(SpinWait);
        while (claim.InCallback)
        {
            if (_clock is not { } clock)
            {
                spin.SpinOnce();
                continue;
            }

            if (!reported && clock.GetElapsedTime(started) > TimeSpan.FromSeconds(2))
            {
                reported = true;
                Fail("A browser audio stream's Chromium callback ran for more than two seconds while the stream was being stopped; its removal waits for it to return.");
            }

            Task.Delay(TimeSpan.FromMilliseconds(1), clock).GetAwaiter().GetResult();
        }
    }
}
