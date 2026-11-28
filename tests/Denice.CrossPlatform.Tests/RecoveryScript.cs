// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// Drives a scripted browser and application session for page-follower tests: the test sets the session state and the
/// browser's load result, and reads back every navigation and whether the session was started.
/// </summary>
internal sealed class RecoveryScript
{
    /// <summary>The scripted startup page.</summary>
    internal static readonly Uri StartupAddress = new("data:text/html,startup");

    /// <summary>Gets every URL the browser was asked to navigate to, in order.</summary>
    internal List<Uri> Navigations { get; } = [];

    /// <summary>Gets or sets the session's current state.</summary>
    internal ApplicationPageState State { get; set; } = new(0, null, null);

    /// <summary>Gets or sets a value indicating whether the current navigation has loaded.</summary>
    internal bool Ready { get; set; }

    /// <summary>Gets or sets the current navigation's load failure.</summary>
    internal string? LoadError { get; set; }

    /// <summary>Gets or sets the number of times the session was started.</summary>
    internal int Starts { get; set; }

    /// <summary>Gets the most recent navigation.</summary>
    internal Uri Last => Navigations[^1];

    /// <summary>Creates the scripted browser.</summary>
    /// <returns>The browser.</returns>
    internal IPlatformBrowser Browser() => ScriptedBrowser.Create(this);

    /// <summary>Creates the scripted session.</summary>
    /// <returns>The session.</returns>
    internal IApplicationSession Session() => ScriptedSession.Create(this);

    /// <summary>A browser whose load results come from the script.</summary>
    private sealed class ScriptedBrowser : IPlatformBrowser
    {
        private readonly RecoveryScript script;

        private ScriptedBrowser(RecoveryScript script)
        {
            this.script = script;
        }

        public bool IsClosed => false;

        public bool IsReady => script.Ready;

        public string? LastLoadError => script.LoadError;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Contract fakes are handed back through their interface (DN0004).")]
        public static IPlatformBrowser Create(RecoveryScript script) => new ScriptedBrowser(script);

        public void Navigate(Uri url)
        {
            script.Navigations.Add(url);
            script.Ready = false;
            script.LoadError = null;
        }

        public void Start(Uri url, int width, int height, IPlatformBrowserFrameSink frames, IPlatformBrowserAudioSink audio, string cachePath, int debugPort) => throw new NotSupportedException();

        public void Pump()
        {
            // Nothing to pump.
        }

        public void Resize(int width, int height)
        {
            // Not used by the follower.
        }

        public void RequestClose()
        {
            // Not used by the follower.
        }

        public void Shutdown()
        {
            // Not used by the follower.
        }

        public void MouseMove(int x, int y, bool leave)
        {
            // Not used by the follower.
        }

        public void MouseClick(int x, int y, int button, bool mouseUp, int clickCount)
        {
            // Not used by the follower.
        }

        public void MouseWheel(int x, int y, int deltaX, int deltaY)
        {
            // Not used by the follower.
        }
    }

    /// <summary>A session whose state comes from the script.</summary>
    private sealed class ScriptedSession : IApplicationSession
    {
        private readonly RecoveryScript script;

        private ScriptedSession(RecoveryScript script)
        {
            this.script = script;
        }

        public Uri StartupPage => StartupAddress;

        public ApplicationPageState State => script.State;

        public Task Stopped => Task.CompletedTask;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Contract fakes are handed back through their interface (DN0004).")]
        public static IApplicationSession Create(RecoveryScript script) => new ScriptedSession(script);

        public void Start() => script.Starts++;

        public void RequestStop()
        {
            // Nothing to stop.
        }
    }
}
