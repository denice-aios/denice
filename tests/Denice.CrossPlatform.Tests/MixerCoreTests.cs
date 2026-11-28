// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Browser;
using Denice.TestHelpers;
using Xunit;
using static Denice.CrossPlatform.Tests.MixerHarness;

namespace Denice.CrossPlatform.Tests;

/// <summary>The OS-agnostic mixer core, driven through fake sources and the fake output on every OS.</summary>
public sealed class MixerCoreTests
{
    private const int Frames = 256;

    [Fact]
    public void RenderCycle_SumsSourcesAndSpeechAtUnityAndQueuesTheSameBlockForTheSink()
    {
        var core = new MixerCore();
        _ = core.Register(new ConstantSource(0.25f, 0.5f));
        _ = core.Register(new ConstantSource(0.125f, -0.25f));
        core.QueueSpeech(Enumerable.Repeat(0.0625f, Frames).ToArray());
        float[] left = new float[Frames];
        float[] right = new float[Frames];
        core.RenderCycle(left, right, 5_000_000);
        var log = new SinkLog();
        core.DrainTo(LoggingSink.Create(log));
        Assert.Equal(0.4375f, left[10], 6);
        Assert.Equal(0.3125f, right[10], 6);
        Assert.Equal(new AudioFormat(2, MixerCore.SampleRate), log.Format);
        Assert.Equal(left, Assert.Single(log.Packets));
        Assert.Equal(right, Assert.Single(log.RightPackets));
        Assert.Equal(Frames, core.SpeechFramesMixed);
        Assert.Equal(1, core.Cycles);
        Assert.Equal(Frames, core.LastCycleFrames);
    }

    [Fact]
    public void RenderCycle_ClearsTheWholeCycleAndMixesAtMostTheCycleLimit()
    {
        var core = new MixerCore();
        _ = core.Register(new ConstantSource(1f, 1f));
        int frames = MixerCore.MaximumCycleFrames + 16;
        float[] left = Enumerable.Repeat(9f, frames).ToArray();
        float[] right = Enumerable.Repeat(9f, frames).ToArray();
        core.RenderCycle(left, right, 0);
        Assert.Equal(1f, left[MixerCore.MaximumCycleFrames - 1]);
        Assert.Equal(0f, left[MixerCore.MaximumCycleFrames]);
        Assert.Equal(0f, right[^1]);
    }

    [Fact]
    public void RenderCycle_WithNoSourceOrSpeech_QueuesNothing()
    {
        var core = new MixerCore();
        float[] left = new float[Frames];
        float[] right = new float[Frames];
        for (int cycle = 0; cycle < 2000; cycle++)
        {
            core.RenderCycle(left, right, cycle);
        }

        var log = new SinkLog();
        core.DrainTo(LoggingSink.Create(log));
        Assert.Empty(log.Packets);
        Assert.Null(core.Failure);
    }

    [Fact]
    public void RenderCycle_RecordsASourceFailureAndKeepsCounting()
    {
        var core = new MixerCore();
        _ = core.Register(new ThrowingSource());
        core.RenderCycle(new float[Frames], new float[Frames], 0);
        Assert.Equal("source failed", core.Failure);
        Assert.Equal(1, core.Cycles);
        Assert.Equal(0, core.RegisteredSources - 1);
    }

    [Fact]
    public void Register_TwiceKeepsOneRegistrationAndUnregisterOfAnUnknownSourceReturns()
    {
        var core = new MixerCore();
        var source = new ConstantSource(0.25f, 0.25f);
        MixerClaim first = core.Register(source);
        MixerClaim second = core.Register(source);
        Assert.Same(first, second);
        Assert.Equal(1, core.RegisteredSources);
        core.Unregister(new ConstantSource(0f, 0f));
        Assert.Equal(1, core.RegisteredSources);
        core.Unregister(source);
        Assert.Equal(0, core.RegisteredSources);
        Assert.False(first.InCallback);
    }

