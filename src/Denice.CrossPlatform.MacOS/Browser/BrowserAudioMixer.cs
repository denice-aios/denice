// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform.MacOS.Browser;

/// <summary>
/// The browser's sole audio output. It opens its own default-output unit through DENICE's own AudioToolbox imports,
/// which do not go through the CEF framework's redirected entries, pulls every redirected Chromium stream and queued
/// page speech on its own clock, sums them at unity gain and plays the sum. The same mixed samples are handed to the
/// browser's audio sink on the main thread, so playback and recording carry identical audio.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio interop: render callbacks exchange stable unmanaged function pointers and buffers with AudioToolbox and Chromium.")]
internal sealed unsafe class BrowserAudioMixer : IDisposable
{
    /// <summary>The mixer's sample rate.</summary>
    internal const int SampleRate = 48000;

    private const int OutputChannels = 2;
#if DENICE_AUDIO_DIAGNOSTICS
    private const int CycleRingSize = 16384;
    private readonly long[] _cycleHost = new long[CycleRingSize];
    private readonly long[] _cycleCallback = new long[CycleRingSize];
    private readonly int[] _cycleFrames = new int[CycleRingSize];
    private readonly int[] _cycleStreams = new int[CycleRingSize];
#endif
    private readonly Lock _gate = new();
    private readonly MixedAudioQueue _mixed = new(SampleRate * 2, 1024);
    private readonly SpeechQueue _speech = new();
    private readonly float[] _drainLeft = new float[RedirectedStream.FrameCapacity];
    private readonly float[] _drainRight = new float[RedirectedStream.FrameCapacity];
#if DENICE_AUDIO_DIAGNOSTICS
    private long _cycleStarted = -1;
#endif
    private TimeProvider? _clock;
    private RedirectedStream[] _streams = [];
    private long _cycles;
    private nint _unit;
    private GCHandle _self;
    private bool _sinkStarted;
    private bool _failureReported;
    private long _droppedReported;
    private string? _failure;

    private BrowserAudioMixer()
    {
    }

    /// <summary>Gets the number of completed render cycles.</summary>
    internal long Cycles => Interlocked.Read(ref _cycles);

#if DENICE_AUDIO_DIAGNOSTICS
    /// <summary>Gets the index of the cycle that has started most recently, or -1.</summary>
    internal long CycleStarted => Interlocked.Read(ref _cycleStarted);
#endif

    /// <summary>Gets every speech frame ever queued; an utterance's position starts here.</summary>
    internal long SpeechFramesQueued => _speech.Queued;

    /// <summary>Gets every queued speech frame that has been mixed or discarded.</summary>
    internal long SpeechFramesDone => _speech.Done;

    /// <summary>Gets the speech frames mixed into the output so far.</summary>
    internal long SpeechFramesMixed => _speech.Mixed;

    /// <summary>Gets or sets the work run on the main thread each time mixed audio is drained (speech events).</summary>
    internal Action? MainThreadTick { get; set; }

    /// <summary>Gets the number of streams currently registered.</summary>
    internal int RegisteredStreams => Volatile.Read(ref _streams).Length;

    /// <summary>Gets the first audio failure, from the render callback or a bounded wait, if any.</summary>
    internal string? Failure => Volatile.Read(ref _failure);

