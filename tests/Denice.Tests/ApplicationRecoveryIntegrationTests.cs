// Copyright (c) Denice.ai. All rights reserved.

using System.Threading.Channels;
using Denice.Abstractions.Contracts;
using Denice.Engine;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// The supervisor driving real loopback <see cref="ApplicationServer"/> attempts: a real server stopped underneath it is
/// replaced at a new revision, and a stop during start, service or retry leaves no server listening. Time advances
/// only by the delays the supervisor itself requests (<see cref="ObservedClock"/>).
/// </summary>
public sealed class ApplicationRecoveryIntegrationTests : IAsyncLifetime
{
    private static readonly TimeSpan TenSecondLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FirstRetry = TimeSpan.FromMilliseconds(250);
    private readonly FakeTimeProvider _clock = new();
    private readonly Channel<TimeSpan> _timers = Channel.CreateUnbounded<TimeSpan>();
    private readonly Channel<Uri> _ready = Channel.CreateUnbounded<Uri>();
    private readonly RealApplicationHost _host = RealApplicationHost.Create();
    private IApplicationSession? _session;

    /// <inheritdoc />
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        _session?.RequestStop();
        await _host.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task ARealServerStoppedUnderneathIsReplacedByANewRealServerAtANewRevision()
    {
        IApplicationSession session = StartSession();
        Uri first = await NextReadyAsync();
        long ready = session.State.Revision;
        (await AnswersHealthAsync(first)).Should().BeTrue();

        await StopFirstServerUnderneathAsync(first);
        session.State.Address.Should().BeNull();
        session.State.Error.Should().Be("The DENICE server stopped. Restarting it.");
        long unavailable = session.State.Revision;
        unavailable.Should().BeGreaterThan(ready);
        _host.Servers[0].DisposedBySupervisor.Should().BeTrue();

        _clock.Advance(FirstRetry);
        Uri second = await NextReadyAsync();
        session.State.Should().BeEquivalentTo(new { Address = second, Error = (string?)null });
        session.State.Revision.Should().BeGreaterThan(unavailable);
        (await AnswersHealthAsync(second)).Should().BeTrue();
        (await AnswersHealthAsync(first)).Should().BeFalse();
        _host.Started.Should().Be(2);
    }

    [Fact]
    public async Task StoppingDuringTheRetryAfterARealServerStopsStartsNothingAndLeavesNoListener()
    {
        IApplicationSession session = StartSession();
        Uri first = await NextReadyAsync();
        await StopFirstServerUnderneathAsync(first);

        session.RequestStop();
        await session.Stopped;
        _clock.Advance(FirstRetry);

        _host.Started.Should().Be(1);
        session.State.Address.Should().BeNull();
        _ready.Reader.TryRead(out _).Should().BeFalse();
        (await AnswersHealthAsync(first)).Should().BeFalse();
    }

    [Fact]
    public async Task StoppingARunningRealServerSessionReleasesItsListener()
    {
        IApplicationSession session = StartSession();
        Uri first = await NextReadyAsync();
        await NextTimerAsync(ProbeInterval);

        session.RequestStop();
        await session.Stopped;

        _host.Servers.Should().ContainSingle().Which.DisposedBySupervisor.Should().BeTrue();
        (await AnswersHealthAsync(first)).Should().BeFalse();
    }

    [Fact]
    public async Task StoppingWhileARealServerStartsLeavesNoServerListening()
    {
        IApplicationSession session = StartSession();
        session.RequestStop();
        await session.Stopped;

        _host.Started.Should().BeLessThanOrEqualTo(1);
        foreach (RealApplicationHost.Attempt attempt in _host.Servers)
        {
            attempt.DisposedBySupervisor.Should().BeTrue();
            (await AnswersHealthAsync(attempt.Address)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task ARealProbeWhoseDeadlineHasPassedReportsNotReadyAndACallerStopThrows()
    {
        IRunningApplication server = await new ApplicationServer.Host(new FakeTimeProvider()).StartAsync(CancellationToken.None);
        try
        {
            (await server.ProbeAsync(TimeSpan.Zero, CancellationToken.None)).Should().BeFalse();
            using var stopped = new CancellationTokenSource();
            await stopped.CancelAsync();
            Func<Task> probe = () => server.ProbeAsync(TimeSpan.FromSeconds(5), stopped.Token);
            await probe.Should().ThrowAsync<OperationCanceledException>();
            (await server.ProbeAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).Should().BeTrue();
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    /// <summary>Asks an address's health endpoint directly, so a closed listener shows as not answering.</summary>
    private static async Task<bool> AnswersHealthAsync(Uri address)
    {
        try
        {
            using var client = new HttpClient();
            using HttpResponseMessage response = await client.GetAsync(new Uri(address, "/health")).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                && string.Equals(await response.Content.ReadAsStringAsync().ConfigureAwait(false), "ready", StringComparison.Ordinal);
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private IApplicationSession StartSession()
    {
        _session = ApplicationSupervisor.Create(_host, ObservedClock.Create(_clock, _timers.Writer), address => _ready.Writer.TryWrite(address));
        _session.Start();
        return _session;
    }

    private Task<Uri> NextReadyAsync() => WhileSupervisingAsync(_ready.Reader.ReadAsync().AsTask());

    /// <summary>Waits for <paramref name="next"/>, failing at once if the supervisor ends first.</summary>
    private async Task<T> WhileSupervisingAsync<T>(Task<T> next)
    {
        Task first = await Task.WhenAny(next, _session!.Stopped).ConfigureAwait(false);
        first.Should().BeSameAs(next, "the supervisor ended early: {0}", _session.Stopped.Exception);
        return await next.ConfigureAwait(false);
    }

    /// <summary>Stops the first real server once the supervisor watches it, and waits until the supervisor is in its first retry delay.</summary>
    private async Task StopFirstServerUnderneathAsync(Uri first)
    {
        await NextTimerAsync(ProbeInterval).ConfigureAwait(false);
        await _host.Servers[0].StopUnderneathAsync().ConfigureAwait(false);
        (await AnswersHealthAsync(first).ConfigureAwait(false)).Should().BeFalse();
        _clock.Advance(ProbeInterval);
        await NextTimerAsync(FirstRetry).ConfigureAwait(false);
    }

    /// <summary>Reads the supervisor's timer requests up to the expected one; only the ten-second start and teardown limits may come first.</summary>
    private async Task NextTimerAsync(TimeSpan expected)
    {
        while (true)
        {
            TimeSpan due = await WhileSupervisingAsync(_timers.Reader.ReadAsync().AsTask()).ConfigureAwait(false);
            if (due == expected)
            {
                return;
            }

            due.Should().Be(TenSecondLimit, "only the start and teardown limits may precede the {0} delay", expected);
        }
    }
}