    [Fact]
    public void DrainTo_ReportsTheFirstFailureOnceAndRunsTheMainThreadTick()
    {
        var core = new MixerCore();
        int ticks = 0;
        core.MainThreadTick = () => ticks++;
        core.Fail("first");
        core.Fail("second");
        var log = new SinkLog();
        IPlatformBrowserAudioSink sink = LoggingSink.Create(log);
        core.DrainTo(sink);
        core.DrainTo(sink);
        Assert.Equal(["first"], log.Errors);
        Assert.Equal(2, ticks);
    }

    [Fact]
    public async Task Unregister_WaitsForTheRunningMixCallAndTheRunningCycleSkipsALaterRemovedSource()
    {
        TimeProvider clock = SystemServicesBuilder.Real().Build().Clock;
        var core = new MixerCore();
        core.UseClock(clock);
        using var blocking = new BlockingSource();
        var counted = new CountingSource();
        _ = core.Register(blocking);
        _ = core.Register(counted);
        Task render = Task.Run(() => core.RenderCycle(new float[Frames], new float[Frames], 0));
        Assert.True(blocking.Entered.Wait(TimeSpan.FromSeconds(5)));
        core.Unregister(counted);
        Task removal = Task.Run(() => core.Unregister(blocking));
        await Task.Delay(TimeSpan.FromMilliseconds(100), clock).ConfigureAwait(true);
        Assert.False(removal.IsCompleted, "Removal must not return while the source's mix call still runs.");
        blocking.Release.Set();
        await removal.ConfigureAwait(true);
        await render.ConfigureAwait(true);
        Assert.Equal(0, counted.Calls);
        Assert.Equal(0, core.RegisteredSources);
    }

    [Fact]
    public void FakeOutput_StartsAfterItsFirstCycleAndStops()
    {
        TimeProvider clock = SystemServicesBuilder.Real().Build().Clock;
        var core = new MixerCore();
        _ = core.Register(new ConstantSource(0.5f, 0.5f));
        using var output = new FakeMixerOutput();
        output.Start(core, clock);
        Assert.True(core.Cycles >= 1);
        Assert.Throws<InvalidOperationException>(() => output.Start(core, clock));
        output.Stop();
        long stopped = core.Cycles;
        output.Stop();
        Assert.Equal(stopped, core.Cycles);
        Assert.Equal(FakeMixerOutput.CycleFrames, core.LastCycleFrames);
    }

    [Fact]
    public void WaitForFirstCycle_FailsAfterThreeSecondsWithoutACycle()
    {
        TimeProvider clock = SystemServicesBuilder.Real().Build().Clock;
        long before = clock.GetTimestamp();
        Assert.Throws<InvalidOperationException>(() => MixerOutputStart.WaitForFirstCycle(() => 0, clock));
        Assert.True(clock.GetElapsedTime(before) >= TimeSpan.FromSeconds(3));
    }

    private sealed class ConstantSource(float leftValue, float rightValue) : IRedirectedSource
    {
        public void MixInto(Span<float> left, Span<float> right, long hostNanoseconds, long cycle)
        {
            for (int frame = 0; frame < left.Length; frame++)
            {
                left[frame] += leftValue;
                right[frame] += rightValue;
            }
        }
    }

    private sealed class ThrowingSource : IRedirectedSource
    {
        public void MixInto(Span<float> left, Span<float> right, long hostNanoseconds, long cycle) => throw new InvalidOperationException("source failed");
    }

    private sealed class CountingSource : IRedirectedSource
    {
        internal int Calls { get; private set; }

        public void MixInto(Span<float> left, Span<float> right, long hostNanoseconds, long cycle) => Calls++;
    }

    private sealed class BlockingSource : IRedirectedSource, IDisposable
    {
        internal ManualResetEventSlim Entered { get; } = new(false);

        internal ManualResetEventSlim Release { get; } = new(false);

        public void MixInto(Span<float> left, Span<float> right, long hostNanoseconds, long cycle)
        {
            Entered.Set();
            _ = Release.Wait(TimeSpan.FromSeconds(10));
        }

        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }
}
