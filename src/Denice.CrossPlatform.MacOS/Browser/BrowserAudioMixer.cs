// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.MacOS.Browser;

/// <summary>
/// The macOS browser mixer: the OS-agnostic <see cref="MixerCore"/> played by a <see cref="MacMixerOutput"/>, the
/// default-output unit DENICE opens through its own AudioToolbox imports. Every redirected Chromium unit is a
/// <see cref="RedirectedStream"/> source of the core, and the same mixed samples are handed to the browser's audio sink
/// on the main thread, so playback and recording carry identical audio.
/// </summary>
internal sealed class BrowserAudioMixer : IMixerRender, IDisposable
{
    /// <summary>The mixer's sample rate.</summary>
    internal const int SampleRate = MixerCore.SampleRate;

#if DENICE_AUDIO_DIAGNOSTICS
    private const int CycleRingSize = 16384;
    private readonly long[] _cycleHost = new long[CycleRingSize];
    private readonly long[] _cycleCallback = new long[CycleRingSize];
    private readonly int[] _cycleFrames = new int[CycleRingSize];
    private readonly int[] _cycleStreams = new int[CycleRingSize];
#endif
    private readonly MixerCore _core = new();
    private readonly CoreAudioCycle _cycle = new();
#if DENICE_AUDIO_DIAGNOSTICS
    private long _cycleStarted = -1;
#endif
    private MacMixerOutput? _output;

    private BrowserAudioMixer()
    {
    }

    /// <summary>Gets the number of completed render cycles.</summary>
    internal long Cycles => _core.Cycles;

#if DENICE_AUDIO_DIAGNOSTICS
    /// <summary>Gets the index of the cycle that has started most recently, or -1.</summary>
    internal long CycleStarted => Interlocked.Read(ref _cycleStarted);
#endif

    /// <summary>Gets every speech frame ever queued; an utterance's position starts here.</summary>
    internal long SpeechFramesQueued => _core.SpeechFramesQueued;

    /// <summary>Gets every queued speech frame that has been mixed or discarded.</summary>
    internal long SpeechFramesDone => _core.SpeechFramesDone;

    /// <summary>Gets the speech frames mixed into the output so far.</summary>
    internal long SpeechFramesMixed => _core.SpeechFramesMixed;

    /// <summary>Gets or sets the work run on the main thread each time mixed audio is drained (speech events).</summary>
    internal Action? MainThreadTick
    {
        get => _core.MainThreadTick;
        set => _core.MainThreadTick = value;
    }

    /// <summary>Gets the number of streams currently registered.</summary>
    internal int RegisteredStreams => _core.RegisteredSources;

    /// <summary>Gets the first audio failure, from the render callback or a bounded wait, if any.</summary>
    internal string? Failure => _core.Failure;

    /// <summary>Gets the frames per device cycle seen most recently.</summary>
    internal int LastCycleFrames => _core.LastCycleFrames;

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _output?.Dispose();
        _output = null;
    }

    /// <inheritdoc />
    public void RenderCycle(Span<float> left, Span<float> right, long hostNanoseconds)
    {
        RecordCycle(_core.Cycles, hostNanoseconds, left.Length, _core.RegisteredSources);
        _core.RenderCycle(left, right, hostNanoseconds);
    }

    /// <summary>Creates a stopped mixer.</summary>
    /// <returns>The mixer.</returns>
    internal static BrowserAudioMixer Create() => new();

    /// <summary>Starts the mixer's own output and returns only after its first render callback.</summary>
    /// <param name="clock">The injected clock for bounded waits.</param>
    internal void Start(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (_output is not null)
        {
            throw new InvalidOperationException("The browser audio output has already started.");
        }

        UseClock(clock);
        _output = new MacMixerOutput(_cycle);
        _output.Start(this, clock);
    }

    /// <summary>Sets the injected clock that bounds the mixer's waits.</summary>
    /// <param name="clock">The clock.</param>
    internal void UseClock(TimeProvider clock) => _core.UseClock(clock);

    /// <summary>Adds a redirected stream to the snapshot the render callback reads.</summary>
    /// <param name="stream">The stream.</param>
    internal void Register(RedirectedStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Cycle = _cycle;
        stream.Claim = _core.Register(stream);
        stream.State = StreamState.Registered;
    }

    /// <summary>
    /// Takes the stream out of the mix and returns only when the render thread is not inside its Chromium callback and can
    /// never enter it again, keeping Core Audio's promise that no callback runs after a stop returns. Chromium frees the
    /// callback's context after that (AUHALStream::Close), so there is no timeout (see <see cref="MixerCore.Unregister"/>).
    /// </summary>
    /// <param name="stream">The stream.</param>
    internal void Unregister(RedirectedStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _core.Unregister(stream);
        stream.State = StreamState.Unregistered;
    }

    /// <summary>Queues speech already converted to the mixer rate, as one mono block.</summary>
    /// <param name="samples">The 48 kHz mono samples.</param>
    internal void QueueSpeech(float[] samples) => _core.QueueSpeech(samples);

    /// <summary>Discards speech that has not been mixed yet, including the rest of the block being mixed.</summary>
    internal void ClearSpeech() => _core.ClearSpeech();

    /// <summary>Holds speech at the next buffer boundary, or resumes it.</summary>
    /// <param name="held">Whether speech is held.</param>
    internal void HoldSpeech(bool held) => _core.HoldSpeech(held);

    /// <summary>Records the first audio failure; later ones are ignored.</summary>
    /// <param name="message">The failure.</param>
    internal void Fail(string message) => _core.Fail(message);

    /// <summary>Hands mixed blocks, and any first failure, to the sink on the main thread.</summary>
    /// <param name="sink">The browser's audio sink.</param>
    internal void DrainTo(IPlatformBrowserAudioSink sink) => _core.DrainTo(sink);

    /// <summary>Stops the mixer's own output; call only after CEF shutdown.</summary>
    internal void Stop() => _output?.Stop();

    /// <summary>Mixes one device cycle into a two-buffer non-interleaved list, as the output unit's render callback does.</summary>
    /// <param name="timestamp">The cycle's AudioTimeStamp.</param>
    /// <param name="frames">The frames requested.</param>
    /// <param name="bufferList">The output AudioBufferList with two float buffers.</param>
    internal void RenderCycle(nint timestamp, int frames, nint bufferList) => _cycle.Render(this, timestamp, frames, bufferList);

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
