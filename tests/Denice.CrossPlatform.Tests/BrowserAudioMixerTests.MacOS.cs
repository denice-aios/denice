// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.MacOS.Browser;
using Denice.TestHelpers;
using Xunit;
using static Denice.CrossPlatform.Tests.MixerHarness;

namespace Denice.CrossPlatform.Tests;

public sealed class BrowserAudioMixerTests
{
    private const int Frames = 256;

    [Fact]
    public void SpeechQueue_SpeakRightAfterCancelFinishesFromItsOwnStart()
    {
        // Regression: the cancelled block's unplayed tail must not count toward the next utterance's position.
        var speech = new SpeechQueue();
        speech.Enqueue(Block(1000, 0.1f));
        var left = new float[Frames];
        var right = new float[Frames];
        _ = speech.MixInto(left, right);
        speech.Clear();
        long start = speech.Queued;
        speech.Enqueue(Block(500, 0.2f));
        long finish = start + 500;
        Array.Clear(left);
        _ = speech.MixInto(left, right);
        Assert.Equal(0.2f, left[0]);
        for (int cycle = 0; cycle < 10 && speech.Done < finish; cycle++)
        {
            speech.MixInto(left, right);
        }

        Assert.True(speech.Done >= finish, $"Done {speech.Done} never reached {finish}.");
        Assert.Equal(Frames + 500, speech.Mixed);
        Assert.Equal(speech.Queued, speech.Done);
    }

    [Fact]
    public void SpeechQueue_ClearCountsQueuedBlocksAsDoneAtOnce()
    {
        var speech = new SpeechQueue();
        speech.Enqueue(Block(300, 0.1f));
        speech.Enqueue(Block(400, 0.1f));
        speech.Clear();
        Assert.Equal(700, speech.Done);
        Assert.Equal(0, speech.Mixed);
    }

    [Fact]
    public void SpeechQueue_HoldStopsAtABlockBoundaryAndResumes()
    {
        var speech = new SpeechQueue();
        speech.Enqueue(Block(100, 0.1f));
        speech.Enqueue(Block(100, 0.3f));
        speech.Hold(true);
        var left = new float[Frames];
        var right = new float[Frames];
        _ = speech.MixInto(left, right);
        Assert.Equal(0, speech.Mixed);
        speech.Hold(false);
        _ = speech.MixInto(left, right);
        Assert.Equal(200, speech.Mixed);
        Assert.Equal(0.3f, left[150], 6);
    }

    [Fact]
    public void Render_SumsStreamsAndSpeechAtUnityAndHandsTheSameBlockToTheSink()
    {
        var buffers = new RenderBuffers(Frames);
        using var mixer = BrowserAudioMixer.Create();
        using RedirectedStream first = FakeStreams.Create(1, FakeStreams.Constant);
        using RedirectedStream second = FakeStreams.Create(2, FakeStreams.Constant);
        mixer.Register(first);
        mixer.Register(second);
        mixer.QueueSpeech(Block(Frames, 0.125f));
        buffers.Render(mixer);
        var log = new SinkLog();
        mixer.DrainTo(LoggingSink.Create(log));

        // Each fake stream writes 0.25 on its first channel and 0.5 on its second.
        Assert.Equal(0.625f, buffers.Left[10], 6);
        Assert.Equal(1.125f, buffers.Right[10], 6);
        Assert.Equal(new AudioFormat(2, 48000), log.Format);
        Assert.Equal(Frames, Assert.Single(log.Packets).Length);
        Assert.Equal(0.625f, log.Packets[0][10], 6);
        Assert.Empty(log.Errors);
    }

    [Fact]
    public void Render_WithNoStreamOrSpeech_QueuesNothingSoAStalledMainThreadDropsNothing()
    {
        // Regression: while CEF starts, the main thread cannot drain; silent cycles must not fill the queue and be reported as drops.
        var buffers = new RenderBuffers(Frames);
        using var mixer = BrowserAudioMixer.Create();
        for (int cycle = 0; cycle < 2000; cycle++)
        {
            buffers.Render(mixer);
        }

        var log = new SinkLog();
        mixer.DrainTo(LoggingSink.Create(log));
        Assert.Empty(log.Packets);
        Assert.Empty(log.Errors);
        Assert.Null(mixer.Failure);
    }

