// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Browser;
using Denice.CrossPlatform.MacOS.Browser;
using Denice.CrossPlatform.MacOS.Desktop;
using Xunit;
using static Denice.CrossPlatform.Tests.MixerHarness;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// Runs real CEF with the integrated browser-audio redirection on the fixture child's main thread, silently: the mixer's
/// output unit is never started, so nothing reaches any device; the fixture pulls the mixer itself as a device would.
/// A page tone, page speech rendered by AVFoundation, and a second tone started from the speech end event must all reach
/// the mixer, and CEF shutdown must leave no Chromium audio unit open.
/// </summary>
internal static class BrowserAudioFixture
{
    private const int Frames = 256;
    private const string Html = "<html><body style='margin:0'><button style='width:150px;height:80px' onclick='go()'>Audio</button><script>"
        + "function tone(f){const c=new AudioContext({sampleRate:48000});const o=c.createOscillator();const g=c.createGain();g.gain.value=0.25;o.frequency.value=f;o.connect(g).connect(c.destination);o.start();o.stop(c.currentTime+0.4);o.onended=()=>setTimeout(()=>c.close(),200);}"
        + "function go(){tone(440);const u=new SpeechSynthesisUtterance('fixture speech');u.onend=()=>tone(880);setTimeout(()=>speechSynthesis.speak(u),800);}"
        + "</script></body></html>";

    /// <summary>Runs the scenario; throws on any failed expectation.</summary>
    /// <param name="services">The real system services.</param>
    /// <param name="directory">The fixture's working directory.</param>
    /// <param name="stage">Writes a progress marker.</param>
    internal static void Run(ISystemServices services, string directory, Action<string> stage)
    {
        TimeProvider clock = services.Clock;
        nint app = MacApplication.Create();
        using var mixer = BrowserAudioMixer.Create();
        mixer.UseClock(clock);
        var log = new SinkLog { SpeechProgress = () => mixer.SpeechFramesMixed };
        IPlatformBrowserAudioSink sink = LoggingSink.Create(log);
        IPlatformBrowser browser = CefPlatformBrowser.Create(mixer);
        var frames = new FrameCount();
        using var device = new CancellationTokenSource();
        Task pulling = Task.Run(() => PullLikeADevice(mixer, clock, device.Token));
        string cache = Path.Combine(directory, "browser-" + services.Random.NewGuid().ToString("N"));
        try
        {
            browser.Start(new Uri("data:text/html," + Uri.EscapeDataString(Html)), 400, 200, FrameCounter.Create(frames), sink, cache, 0);
            stage("browser started with the audio redirection installed");
            ClickUntilTone(app, browser, clock, frames, log, stage);

            PumpUntil(app, browser, clock, TimeSpan.FromSeconds(30), () => log.Tones().Count >= 2);
            stage($"tones heard by the mixer: {string.Join(", ", log.Tones().Select(tone => $"{tone.Hertz:F0} Hz after {tone.SpeechFramesBefore} speech frames"))}; speech mixed {mixer.SpeechFramesMixed} frames");
        }
        finally
        {
            browser.RequestClose();
            PumpUntil(app, browser, clock, TimeSpan.FromSeconds(10), () => browser.IsClosed);
            Assert.True(browser.IsClosed, "CEF must complete browser closure before shutdown.");
            browser.Shutdown();
            device.Cancel();
            pulling.GetAwaiter().GetResult();
        }

        IReadOnlyList<SinkLog.Tone> tones = log.Tones();
        Assert.True(tones.Count >= 2, $"Expected the first tone and the tone started by the speech end event; saw {tones.Count}.");
        Assert.InRange(tones[0].Hertz, 400, 480);
        Assert.True(mixer.SpeechFramesMixed > BrowserAudioMixer.SampleRate / 4, $"Page speech must reach the mixer; mixed {mixer.SpeechFramesMixed} frames.");
        Assert.InRange(tones[^1].Hertz, 820, 940);
        Assert.True(tones[^1].SpeechFramesBefore > 0, "The second tone must follow the speech the page waited for.");
        Assert.Equal(0, AudioOutputRedirection.TrackedUnits);
        Assert.Contains("real starts of CEF units=0", AudioOutputRedirection.Report, StringComparison.Ordinal);
        Assert.Null(mixer.Failure);
        Assert.Empty(log.Errors);
        Assert.True(log.Stopped, "Shutdown must end the audio stream.");
        stage("browser audio verified: " + AudioOutputRedirection.Report + "; " + SpeechRedirection.Report);
    }

