// Copyright (c) Denice.ai. All rights reserved.

using System.Text.Json.Nodes;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Browser;
using Denice.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// The Windows browser-audio redirection proof (stage W1), compiled only with DeniceAudioRedirectionProof. Hosted runners
/// have no audio endpoint, so the mixer runs on the test-only timer-driven <see cref="FakeMixerOutput"/> and the
/// redirected enumerator supplies one virtual default endpoint; nothing reaches any device. Evidence goes to
/// DENICE_AUDIO_PROOF_OUT. The explicit-device case selects the first audio output that is not "default" or
/// "communications" (or the one whose label contains DENICE_AUDIO_EXPLICIT_LABEL).
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
    public void ChromiumAudioReachesOnlyTheMixer()
    {
        ISystemServices services = SystemServicesBuilder.Real().Build();
        TimeProvider clock = services.Clock;
        string directory = services.Environment.GetEnvironmentVariable("DENICE_AUDIO_PROOF_OUT") ?? Path.Combine(services.Environment.GetBaseDirectory(), "audio-proof");
        string label = services.Environment.GetEnvironmentVariable("DENICE_AUDIO_EXPLICIT_LABEL") ?? string.Empty;
        services.FileSystem.GetDirectoryWriter().CreateDirectory(directory);
        var core = new MixerCore();
        core.UseClock(clock);
        var facts = new JsonObject { ["output"] = nameof(FakeMixerOutput), ["explicitLabel"] = label };
        AudioProofDriver.Evidence evidence;
        using (var output = new FakeMixerOutput())
        {
            output.Start(core, clock);
            IPlatformBrowser browser = CefPlatformBrowser.CreateWithAudioProof(core, clock);
            evidence = AudioProofDriver.Run(services, browser, core, Path.Combine(directory, "cache"), label, text => Stage(services, directory, text));
            output.Stop();
            facts["fakeOutputCycles"] = output.Cycles;
            facts["fakeOutputWakes"] = output.Wakes;
            facts["fakeOutputLargestBurstCycles"] = output.LargestBurst;
        }

        JsonObject report = AudioProofReport.Write(services, directory, evidence, core, facts, AudioProofReport.MediaReference(services), RampAnalysis.Precision.Float);
        _output.WriteLine(report.ToJsonString());
        Assert.True(evidence.Results.ContainsKey("final"), "The case table must run to its final ramp.");
        Assert.True(AudioProofLog.Counter("DENICE IAudioClient activations") > 0, "Chromium's WASAPI streams must be redirected.");
        Assert.Equal(0, AudioProofLog.Counter("waveOutOpen (redirected)"));
        Assert.All(report["cases"]!.AsArray(), item => Assert.True((bool)item!["accept"]!, item.ToJsonString()));
        Assert.Null(core.Failure);
    }

    private void Stage(ISystemServices services, string directory, string text)
    {
        // Kept on disk at every stage, so a run that ends the test host still shows how far it got.
        _output.WriteLine(text);
        _progress.Add(text);
        services.FileSystem.GetFileWriter().WriteAllText(Path.Combine(directory, "progress.log"), string.Join('\n', _progress) + "\n\n" + AudioProofLog.Events);
    }
}