    [Fact]
    public void DrainTo_ReportsTheFirstFailureOnce()
    {
        using var mixer = BrowserAudioMixer.Create();
        mixer.Fail("first");
        mixer.Fail("second");
        var log = new SinkLog();
        IPlatformBrowserAudioSink sink = LoggingSink.Create(log);
        mixer.DrainTo(sink);
        mixer.DrainTo(sink);
        Assert.Equal(["first"], log.Errors);
    }

    [Fact]
    public void Unregister_WithNoCallbackRunning_ReturnsAtOnce()
    {
        var buffers = new RenderBuffers(Frames);
        using var mixer = BrowserAudioMixer.Create();
        using RedirectedStream stream = FakeStreams.Create(1, FakeStreams.Constant);
        mixer.Register(stream);
        buffers.Render(mixer);
        mixer.Unregister(stream);
        Assert.Equal(StreamState.Unregistered, stream.State);
        Assert.False(stream.InCallback);
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(2300, true)]
    public async Task Unregister_WaitsForTheStreamsOwnRunningCallbackWithoutTimeout(int callbackMilliseconds, bool reportsLongWait)
    {
        // There is no timeout. Chromium frees the callback's context once the stop returns, so returning early would be
        // unsafe. A callback running past two seconds is reported while the wait continues.
        TimeProvider clock = SystemServicesBuilder.Real().Build().Clock;
        using var mixer = BrowserAudioMixer.Create();
        (RedirectedStream stream, Task render) = StartBlockedRender(mixer, clock, null);
        using RedirectedStream owned = stream;
        Task release = ReleaseAfter(clock, TimeSpan.FromMilliseconds(callbackMilliseconds));
        long before = clock.GetTimestamp();
        mixer.Unregister(stream);
        Assert.True(clock.GetElapsedTime(before) >= TimeSpan.FromMilliseconds(callbackMilliseconds - 100), "Removal must not return while the stream's callback still runs.");
        Assert.False(stream.InCallback);
        await render.ConfigureAwait(true);
        await release.ConfigureAwait(true);
        var log = new SinkLog();
        mixer.DrainTo(LoggingSink.Create(log));
        Assert.Equal(reportsLongWait, log.Errors.Any(error => error.Contains("more than two seconds", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Unregister_DoesNotWaitForAnotherStreamAndTheRunningCycleSkipsTheRemovedStream()
    {
        // Regression: a cycle that read its snapshot before the removal must not call the removed stream afterwards,
        // because Chromium frees that stream's callback context once its stop returns.
        TimeProvider clock = SystemServicesBuilder.Real().Build().Clock;
        using var mixer = BrowserAudioMixer.Create();
        using RedirectedStream counted = FakeStreams.Create(2, FakeStreams.Counting);
        FakeStreams.CountingCalls = 0;
        (RedirectedStream stuck, Task render) = StartBlockedRender(mixer, clock, counted);
        using RedirectedStream owned = stuck;
        long before = clock.GetTimestamp();
        mixer.Unregister(counted);
        Assert.True(clock.GetElapsedTime(before) < TimeSpan.FromSeconds(1), "Removing an idle stream must not wait for another stream's callback.");
        FakeStreams.Release.Set();
        await render.ConfigureAwait(true);
        Assert.Equal(0, FakeStreams.CountingCalls);
    }

    [Fact]
    public void Desktop_KeepsAnAudioFailureWithoutARecordingAndReportsItInTheRunStatistics()
    {
        // Regression: OnAudioError used to discard the failure unless a recording was active.
        IPlatformDesktop desktop = Denice.CrossPlatform.MacOS.Desktop.MacDesktop.Create();
        var sink = Assert.IsAssignableFrom<IPlatformBrowserAudioSink>(desktop);
        Assert.Contains("Browser audio: no failures.", desktop.LastFrameStatistics, StringComparison.Ordinal);
        sink.OnAudioError("3 browser audio streams were still open after CEF shutdown.");
        sink.OnAudioError("a later failure");
        Assert.Contains("Browser audio: FAILED: 3 browser audio streams were still open after CEF shutdown.", desktop.LastFrameStatistics, StringComparison.Ordinal);
        Assert.Null(desktop.LastRecordingError);
        Assert.Equal("3 browser audio streams were still open after CEF shutdown.", desktop.LastAudioError);
    }

    private static Task ReleaseAfter(TimeProvider clock, TimeSpan delay) => Task.Run(async () =>
    {
        await Task.Delay(delay, clock).ConfigureAwait(false);
        FakeStreams.Release.Set();
    });

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The stream is returned to the caller, which owns and disposes it.")]
    private static BlockedRender StartBlockedRender(BrowserAudioMixer mixer, TimeProvider clock, RedirectedStream? after)
    {
        // One render cycle that blocks inside the first stream's Chromium callback until the test releases it.
        mixer.UseClock(clock);
        RedirectedStream stream = FakeStreams.Create(1, FakeStreams.Blocking);
        mixer.Register(stream);
        if (after is not null)
        {
            mixer.Register(after);
        }

        FakeStreams.Entered.Reset();
        FakeStreams.Release.Reset();
        var buffers = new RenderBuffers(Frames);
        Task render = Task.Run(() => buffers.Render(mixer));
        Assert.True(FakeStreams.Entered.Wait(TimeSpan.FromSeconds(5)));
        return new BlockedRender(stream, render);
    }

    private static float[] Block(int length, float value) => Enumerable.Repeat(value, length).ToArray();

    /// <summary>A render callback held inside a stream's Chromium callback.</summary>
    /// <param name="Stream">The stream being pulled.</param>
    /// <param name="Render">The render cycle, finishing once released.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct BlockedRender(RedirectedStream Stream, Task Render);

    /// <summary>Fake Chromium render callbacks.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "The mixer's render entry point and Chromium's callbacks take unmanaged Core Audio structures.")]
    private static unsafe class FakeStreams
    {
        internal static ManualResetEventSlim Entered { get; } = new(false);

        internal static ManualResetEventSlim Release { get; } = new(true);

        internal static nint Constant => (nint)(delegate* unmanaged<nint, uint*, nint, uint, uint, nint, int>)&WriteConstant;

        internal static nint Blocking => (nint)(delegate* unmanaged<nint, uint*, nint, uint, uint, nint, int>)&BlockUntilReleased;

        internal static nint Counting => (nint)(delegate* unmanaged<nint, uint*, nint, uint, uint, nint, int>)&Count;

        internal static int CountingCalls { get; set; }

        internal static RedirectedStream Create(nint unit, nint callback)
        {
            RedirectedStream stream = RedirectedStream.Create(unit);
            stream.RenderCallback = new RedirectedStream.RenderCallbackTarget(callback, 0);
            stream.Format = new RedirectedStream.StreamFormat(48000, 0x29, 2, 32);
            return stream;
        }

        [UnmanagedCallersOnly]
        private static int WriteConstant(nint context, uint* flags, nint timestamp, uint bus, uint frames, nint bufferList)
        {
            float* first = *(float**)(bufferList + 8 + 8);
            float* second = *(float**)(bufferList + 8 + 16 + 8);
            for (int frame = 0; frame < frames; frame++)
            {
                first[frame] = 0.25f;
                second[frame] = 0.5f;
            }

            return 0;
        }

        [UnmanagedCallersOnly]
        private static int Count(nint context, uint* flags, nint timestamp, uint bus, uint frames, nint bufferList)
        {
            CountingCalls++;
            return 0;
        }

        [UnmanagedCallersOnly]
        private static int BlockUntilReleased(nint context, uint* flags, nint timestamp, uint bus, uint frames, nint bufferList)
        {
            Entered.Set();
            _ = Release.Wait(TimeSpan.FromSeconds(10));
            return 0;
        }
    }
}