    private static void ClickUntilTone(nint app, IPlatformBrowser browser, TimeProvider clock, FrameCount frames, SinkLog log, Action<string> stage)
    {
        // A click before the page has painted hits nothing; wait for rendered frames, and click again if no tone follows.
        PumpUntil(app, browser, clock, TimeSpan.FromSeconds(30), () => browser.IsReady && frames.Value >= 1);
        Assert.True(browser.IsReady && frames.Value >= 1, $"The fixture page did not load and paint (ready {browser.IsReady}, frames {frames.Value}).");
        long painted = clock.GetTimestamp();
        PumpUntil(app, browser, clock, TimeSpan.FromMilliseconds(500), () => clock.GetElapsedTime(painted) >= TimeSpan.FromMilliseconds(500));
        for (int attempt = 1; attempt <= 3 && log.Tones().Count == 0; attempt++)
        {
            browser.MouseMove(40, 40, false);
            browser.MouseClick(40, 40, 0, false, 1);
            browser.MouseClick(40, 40, 0, true, 1);
            stage($"button clicked (attempt {attempt}, {frames.Value} frames painted)");
            PumpUntil(app, browser, clock, TimeSpan.FromSeconds(4), () => log.Tones().Count > 0);
        }
    }

    private static void PullLikeADevice(BrowserAudioMixer mixer, TimeProvider clock, CancellationToken stop)
    {
        // Stands in for the output device: one 256-frame render cycle every few milliseconds, never reaching any hardware.
        var buffers = new RenderBuffers(Frames);
        while (!stop.IsCancellationRequested)
        {
            buffers.Render(mixer);
            try
            {
                Task.Delay(TimeSpan.FromMilliseconds(5), clock, stop).GetAwaiter().GetResult();
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private static void PumpUntil(nint app, IPlatformBrowser browser, TimeProvider clock, TimeSpan limit, Func<bool> done)
    {
        long start = clock.GetTimestamp();
        while (!done() && clock.GetElapsedTime(start) < limit)
        {
            // The main dispatch queue, where AVFoundation delivers rendered speech, drains while the run loop runs.
            nint pool = MacNative.Call(MacNative.Class("NSAutoreleasePool"), "new");
            try
            {
                nint evt = MacApplication.NextEvent(app);
                if (evt != 0)
                {
                    _ = MacNative.Call(app, "sendEvent:", evt);
                }

                browser.Pump();
            }
            finally
            {
                MacNative.Release(pool);
            }

            Task.Delay(TimeSpan.FromMilliseconds(2), clock).GetAwaiter().GetResult();
        }
    }

    /// <summary>Counts accelerated frames without drawing them; this scenario needs only the browser running.</summary>
    private sealed class FrameCounter : IPlatformBrowserFrameSink
    {
        private readonly FrameCount _count;

        private FrameCounter(FrameCount count) => _count = count;

        public void OnAcceleratedFrame(scoped in BrowserGpuBuffer buffer, int format, int width, int height, int visibleX, int visibleY, int visibleWidth, int visibleHeight, bool popup) => _count.Value++;

        public void OnPopup(bool visible, int x, int y, int width, int height)
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Contract fakes are created behind their interface (DN0003, DN0006).")]
        internal static IPlatformBrowserFrameSink Create(FrameCount count) => new FrameCounter(count);
    }

    /// <summary>The number of accelerated frames painted so far.</summary>
    private sealed class FrameCount
    {
        internal int Value { get; set; }
    }
}
