// Copyright (c) Denice.ai. All rights reserved.

using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace Denice.Tests;

/// <summary>
/// A fake clock that reports the due time of every timer it creates, so a test advances time by exactly what the code
/// under test asked for instead of stepping blind (stepping raced the supervisor on slower hosts).
/// </summary>
internal sealed class ObservedClock : TimeProvider
{
    private readonly FakeTimeProvider _inner;
    private readonly ChannelWriter<TimeSpan> _timers;

    private ObservedClock(FakeTimeProvider inner, ChannelWriter<TimeSpan> timers)
    {
        _inner = inner;
        _timers = timers;
    }

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    /// <inheritdoc />
    public override long TimestampFrequency => _inner.TimestampFrequency;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();

    /// <inheritdoc />
    public override long GetTimestamp() => _inner.GetTimestamp();

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ITimer timer = _inner.CreateTimer(callback, state, dueTime, period);
        _timers.TryWrite(dueTime);
        return timer;
    }

    /// <summary>Wraps a fake clock.</summary>
    /// <param name="inner">The clock that owns time and timers.</param>
    /// <param name="timers">Receives each new timer's due time, after the timer exists.</param>
    /// <returns>The observing clock.</returns>
    internal static ObservedClock Create(FakeTimeProvider inner, ChannelWriter<TimeSpan> timers) => new(inner, timers);
}
