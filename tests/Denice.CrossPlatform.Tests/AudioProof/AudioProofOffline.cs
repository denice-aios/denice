// Copyright (c) Denice.ai. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// Re-scores recorded L1 evidence offline with the current <see cref="AudioProofScore"/>: each pass's mixer dump
/// (evidence/mix.f32), with the media-element row judged against the unredirected baseline's sink capture
/// (linux-x64-baseline/out-sink.f32). No browser, device or proof code runs.
/// </summary>
internal static class AudioProofOffline
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Scores every pass under the evidence root and returns the result.</summary>
    /// <param name="services">The real system services.</param>
    /// <param name="root">handoff/audio-proofs/evidence.</param>
    /// <returns>The JSON result.</returns>
    internal static string Run(ISystemServices services, string root)
    {
        ArgumentNullException.ThrowIfNull(services);
        IFileReader files = services.FileSystem.GetFileReader();
        float[] baselineRight = AudioProofScore.Channel(files.ReadAllBytes(Path.Combine(root, "linux-x64-baseline", "out-sink.f32")), 1);
        float[] reference = RampAnalysis.ReferenceLeadIn(baselineRight, 240);
        var result = new JsonObject
        {
            ["reference"] = new JsonObject
            {
                ["source"] = "linux-x64-baseline/out-sink.f32, right channel, the samples before the run that starts at ramp index 240",
                ["samples"] = reference.Length,
                ["firstGains"] = new JsonArray([.. reference.Take(4).Select((value, index) => JsonValue.Create(value / RampAnalysis.Expected(index)))]),
            },
        };
        foreach (string pass in new[] { "pipewire", "pulseaudio" })
        {
            byte[] mix = files.ReadAllBytes(Path.Combine(root, "linux-x64-" + pass, "evidence", "mix.f32"));
            result[pass] = AudioProofScore.Score(AudioProofScore.Channel(mix, 0), AudioProofScore.Channel(mix, 1), reference, RampAnalysis.Precision.Float);
        }

        return result.ToJsonString(Indented);
    }
}
