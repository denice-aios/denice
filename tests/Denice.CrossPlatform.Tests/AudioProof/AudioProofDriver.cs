// Copyright (c) Denice.ai. All rights reserved.

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Browser;
using static Denice.CrossPlatform.Tests.MixerHarness;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// Drives the shared case table of the Windows and Linux browser-audio redirection proofs on the thread that owns CEF:
/// it serves <see cref="AudioProofPage"/> on two loopback sites, runs every case through the DevTools protocol while
/// pumping the browser, closes the browser right after the final ramp and finds every ramp in the mixer's drained
/// output. The OS fixture owns the mixer, its output and the redirection; this driver only uses the browser contract.
/// </summary>
internal static class AudioProofDriver
{
    private static readonly TimeSpan CaseLimit = TimeSpan.FromSeconds(60);

    /// <summary>Runs every case and returns the evidence.</summary>
    /// <param name="services">The real system services.</param>
    /// <param name="browser">The browser created with the proof's mixer.</param>
    /// <param name="core">The started mixer core.</param>
    /// <param name="cache">The browser cache directory.</param>
    /// <param name="explicitDevice">The label of the output device the explicit-device case selects.</param>
    /// <param name="stage">Writes a progress marker.</param>
    /// <param name="started">Runs once the page has loaded, before the first case (the OS fixture adds always-on sources here,
    /// once the browser drains the mixer).</param>
    /// <returns>The evidence.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The attached session is disposed by the using declaration once the awaited task completes.")]
    internal static Evidence Run(ISystemServices services, IPlatformBrowser browser, MixerCore core, string cache, string explicitDevice, Action<string> stage, Action? started = null)
    {
        TimeProvider clock = services.Clock;
        var evidence = new Evidence();
        evidence.Log.SpeechProgress = () => core.SpeechFramesMixed;
        using LoopbackPageServer server = LoopbackPageServer.Start(AudioProofPage.Html);
        int port = FreePort();
        stage("starting the browser at " + server.First);
        browser.Start(server.First, 400, 200, CountingFrames.Create(), LoggingSink.Create(evidence.Log), cache, port);
        stage($"browser started, DevTools port {port}");
        try
        {
            PumpUntil(browser, clock, TimeSpan.FromSeconds(30), () => browser.IsReady, evidence);
            evidence.Results["ready"] = browser.IsReady ? "true" : "false";
            started?.Invoke();
            Task<DevToolsSession> attach = DevToolsSession.AttachAsync(port, clock);
            using DevToolsSession session = Await(browser, clock, attach, evidence);
            MeasurePageClock(browser, clock, session, evidence);
            RunCases(browser, clock, session, server, explicitDevice, evidence, stage);
        }
        finally
        {
            // Right after the final ramp: close and shut down with the mixer running.
            browser.RequestClose();
            PumpUntil(browser, clock, TimeSpan.FromSeconds(15), () => browser.IsClosed, evidence);
            evidence.Results["closed"] = browser.IsClosed ? "true" : "false";
            browser.Shutdown();
            stage("CEF shut down");
        }

        evidence.Left = [.. evidence.Log.Packets.SelectMany(packet => packet)];
        evidence.Right = [.. evidence.Log.RightPackets.SelectMany(packet => packet)];
        return evidence;
    }

    private static void RunCases(IPlatformBrowser browser, TimeProvider clock, DevToolsSession session, LoopbackPageServer server, string explicitDevice, Evidence evidence, Action<string> stage)
    {
        string[] first = ["midi", "cold", "silenceSame", "silenceNew", "concurrent", "devices", $"explicit({JsonValue.Create(explicitDevice).ToJsonString()})", "speech"];
        foreach (string call in first)
        {
            Case(browser, clock, session, call, evidence, stage);
        }

        Await(browser, clock, session.NavigateAsync(server.Second, CaseLimit), evidence);
        stage("navigated cross-site to " + server.Second);
        Case(browser, clock, session, "navCold", evidence, stage);
        Case(browser, clock, session, "final", evidence, stage);
    }

