// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.Engine;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Denice.Tests;

/// <summary>The application supervisor: one attempt at a time, back-off, probing, restart and stop.</summary>
public sealed class ApplicationSupervisorTests : IAsyncLifetime
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(50);
    private readonly FakeTimeProvider _clock = new();
    private readonly FakeApplicationHost _host = FakeApplicationHost.Create();

    /// <inheritdoc />
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    [Fact]
    public void FailedStartsRetryWithTheApprovedBackOff()
    {
        for (int index = 0; index < 7; index++)
        {
            _host.Fails("busy");
        }

        IApplicationSession session = ApplicationSupervisor.Create(_host, _clock, null);
        session.Start();
        var starts = new List<TimeSpan>();
        DateTimeOffset origin = _clock.GetUtcNow();
        int seen = 0;
        Until(() =>
        {
            if (_host.Started > seen)
            {
                seen = _host.Started;
                starts.Add(_clock.GetUtcNow() - origin);
            }

            return seen >= 7;
        });
        session.RequestStop();
        IEnumerable<double> gaps = starts.Zip(starts.Skip(1), (first, second) => (second - first).TotalMilliseconds);
        gaps.Should().Equal([250, 500, 1000, 2000, 5000, 5000], (actual, expected) => Math.Abs(actual - expected) <= Step.TotalMilliseconds);
        session.State.Address.Should().BeNull();
        session.State.Error.Should().Contain("busy");
    }

    [Fact]
    public void DelayedReadinessPublishesTheAddressAndReportsIt()
    {
        _host.Fails("not yet");
        _ = _host.Serves(5001);
        var ready = new List<Uri>();
        IApplicationSession session = ApplicationSupervisor.Create(_host, _clock, address => ready.Add(address));
        session.Start();
        Until(() => session.State.Address is not null);
        session.State.Address.Should().Be(new Uri("http://127.0.0.1:5001/"));
        session.State.Error.Should().BeNull();
        ready.Should().ContainSingle();
        session.RequestStop();
        Until(() => session.Stopped.IsCompleted);
    }

    [Fact]
    public void AStoppedServerIsPublishedUnavailableTornDownAndReplaced()
    {
        FakeRunningApplication first = _host.Serves(5002);
        _ = _host.Serves(5003);
        IApplicationSession session = ApplicationSupervisor.Create(_host, _clock, null);
        session.Start();
        Until(() => session.State.Address is not null);
        long firstReady = session.State.Revision;
        first.Stop();
        Until(() => session.State.Address is null);
        session.State.Error.Should().Contain("stopped");
        Until(() => session.State.Address?.Port == 5003);
        first.Disposed.Should().BeTrue();
        session.State.Revision.Should().BeGreaterThan(firstReady + 1);
        session.RequestStop();
    }

    [Fact]
    public void ARestartAtTheSameAddressHasANewRevision()
    {
        FakeRunningApplication first = _host.Serves(5004);
        _ = _host.Serves(5004);
        IApplicationSession session = ApplicationSupervisor.Create(_host, _clock, null);
        session.Start();
        Until(() => session.State.Address is not null);
        ApplicationPageState before = session.State;
        first.StopAnswering();
        Until(() => session.State.Address is not null && session.State.Revision > before.Revision + 1);
        session.State.Address.Should().Be(before.Address);
        session.RequestStop();
    }

    [Fact]
    public void StoppingDuringRetryEndsWithoutAnotherAttemptOrReadyState()
    {
        _host.Fails("busy");
        _ = _host.Serves(5005);
        IApplicationSession session = ApplicationSupervisor.Create(_host, _clock, null);
        session.Start();
        Until(() => _host.Started == 1 && session.State.Error is not null);
        session.RequestStop();
        Until(() => session.Stopped.IsCompleted);
        _clock.Advance(TimeSpan.FromSeconds(30));
        _host.Started.Should().Be(1);
        session.State.Address.Should().BeNull();
        session.Stopped.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void StoppingDuringAStartCancelsIt()
    {
        _host.Hangs();
        IApplicationSession session = ApplicationSupervisor.Create(_host, _clock, null);
        session.Start();
        Until(() => _host.Started == 1);
        session.RequestStop();
        Until(() => session.Stopped.IsCompleted);
        session.Stopped.IsCompletedSuccessfully.Should().BeTrue();
        session.State.Address.Should().BeNull();
    }

    [Fact]
    public void AStartThatTakesLongerThanTenSecondsIsRetried()
    {
        _host.Hangs();
        _ = _host.Serves(5006);
        IApplicationSession session = ApplicationSupervisor.Create(_host, _clock, null);
        session.Start();
        Until(() => session.State.Address is not null);
        _host.Started.Should().Be(2);
        session.RequestStop();
    }

    [Fact]
    public void StopBeforeStartCompletesAndLaterStartDoesNothing()
    {
        IApplicationSession session = ApplicationSupervisor.Create(_host, _clock, null);
        session.RequestStop();
        session.RequestStop();
        session.Stopped.IsCompletedSuccessfully.Should().BeTrue();
        session.Start();
        _clock.Advance(TimeSpan.FromSeconds(5));
        _host.Started.Should().Be(0);
        session.StartupPage.Scheme.Should().Be("data");
    }

    [Fact]
    public void AServerThatCannotStopEndsTheSessionWithoutASecondServer()
    {
        FakeRunningApplication stuck = _host.Serves(5007, teardownHangs: true);
        _ = _host.Serves(5008);
        IApplicationSession session = ApplicationSupervisor.Create(_host, _clock, null);
        session.Start();
        Until(() => session.State.Address is not null);
        stuck.Stop();
        Until(() => session.Stopped.IsCompleted);
        session.Stopped.IsFaulted.Should().BeTrue();
        session.Stopped.Exception!.InnerException.Should().BeOfType<TimeoutException>();
        _host.Started.Should().Be(1);
        session.State.Error.Should().Contain("did not stop");
    }

    /// <summary>Advances fake time in small steps, letting the worker run between steps, until the condition holds.</summary>
    private void Until(Func<bool> condition)
    {
        for (int step = 0; step < 4000; step++)
        {
            if (SpinWait.SpinUntil(condition, 2))
            {
                return;
            }

            _clock.Advance(Step);
        }

        throw new TimeoutException("The supervisor did not reach the expected state.");
    }
}