    /// <summary>Gets the frames per device cycle seen most recently.</summary>
    internal int LastCycleFrames { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    /// <summary>Creates a stopped mixer.</summary>
    /// <returns>The mixer.</returns>
    internal static BrowserAudioMixer Create() => new();

    /// <summary>Starts the mixer's own output and returns only after its first render callback.</summary>
    /// <param name="clock">The injected clock for bounded waits.</param>
    internal void Start(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (_unit != 0)
        {
            throw new InvalidOperationException("The browser audio output has already started.");
        }

        UseClock(clock);
        _self = GCHandle.Alloc(this);
        _unit = AudioOutputRedirection.StartOutputUnit((nint)(delegate* unmanaged<nint, uint*, nint, uint, uint, nint, int>)&Render, GCHandle.ToIntPtr(_self), SampleRate);
        long started = clock.GetTimestamp();
        while (Cycles == 0)
        {
            if (clock.GetElapsedTime(started) > TimeSpan.FromSeconds(3))
            {
                throw new InvalidOperationException("The audio output produced no render callback within three seconds.");
            }

            Task.Delay(TimeSpan.FromMilliseconds(1), clock).GetAwaiter().GetResult();
        }
    }

    /// <summary>Sets the injected clock that bounds the mixer's waits.</summary>
    /// <param name="clock">The clock.</param>
    internal void UseClock(TimeProvider clock) => _clock = clock;

    /// <summary>Adds a redirected stream to the snapshot the render callback reads.</summary>
    /// <param name="stream">The stream.</param>
    internal void Register(RedirectedStream stream)
    {
        lock (_gate)
        {
            stream.MarkRegistered();
            if (Array.IndexOf(_streams, stream) < 0)
            {
                Volatile.Write(ref _streams, [.. _streams, stream]);
            }

            stream.State = StreamState.Registered;
        }
    }

    /// <summary>
    /// Takes the stream out of the mix and returns only when the render thread is not inside its Chromium callback and can
    /// never enter it again, keeping Core Audio's promise that no callback runs after a stop returns. Chromium frees the
    /// callback's context after that (AUHALStream::Close), so there is no timeout: the wait lasts exactly as long as the
    /// stream's own callback, which Chromium's own lock would make it wait for anyway. Waiting longer than two seconds is
    /// reported while the wait continues.
    /// </summary>
    /// <param name="stream">The stream.</param>
    internal void Unregister(RedirectedStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        lock (_gate)
        {
            if (Array.IndexOf(_streams, stream) >= 0)
            {
                Volatile.Write(ref _streams, [.. _streams.Where(item => !ReferenceEquals(item, stream))]);
            }
        }

        stream.MarkRemoved();
        WaitWhileInCallback(stream);
        stream.State = StreamState.Unregistered;
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
    internal void DrainTo(IPlatformBrowserAudioSink sink)
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

    /// <summary>Stops the mixer's own output; call only after CEF shutdown.</summary>
    internal void Stop()
    {
        if (_unit == 0)
        {
            return;
        }

        AudioOutputRedirection.StopOutputUnit(_unit);
        _unit = 0;
    }

    /// <summary>Mixes one device cycle into a two-buffer non-interleaved list; the render thread's entry point.</summary>
    /// <param name="timestamp">The cycle's AudioTimeStamp.</param>
    /// <param name="frames">The frames requested.</param>
    /// <param name="bufferList">The output AudioBufferList with two float buffers.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross the Core Audio render callback; the first one is recorded and reported on the main thread.")]
    internal void RenderCycle(nint timestamp, int frames, nint bufferList)
    {
        // Real-time thread: no locks, no allocation; streams are read from an immutable snapshot.
        float* left = (float*)Marshal.ReadIntPtr(bufferList + 8 + 8);
        float* right = (float*)Marshal.ReadIntPtr(bufferList + 8 + 16 + 8);
        long cycle = Interlocked.Read(ref _cycles);
        try
        {
            LastCycleFrames = frames;
            int count = Math.Min(frames, RedirectedStream.FrameCapacity);
            new Span<float>(left, frames).Clear();
            new Span<float>(right, frames).Clear();
            RedirectedStream[] streams = Volatile.Read(ref _streams);
            long host = AudioOutputRedirection.HostTimeToNanoseconds(*(ulong*)(timestamp + 8));
            RecordCycle(cycle, host, frames, streams.Length);
            foreach (RedirectedStream stream in streams)
            {
                MixStream(stream, timestamp, count, left, right, cycle);
            }

            int speech = _speech.MixInto(new Span<float>(left, count), new Span<float>(right, count));

            // A cycle with no stream and no speech is silence; the sink records gaps as silence, so it is not queued. This
            // keeps the queue empty while CEF starts up and the main thread cannot drain it.
            if (streams.Length > 0 || speech > 0)
            {
                _ = _mixed.TryWrite(new ReadOnlySpan<float>(left, count), new ReadOnlySpan<float>(right, count), host);
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

#if DENICE_AUDIO_DIAGNOSTICS
    /// <summary>Formats the recorded cycles in a range; called off the render thread.</summary>
    /// <param name="first">The first cycle index.</param>
    /// <param name="last">The last cycle index.</param>
    /// <returns>One line per cycle still held in the ring.</returns>
    internal IEnumerable<string> DescribeCycles(long first, long last)
    {
        long completed = Cycles;
        for (long cycle = Math.Max(0, Math.Max(first, completed - CycleRingSize)); cycle <= Math.Min(last, completed - 1); cycle++)
        {
            int slot = (int)(cycle % CycleRingSize);
            yield return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"DENICE-ICPT-CYCLE {cycle} host={_cycleHost[slot]} callback={_cycleCallback[slot]} frames={_cycleFrames[slot]} streams={_cycleStreams[slot]}");
        }
    }
#endif

    [UnmanagedCallersOnly]
    private static int Render(nint context, uint* flags, nint timestamp, uint bus, uint frames, nint bufferList)
    {
        if (GCHandle.FromIntPtr(context).Target is BrowserAudioMixer mixer)
        {
            mixer.RenderCycle(timestamp, (int)frames, bufferList);
        }

        return 0;
    }

    private static void MixStream(RedirectedStream stream, nint timestamp, int frames, float* left, float* right, long cycle)
    {
        nint procedure = stream.RenderCallback.Procedure;
        if (procedure == 0)
        {
            return;
        }

        if (!stream.TryEnterCallback())
        {
            // Removed after this cycle read its snapshot: Chromium may already have freed the callback's context.
            return;
        }

        if (stream.Converter is { } converter)
        {
            MixConverted(stream, converter, timestamp, frames, left, right, cycle);
            return;
        }

        int channels = (int)Math.Clamp(stream.Format.ChannelsPerFrame, 1, 8);
        int status;
        try
        {
            stream.PrepareBufferList(channels, frames);
            uint flags = 0;
            status = ((delegate* unmanaged<nint, uint*, nint, uint, uint, nint, int>)procedure)(stream.RenderCallback.Context, &flags, timestamp, 0, (uint)frames, stream.BufferList);
        }
        finally
        {
            stream.ExitCallback();
        }

        if (status != 0)
        {
            return;
        }

        float* first = (float*)stream.Channel(0);
        float* second = (float*)stream.Channel(channels > 1 ? 1 : 0);
        for (int frame = 0; frame < frames; frame++)
        {
            left[frame] += first[frame];
            right[frame] += second[frame];
        }

        stream.NotePull(cycle, first, second, frames);
    }

    private static void MixConverted(RedirectedStream stream, StreamRateConverter converter, nint timestamp, int frames, float* left, float* right, long cycle)
    {
        // Called with the stream's callback claim held; the claim ends here.
        try
        {
            _ = converter.Pull(timestamp, frames);
        }
        finally
        {
            stream.ExitCallback();
        }

        for (int frame = 0; frame < frames; frame++)
        {
            left[frame] += converter.Left[frame];
            right[frame] += converter.Right[frame];
        }

        stream.NotePull(cycle, converter.Left, converter.Right, frames);
    }

    private void WaitWhileInCallback(RedirectedStream stream)
    {
        long started = _clock?.GetTimestamp() ?? 0;
        bool reported = false;
        var spin = default(SpinWait);
        while (stream.InCallback)
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

    [System.Diagnostics.Conditional("DENICE_AUDIO_DIAGNOSTICS")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Uses instance data in diagnostic builds (DeniceAudioDiagnostics); empty otherwise.")]
    private void RecordCycle(long cycle, long host, int frames, int streams)
    {
#if DENICE_AUDIO_DIAGNOSTICS
        int slot = (int)(cycle % CycleRingSize);
        _cycleCallback[slot] = Recording.RecordingNative.HostNanoseconds();
        Interlocked.Exchange(ref _cycleStarted, cycle);
        _cycleHost[slot] = host;
        _cycleFrames[slot] = frames;
        _cycleStreams[slot] = streams;
#endif
    }
}