    private static void MeasurePageClock(IPlatformBrowser browser, TimeProvider clock, DevToolsSession session, Evidence evidence)
    {
        // The page's performance.now() against the host clock the mixer stamps packets with, to the evaluation's round trip.
        double before = HostMilliseconds(clock);
        string page = Await(browser, clock, session.EvaluateAsync("performance.now() - 0", CaseLimit), evidence);
        double after = HostMilliseconds(clock);
        evidence.PageOffsetMilliseconds = ((before + after) / 2) - double.Parse(page, CultureInfo.InvariantCulture);
        evidence.PageOffsetUncertaintyMilliseconds = (after - before) / 2;
    }

    private static double HostMilliseconds(TimeProvider clock) => clock.GetTimestamp() * (1000.0 / clock.TimestampFrequency);

    private static void Case(IPlatformBrowser browser, TimeProvider clock, DevToolsSession session, string call, Evidence evidence, Action<string> stage)
    {
        string name = call.Split('(')[0];
        string expression = call.Contains('(', StringComparison.Ordinal) ? $"cases.{call}" : $"cases.{call}()";
        long started = clock.GetTimestamp();
        string result = Await(browser, clock, session.EvaluateAsync(expression, CaseLimit), evidence);
        evidence.Results[name] = result;
        stage(string.Create(CultureInfo.InvariantCulture, $"case {name} ({clock.GetElapsedTime(started).TotalSeconds:F1} s): {(result.Length > 600 ? result[..600] + "…" : result)}"));
    }

    private static T Await<T>(IPlatformBrowser browser, TimeProvider clock, Task<T> task, Evidence evidence)
    {
        PumpUntil(browser, clock, CaseLimit + TimeSpan.FromSeconds(5), () => task.IsCompleted, evidence);
        return task.IsCompleted ? task.GetAwaiter().GetResult() : throw new TimeoutException("A proof case did not complete.");
    }

    private static void Await(IPlatformBrowser browser, TimeProvider clock, Task task, Evidence evidence)
    {
        PumpUntil(browser, clock, CaseLimit + TimeSpan.FromSeconds(5), () => task.IsCompleted, evidence);
        if (!task.IsCompleted)
        {
            throw new TimeoutException("A proof step did not complete.");
        }

        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(IPlatformBrowser browser, TimeProvider clock, TimeSpan limit, Func<bool> done, Evidence evidence)
    {
        long start = clock.GetTimestamp();
        while (!done() && clock.GetElapsedTime(start) < limit)
        {
            try
            {
                browser.Pump();
            }
            catch (InvalidOperationException error) when (error.Message.Contains("CPU pixels", StringComparison.Ordinal))
            {
                // No GPU in the proof host: CEF paints in software. Frames are not needed for the audio proof.
                evidence.CpuPaints++;
            }

            Task.Delay(TimeSpan.FromMilliseconds(2), clock).GetAwaiter().GetResult();
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        probe.Dispose();
        return port;
    }

    /// <summary>What one proof run produced.</summary>
    internal sealed class Evidence
    {
        /// <summary>Gets the mixed audio the sink received.</summary>
        internal SinkLog Log { get; } = new();

        /// <summary>Gets each case's JSON result.</summary>
        internal SortedDictionary<string, string> Results { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets or sets the mixed left channel.</summary>
        internal float[] Left { get; set; } = [];

        /// <summary>Gets or sets the mixed right channel.</summary>
        internal float[] Right { get; set; } = [];

        /// <summary>Gets or sets host milliseconds minus page milliseconds (performance.now).</summary>
        internal double PageOffsetMilliseconds { get; set; }

        /// <summary>Gets or sets half the round trip of the clock measurement.</summary>
        internal double PageOffsetUncertaintyMilliseconds { get; set; }

        /// <summary>Gets or sets the software paints CEF reported.</summary>
        internal int CpuPaints { get; set; }
    }

    /// <summary>Counts nothing and draws nothing: the audio proof needs no frames.</summary>
    private sealed class CountingFrames : IPlatformBrowserFrameSink
    {
        private CountingFrames()
        {
        }

        public void OnAcceleratedFrame(scoped in BrowserGpuBuffer buffer, int format, int width, int height, int visibleX, int visibleY, int visibleWidth, int visibleHeight, bool popup)
        {
        }

        public void OnPopup(bool visible, int x, int y, int width, int height)
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Contract fakes are created behind their interface (DN0003, DN0006).")]
        internal static IPlatformBrowserFrameSink Create() => new CountingFrames();
    }
}
