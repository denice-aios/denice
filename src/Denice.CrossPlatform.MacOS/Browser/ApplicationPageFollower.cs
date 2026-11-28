// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform.Browser;

/// <summary>
/// Keeps the one browser on the application session's current page: the bundled startup page while the server is
/// unavailable, the application page when a new ready revision appears, and back to the startup page with a retry when
/// the application page fails to load. Called on the desktop's main thread after every browser pump; it never waits.
/// </summary>
internal sealed class ApplicationPageFollower
{
    /// <summary>The text the startup page shows when it first loads, without a fragment.</summary>
    private const string StartingStatus = "DENICE is starting.";
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LoadDeadline = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FirstPageRetry = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LongestPageRetry = TimeSpan.FromSeconds(5);
    private readonly IApplicationSession _session;
    private readonly IPlatformBrowser _browser;
    private readonly TimeProvider _clock;
    private readonly long _created;
    private long _lastPoll = long.MinValue;
    private long _navigated;
    private long _startupFailing = -1;
    private long _retryAt = -1;
    private long _applicationRevision = -1;
    private TimeSpan _pageRetry = FirstPageRetry;
    private bool _onApplication;
    private bool _awaitingLoad = true;
    private string _status = StartingStatus;

    /// <summary>Initializes a new instance of the <see cref="ApplicationPageFollower"/> class.</summary>
    /// <param name="session">The application session.</param>
    /// <param name="browser">The browser, started on <see cref="IApplicationSession.StartupPage"/>.</param>
    /// <param name="clock">The host clock.</param>
    internal ApplicationPageFollower(IApplicationSession session, IPlatformBrowser browser, TimeProvider clock)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _created = clock.GetTimestamp();
        _navigated = _created;
    }

    /// <summary>Gets a value indicating whether the session has been started, after the first startup page frame.</summary>
    internal bool SessionStarted { get; private set; }

    /// <summary>Gets the host timestamp at which an application page first finished loading, or null.</summary>
    internal long? FirstApplicationReady { get; private set; }

    /// <summary>Gets a value indicating whether the browser currently shows a loaded application page.</summary>
    internal bool ShowingApplication => _onApplication && _browser.IsReady;

    /// <summary>Follows the session once; throws when the bundled startup page cannot be shown.</summary>
    /// <param name="hasFrame">Whether the desktop has presented a browser frame.</param>
    internal void Tick(bool hasFrame)
    {
        long now = _clock.GetTimestamp();
        if (!SessionStarted)
        {
            WaitForStartupPage(hasFrame, now);
            return;
        }

        if (_onApplication)
        {
            FollowApplicationPage(now);
        }
        else
        {
            RetryStartupPage(now);
        }

        if (_lastPoll != long.MinValue && _clock.GetElapsedTime(_lastPoll, now) < Poll)
        {
            return;
        }

        _lastPoll = now;
        ApplicationPageState state = _session.State;
        if (state.Address is null)
        {
            ShowStartupPage(state.Error ?? StartingStatus, now);
        }
        else if (state.Revision != _applicationRevision || (!_onApplication && _retryAt >= 0 && now >= _retryAt))
        {
            ShowApplicationPage(state, now);
        }
    }

    private void WaitForStartupPage(bool hasFrame, long now)
    {
        if (_browser.IsReady && hasFrame)
        {
            SessionStarted = true;
            _session.Start();
            return;
        }

        if (_clock.GetElapsedTime(_created, now) >= LoadDeadline)
        {
            string reason = _browser.LastLoadError ?? "no loaded frame arrived";
            throw new IOException("The bundled startup page could not be shown within fifteen seconds: " + reason);
        }

        RetryStartupPage(now);
    }

    private void RetryStartupPage(long now)
    {
        if (_browser.LastLoadError is null)
        {
            _startupFailing = -1;
            return;
        }

        if (_startupFailing < 0)
        {
            _startupFailing = now;
        }
        else if (_clock.GetElapsedTime(_startupFailing, now) >= LoadDeadline)
        {
            throw new IOException("The bundled startup page could not load: " + _browser.LastLoadError);
        }

        if (_clock.GetElapsedTime(_navigated, now) >= FirstPageRetry)
        {
            Navigate(StatusPage(_status), now);
        }
    }

    private void FollowApplicationPage(long now)
    {
        if (_browser.IsReady)
        {
            _awaitingLoad = false;
            _pageRetry = FirstPageRetry;
            FirstApplicationReady ??= now;
            return;
        }

        // The load deadline applies to DENICE's own navigation; a later navigation the page starts itself has none.
        string? failure = _browser.LastLoadError;
        if (failure is null && _awaitingLoad && _clock.GetElapsedTime(_navigated, now) >= LoadDeadline)
        {
            failure = "The application page did not load within fifteen seconds.";
        }

        if (failure is not null)
        {
            // Back to the startup page; the same ready revision is retried after the back-off.
            ShowStartupPage(failure, now);
            _retryAt = now + (long)(_pageRetry.TotalSeconds * _clock.TimestampFrequency);
            _pageRetry = TimeSpan.FromTicks(Math.Min(_pageRetry.Ticks * 2, LongestPageRetry.Ticks));
        }
    }

    private void ShowApplicationPage(ApplicationPageState state, long now)
    {
        if (state.Revision != _applicationRevision)
        {
            _pageRetry = FirstPageRetry;
        }

        _applicationRevision = state.Revision;
        _retryAt = -1;
        _onApplication = true;
        Navigate(state.Address!, now);
    }

    private void ShowStartupPage(string status, long now)
    {
        if (!_onApplication && string.Equals(status, _status, StringComparison.Ordinal))
        {
            return;
        }

        _onApplication = false;
        _status = status;
        Navigate(StatusPage(status), now);
    }

    private Uri StatusPage(string status) => new(_session.StartupPage.AbsoluteUri + "#" + Uri.EscapeDataString(status));

    private void Navigate(Uri url, long now)
    {
        _navigated = now;
        _awaitingLoad = true;
        _browser.Navigate(url);
    }
}
