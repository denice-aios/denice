// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using Denice.Tests.Shared;
using Denice.Updates;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Denice.Runtime.Tests;

/// <summary>
/// The long-running host (D6) through <c>Program.Run</c>, with a long-running command only these tests register (Denice
/// ships none yet): it runs alongside the update checks of the production composition, its own exit code wins when it
/// ends first, the session ends with the restart exit code when a newer version is ready, and a one-shot command is
/// unchanged (D5). The disk is simulated, the home is a temporary folder (never the user's), the clock is fake, and no run
/// has a loader, so no check reaches the network; the checks themselves against a fake GitHub, through to an installed
/// newer version, are proven in Denice.Tests (UpdateServiceTests, TheHost_*).
/// </summary>
public sealed class LongRunningCliTests
{
    private const string CommandName = "test-long-running";

    private static readonly TimeSpan SixHours = TimeSpan.FromHours(6);

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
    private readonly Channel<TimeSpan> timers = Channel.CreateUnbounded<TimeSpan>();
    private readonly RecordingConsole console = new();
    private readonly LongRunningTestCommand command = new();
    private readonly ISystemServices services;

    /// <summary>Initializes a new instance of the <see cref="LongRunningCliTests"/> class.</summary>
    public LongRunningCliTests()
    {
        ISystemServices disk = SystemServicesBuilder.Real().SimulateFileSystem().Build();
        string home = disk.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-long-running-");
        services = SystemServicesBuilder.Real()
            .SimulateFileSystem()
            .With(FakeEnvironment.Create(
                home: home,
                variables: new Dictionary<string, string>(StringComparer.Ordinal) { ["LOCALAPPDATA"] = home }))
            .With(new ObservedClock(clock, timers.Writer))
            .With(console.Console)
            .Build();
        services.FileSystem.GetDirectoryWriter().CreateDirectory(home);
    }

    [Fact]
    public async Task ALongRunningCommand_RunsOnThroughTheIntervalChecks()
    {
        Task<int> run = Run(host: null);
        await command.Started;

        for (int interval = 0; interval < 3; interval++)
        {
            (await NextTimerAsync(run)).Should().Be(SixHours);
            clock.Advance(SixHours);
        }

        (await NextTimerAsync(run)).Should().Be(SixHours, "each check found nothing to restart into, so the checks go on");
        command.IsRunning.Should().BeTrue();
        command.Finish(0);
        (await run).Should().Be(0);
    }

    [Fact]
    public async Task ALongRunningCommand_EndingOnItsOwn_ExitsWithItsCode_AndTheChecksStop()
    {
        Task<int> run = Run(host: null);
        (await NextTimerAsync(run)).Should().Be(SixHours);

        command.Finish(7);

        (await run).Should().Be(7);
        command.WasStopped.Should().BeFalse();
        clock.Advance(SixHours);
        (await NextTimerAsync(Task.CompletedTask)).Should().BeNull("no check waits once the command has ended");
    }

    [Fact]
    public async Task ALongRunningCommand_IsStoppedWhenANewerVersionIsReady_AndTheSessionExitsWithTheRestartCode()
    {
        Task<int> run = Run(_ => new LongRunningHost(async ct =>
        {
            await command.Started.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return new NewerVersionReady("0.1.300-pre-release+ccccccc");
        }));

        (await run).Should().Be(LoaderProtocol.ExitRestartIntoNewerVersion).And.Be(75);
        command.WasStopped.Should().BeTrue();
    }

    [Fact]
    public async Task AOneShotCommand_IsUnchanged_AndNeverRunsTheHost()
    {
        bool hosted = false;
        var program = new Program(["version"], [TestCommand()], _ =>
        {
            hosted = true;
            return new LongRunningHost(_ => Task.FromResult<NewerVersionReady?>(new NewerVersionReady("0.1.300-pre-release+ccccccc")));
        });

        int exitCode = await program.Run(services);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("SemVer:");
        hosted.Should().BeFalse();
        command.Started.IsCompleted.Should().BeFalse();
    }

    private Task<int> Run(Func<ISystemServices, LongRunningHost>? host) =>
        new Program([CommandName], [TestCommand()], host).Run(services);

    private LongRunningCommand TestCommand() =>
        new(new Command(CommandName, "Test only: runs until the test ends it."), (_, ct) => command.RunAsync(ct));

    // The due time of the next timer on the clock (the update loop's wait for the next check, now on the fake clock so
    // moving the clock on reaches it), or null when the run ends instead, or no timer follows a finished run.
    private async Task<TimeSpan?> NextTimerAsync(Task run)
    {
        if (run.IsCompleted)
        {
            return timers.Reader.TryRead(out TimeSpan pending) ? pending : null;
        }

        Task<TimeSpan> timer = timers.Reader.ReadAsync().AsTask();
        return await Task.WhenAny(timer, run).ConfigureAwait(false) == timer ? await timer.ConfigureAwait(false) : null;
    }

    // The fake clock, telling the test each time a timer is put on it.
    private sealed class ObservedClock(FakeTimeProvider inner, ChannelWriter<TimeSpan> timers) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

        public override long TimestampFrequency => inner.TimestampFrequency;

        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

        public override long GetTimestamp() => inner.GetTimestamp();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer timer = inner.CreateTimer(callback, state, dueTime, period);
            timers.TryWrite(dueTime);
            return timer;
        }
    }
}
