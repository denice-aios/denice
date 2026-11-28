// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.CrossPlatform.Browser;

/// <summary>The start wait every <see cref="IMixerOutput"/> shares: return after the first render cycle, or fail after three seconds.</summary>
internal static class MixerOutputStart
{
    /// <summary>Waits until the output's cycle count is above zero.</summary>
    /// <param name="cycles">Reads the output's completed cycles.</param>
    /// <param name="clock">The injected clock that bounds the wait.</param>
    internal static void WaitForFirstCycle(Func<long> cycles, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(cycles);
        ArgumentNullException.ThrowIfNull(clock);
        long started = clock.GetTimestamp();
        while (cycles() == 0)
        {
            if (clock.GetElapsedTime(started) > TimeSpan.FromSeconds(3))
            {
                throw new InvalidOperationException("The audio output produced no render callback within three seconds.");
            }

            Task.Delay(TimeSpan.FromMilliseconds(1), clock).GetAwaiter().GetResult();
        }
    }
}
