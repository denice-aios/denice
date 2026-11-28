// Copyright (c) Denice.ai. All rights reserved.

using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// A test-only mixer output with no device: a background loop renders one 5 ms cycle (240 frames at 48 kHz) on the
/// injected clock's schedule, so the mixer core runs at real-time pace on any OS, including hosts with no audio endpoint.
/// Nothing reaches any device.
/// </summary>
internal sealed class FakeMixerOutput : IMixerOutput
{
    /// <summary>The frames rendered per cycle.</summary>
    internal const int CycleFrames = 240;

    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(5);
    private readonly float[] _left = new float[CycleFrames];
    private readonly float[] _right = new float[CycleFrames];
    private CancellationTokenSource? _stop;
    private Task? _loop;
    private long _cycles;

    /// <summary>Gets the cycles rendered so far.</summary>
    internal long Cycles => Interlocked.Read(ref _cycles);

    /// <inheritdoc />
    public void Start(IMixerRender render, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(clock);
        if (_loop is not null)
        {
            throw new InvalidOperationException("The fake mixer output has already started.");
        }

        _stop = new CancellationTokenSource();
        CancellationToken token = _stop.Token;
        _loop = Task.Run(() => Run(render, clock, token), token);
        MixerOutputStart.WaitForFirstCycle(() => Cycles, clock);
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_loop is null || _stop is null)
        {
            return;
        }

        _stop.Cancel();
        _loop.GetAwaiter().GetResult();
        _loop = null;
        _stop.Dispose();
        _stop = null;
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private void Run(IMixerRender render, TimeProvider clock, CancellationToken stop)
    {
        long origin = clock.GetTimestamp();
        long cycle = 0;
        while (!stop.IsCancellationRequested)
        {
            TimeSpan due = Period * cycle;
            render.RenderCycle(_left, _right, due.Ticks * 100);
            _ = Interlocked.Increment(ref _cycles);
            cycle++;
            TimeSpan wait = (Period * cycle) - clock.GetElapsedTime(origin);
            if (wait <= TimeSpan.Zero)
            {
                continue;
            }

            try
            {
                Task.Delay(wait, clock, stop).GetAwaiter().GetResult();
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }
}
