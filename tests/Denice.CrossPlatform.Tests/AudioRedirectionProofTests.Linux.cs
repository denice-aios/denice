// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Browser;
using Denice.CrossPlatform.Linux.Browser;
using Denice.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// The Linux browser-audio redirection proof (stage L1), compiled only with DeniceAudioRedirectionProof and run by
/// handoff/audio-proofs/linux/run-proof.sh in a container whose sinks are all null sinks. With a PulseAudio-compatible
/// server the mixer plays through DENICE's own libpulse stream and page speech comes from a private speech-dispatcher
/// playing to DENICE's speech sink; with no server (DENICE_AUDIO_SERVER=none) the mixer runs on the fake output and the
/// run measures Chromium's ALSA call sequence. Evidence goes to DENICE_AUDIO_PROOF_OUT.
/// </summary>
public sealed class AudioRedirectionProofTests
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _progress = [];

    /// <summary>Initializes a new instance of the <see cref="AudioRedirectionProofTests"/> class.</summary>
    /// <param name="output">The evidence log.</param>
    public AudioRedirectionProofTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    [SuppressMessage("Denice.Architecture", "DN0011", Justification = "This bounded proof fixture starts and stops its own private speech-dispatcher; no product process interface exists.")]
    public void ChromiumAudioReachesOnlyTheMixer()
    {
        ISystemServices services = SystemServicesBuilder.Real().Build();
        TimeProvider clock = services.Clock;
        string directory = services.Environment.GetEnvironmentVariable("DENICE_AUDIO_PROOF_OUT") ?? Path.Combine(services.Environment.GetBaseDirectory(), "audio-proof");
        bool server = !string.Equals(services.Environment.GetEnvironmentVariable("DENICE_AUDIO_SERVER"), "none", StringComparison.Ordinal);
        int processId = Environment.ProcessId;

        // The socket lives in a private temporary directory: the evidence directory may be a bind mount that cannot hold unix
        // sockets (measured in the Docker-on-macOS container).
        string socket = Path.Combine(services.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-speechd-"), "speechd.sock");

        // libc setenv first, before any audio library thread exists that could read the environment concurrently.
        if (server)
        {
            PulseSpeechCapture.UseSpeechAddress(socket);
        }

        var core = new MixerCore();
        core.UseClock(clock);
        using IMixerOutput output = server ? new LinuxPulseOutput() : new FakeMixerOutput();
        output.Start(core, clock);
        PulseSpeechCapture? speech = server ? PulseSpeechCapture.Start(processId, clock) : null;
        Process? daemon = null;
        var facts = new JsonObject { ["server"] = services.Environment.GetEnvironmentVariable("DENICE_AUDIO_SERVER"), ["output"] = output.GetType().Name };
        AudioProofDriver.Evidence evidence;
        try
        {
            services.FileSystem.GetDirectoryWriter().CreateDirectory(directory);
            Stage(services, directory, "mixer output started: " + output.GetType().Name);
            if (speech is not null)
            {
                daemon = StartSpeechDaemon(services, directory, socket, speech.Sink, facts);
            }

            evidence = RunCases(services, directory, core, speech, facts);
        }
        finally
        {
            output.Stop();
            speech?.Dispose();
            StopSpeechDaemon(daemon, facts);
            CopySpeechLogs(services, socket, directory);
        }

        facts["redirectedStreamsOpenAfterShutdown"] = PulseRedirection.OpenStreams;
        facts["alsaSequences"] = AlsaObservation.Report;
        JsonObject report = AudioProofReport.Write(services, directory, evidence, core, facts);
        _output.WriteLine(report.ToJsonString());
        Assert.True(evidence.Results.ContainsKey("final"), "The case table must run to its final ramp.");
        if (server)
        {
            Assert.Equal(0, AudioProofLog.Counter("real playback connects"));
            Assert.True(AudioProofLog.Counter("redirected playback connects") > 0, "Chromium's playback streams must be redirected.");
            Assert.All(report["cases"]!.AsArray(), item => Assert.True((bool)item!["accept"]!, item.ToJsonString()));
            Assert.Null(core.Failure);
        }
    }

    private static void Register(MixerCore core, PulseSpeechCapture? speech)
    {
        // The speech monitor is always sounding (silence included), so it joins the mix only once the browser drains it.
        if (speech is not null)
        {
            speech.Arm();
            _ = core.Register(speech);
        }
    }

    [SuppressMessage("Denice.Architecture", "DN0011", Justification = "Starts this proof fixture's own private speech-dispatcher daemon.")]
    private static Process? StartSpeechDaemon(ISystemServices services, string directory, string socket, string sink, JsonObject facts)
    {
        // Everything the daemon writes stays on the container's own filesystem: its line-by-line logging to the evidence
        // bind mount delayed its socket by about 50 seconds (measured). Its logs are copied to the evidence afterwards.
        string root = Path.GetDirectoryName(socket)!;
        string config = Path.Combine(root, "config");
        services.FileSystem.GetDirectoryWriter().CreateDirectory(Path.Combine(config, "modules"));
        services.FileSystem.GetDirectoryWriter().CreateDirectory(Path.Combine(root, "logs"));
        string settings = $"""
            AudioOutputMethod "pulse"
            AudioPulseDevice "{sink}"
            AddModule "espeak-ng" "sd_espeak-ng" "espeak-ng.conf"
            DefaultModule espeak-ng
            LogLevel 3
            """;
        services.FileSystem.GetFileWriter().WriteAllText(Path.Combine(config, "speechd.conf"), settings);
        const string module = "/etc/speech-dispatcher/modules/espeak-ng.conf";
        if (services.FileSystem.GetFileReader().Exists(module))
        {
            services.FileSystem.GetFileWriter().WriteAllText(Path.Combine(config, "modules", "espeak-ng.conf"), services.FileSystem.GetFileReader().ReadAllText(module));
        }

        string pidFile = Path.Combine(root, "speechd.pid");

        // The daemon keeps the launcher's output, so it is sent to its own log rather than the test's.
        var start = new ProcessStartInfo("sh") { UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("exec speech-dispatcher -d -c unix_socket -S \"$1\" -C \"$2\" -P \"$3\" -t 0 -L \"$4\" >\"$4/stdout.log\" 2>&1");
        foreach (string argument in new[] { "sh", socket, config, pidFile, Path.Combine(root, "logs") })
        {
            start.ArgumentList.Add(argument);
        }

        // speech-dispatcher 0.12 stays in the foreground with -d; exec makes this process the daemon itself.
        Process daemon = Process.Start(start) ?? throw new InvalidOperationException("speech-dispatcher did not start.");

        // The daemon creates its socket only after its module has listed every voice; libspeechd connects once, when
        // Chromium's speech starts up, so the browser starts only after the socket exists.
        long started = services.Clock.GetTimestamp();
        bool present = false;
        while (!present && services.Clock.GetElapsedTime(started) < TimeSpan.FromSeconds(60))
        {
            present = Accepts(socket);
            Task.Delay(TimeSpan.FromMilliseconds(100), services.Clock).GetAwaiter().GetResult();
        }

        facts["speechSocketPresent"] = present;
        facts["speechSocketAfterMs"] = services.Clock.GetElapsedTime(started).TotalMilliseconds;
        facts["speechDaemonPid"] = services.FileSystem.GetFileReader().Exists(pidFile) ? services.FileSystem.GetFileReader().ReadAllText(pidFile).Trim() : string.Empty;
        return daemon;
    }

    private static void CopySpeechLogs(ISystemServices services, string socket, string directory)
    {
        string logs = Path.Combine(Path.GetDirectoryName(socket)!, "logs");
        if (!services.FileSystem.GetDirectoryReader().DirectoryExists(logs))
        {
            return;
        }

        string target = Path.Combine(directory, "speechd-logs");
        services.FileSystem.GetDirectoryWriter().CreateDirectory(target);
        foreach (string log in services.FileSystem.GetDirectoryReader().EnumerateFiles(logs, "*.log", new EnumerationOptions()))
        {
            services.FileSystem.GetFileWriter().WriteAllText(Path.Combine(target, Path.GetFileName(log)), services.FileSystem.GetFileReader().ReadAllText(log));
        }
    }

    private static bool Accepts(string socket)
    {
        using var probe = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
        try
        {
            probe.Connect(new System.Net.Sockets.UnixDomainSocketEndPoint(socket));
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;
        }
    }

    [SuppressMessage("Denice.Architecture", "DN0011", Justification = "Stops this proof fixture's own private speech-dispatcher daemon.")]
    private static void StopSpeechDaemon(Process? daemon, JsonObject facts)
    {
        if (daemon is null)
        {
            return;
        }

        using (daemon)
        {
            facts["speechDaemonRunningAtEnd"] = !daemon.HasExited;
            if (!daemon.HasExited)
            {
                daemon.Kill();
            }
        }
    }

    private AudioProofDriver.Evidence RunCases(ISystemServices services, string directory, MixerCore core, PulseSpeechCapture? speech, JsonObject facts)
    {
        // DENICE_AUDIO_PROOF_BASELINE=1 runs the same cases with the normal, unredirected browser (null sinks only), to
        // separate CEF's own behaviour in the container from the redirection's.
        bool baseline = string.Equals(services.Environment.GetEnvironmentVariable("DENICE_AUDIO_PROOF_BASELINE"), "1", StringComparison.Ordinal);
        facts["baseline"] = baseline;
        IPlatformBrowser browser = baseline ? CefPlatformBrowser.Create() : CefPlatformBrowser.CreateWithAudioProof(core);
        AudioProofDriver.Evidence evidence = AudioProofDriver.Run(services, browser, core, Path.Combine(directory, "cache"), "denice_explicit", text => Stage(services, directory, text), () => Register(core, speech));
        if (speech is not null)
        {
            facts["speechCapturedFrames"] = speech.FramesCaptured;
            facts["speechSoundingFrames"] = speech.SoundingFrames;
            facts["speechFramesMixed"] = speech.FramesMixed;
            facts["speechSink"] = speech.Sink;
            core.Unregister(speech);
        }

        return evidence;
    }

    private void Stage(ISystemServices services, string directory, string text)
    {
        // Kept on disk at every stage, so a run that ends the test host still shows how far it got.
        _output.WriteLine(text);
        _progress.Add(text);
        services.FileSystem.GetFileWriter().WriteAllText(Path.Combine(directory, "progress.log"), string.Join('\n', _progress) + "\n\n" + AudioProofLog.Events);
    }
}
