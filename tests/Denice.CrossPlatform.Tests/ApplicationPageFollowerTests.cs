// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Browser;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>How every desktop keeps its one browser on the application session's current page.</summary>
public sealed class ApplicationPageFollowerTests
{
    private static readonly Uri Application = new("http://127.0.0.1:5000/");
    private readonly FakeTimeProvider clock = new();
    private readonly RecoveryScript script = new();
    private readonly ApplicationPageFollower follower;

    public ApplicationPageFollowerTests()
    {
        follower = new ApplicationPageFollower(script.Session(), script.Browser(), clock);
    }

    [Fact]
    public void TheSessionStartsOnlyAfterTheStartupPageHasLoadedAFrame()
    {
        follower.Tick(hasFrame: false);
        script.Ready = true;
        follower.Tick(hasFrame: false);
        script.Starts.Should().Be(0);
        follower.Tick(hasFrame: true);
        follower.Tick(hasFrame: true);
        script.Starts.Should().Be(1);
        follower.SessionStarted.Should().BeTrue();
    }

    [Fact]
    public void AStartupPageThatNeverLoadsStopsStartupAfterFifteenSeconds()
    {
        follower.Tick(hasFrame: false);
        clock.Advance(TimeSpan.FromSeconds(15));
        Action tick = () => follower.Tick(hasFrame: false);
        tick.Should().Throw<IOException>().WithMessage("*startup page*fifteen seconds*");
    }

    [Fact]
    public void AFailingStartupPageIsRetriedEverySecondThenGivenUp()
    {
        StartSession();
        script.LoadError = "boom";
        follower.Tick(hasFrame: true);
        script.Navigations.Should().BeEmpty();
        clock.Advance(TimeSpan.FromSeconds(1));
        follower.Tick(hasFrame: true);
        script.Navigations.Should().ContainSingle().Which.AbsoluteUri.Should().StartWith(RecoveryScript.StartupAddress.AbsoluteUri);
        script.LoadError = "boom";
        clock.Advance(TimeSpan.FromSeconds(15));
        Action tick = () => follower.Tick(hasFrame: true);
        tick.Should().Throw<IOException>().WithMessage("*bundled startup page could not load*boom*");
    }

    [Fact]
    public void AReadyRevisionOpensTheApplicationPageAndStartsTheDuration()
    {
        StartSession();
        script.State = new ApplicationPageState(2, Application, null);
        Poll();
        script.Last.Should().Be(Application);
        follower.FirstApplicationReady.Should().BeNull();
        script.Ready = true;
        long readyAt = clock.GetTimestamp();
        follower.Tick(hasFrame: true);
        follower.FirstApplicationReady.Should().Be(readyAt);
        follower.ShowingApplication.Should().BeTrue();
    }

    [Fact]
    public void AnUnavailableServerShowsTheStartupPageWithItsReasonAsText()
    {
        OpenApplication();
        script.State = new ApplicationPageState(3, null, "The DENICE server stopped. Restarting it.");
        Poll();
        script.Last.AbsoluteUri.Should().Be(RecoveryScript.StartupAddress.AbsoluteUri + "#" + Uri.EscapeDataString("The DENICE server stopped. Restarting it."));
        follower.ShowingApplication.Should().BeFalse();
        int navigations = script.Navigations.Count;
        Poll();
        script.Navigations.Should().HaveCount(navigations, "the same status is not reloaded");
    }

    [Fact]
    public void AFailedApplicationLoadReturnsToTheStartupPageAndRetriesWithBackOff()
    {
        OpenApplication();
        var retries = new List<double>();
        for (int failure = 0; failure < 5; failure++)
        {
            script.Ready = false;
            script.LoadError = "The application page returned HTTP 404.";
            follower.Tick(hasFrame: true);
            script.Last.Fragment.Should().Contain("404");
            long failedAt = clock.GetTimestamp();
            while (script.Last != Application)
            {
                clock.Advance(TimeSpan.FromMilliseconds(100));
                follower.Tick(hasFrame: true);
            }

            retries.Add(clock.GetElapsedTime(failedAt).TotalSeconds);
        }

        retries.Should().Equal([1, 2, 4, 5, 5], (actual, expected) => Math.Abs(actual - expected) <= 0.11);
    }

    [Fact]
    public void AnApplicationPageThatDoesNotLoadInFifteenSecondsIsTreatedAsFailed()
    {
        StartSession();
        script.State = new ApplicationPageState(2, Application, null);
        Poll();
        clock.Advance(TimeSpan.FromSeconds(15));
        follower.Tick(hasFrame: true);
        script.Last.Fragment.Should().Contain("fifteen");
    }

    [Fact]
    public void ANavigationThePageStartsItselfHasNoLoadDeadline()
    {
        OpenApplication();
        int navigations = script.Navigations.Count;
        script.Ready = false;
        clock.Advance(TimeSpan.FromSeconds(20));
        follower.Tick(hasFrame: true);
        script.Navigations.Should().HaveCount(navigations);
        script.LoadError = "The application page returned HTTP 404.";
        follower.Tick(hasFrame: true);
        script.Last.Fragment.Should().Contain("404");
    }

    [Fact]
    public void ARestartAtTheSameAddressReloadsTheApplicationPage()
    {
        OpenApplication();
        int navigations = script.Navigations.Count;
        script.State = new ApplicationPageState(9, Application, null);
        Poll();
        script.Navigations.Should().HaveCount(navigations + 1);
        script.Last.Should().Be(Application);
    }

    [Fact]
    public void TheSessionIsReadAtMostEveryHundredMilliseconds()
    {
        OpenApplication();
        int navigations = script.Navigations.Count;
        script.State = new ApplicationPageState(10, null, "down");
        clock.Advance(TimeSpan.FromMilliseconds(50));
        follower.Tick(hasFrame: true);
        script.Navigations.Should().HaveCount(navigations);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        follower.Tick(hasFrame: true);
        script.Navigations.Should().HaveCount(navigations + 1);
    }

    private void StartSession()
    {
        script.Ready = true;
        follower.Tick(hasFrame: true);
        script.Ready = false;
    }

    private void OpenApplication()
    {
        StartSession();
        script.State = new ApplicationPageState(2, Application, null);
        Poll();
        script.Ready = true;
        follower.Tick(hasFrame: true);
    }

    private void Poll()
    {
        clock.Advance(TimeSpan.FromMilliseconds(100));
        follower.Tick(hasFrame: true);
    }
}
