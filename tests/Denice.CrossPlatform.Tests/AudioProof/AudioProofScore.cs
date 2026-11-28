// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// Scores the ramp runs of one mix against the case table, left then right channel, in the order the page plays them.
/// Every row needs one whole exact ramp, except the media element: Chromium's media-element path fades its first samples
/// in (measured in the unredirected baseline), so that row is accepted when its ramp is exact from the first unfaded index
/// to the end and every faded leading sample equals the unredirected baseline's at the same position.
/// </summary>
internal static class AudioProofScore
{
    /// <summary>The left-channel rows, in play order.</summary>
    internal static readonly string[] LeftCases = ["cold ramp, 1 s after the first context", "after 3 s silence, same context", "after 3 s silence, new context", "cross-site navigation, new document", "final ramp, shutdown right after"];

    /// <summary>The right-channel rows, in play order.</summary>
    internal static readonly string[] RightCases = ["new AudioContext while another plays", "media element while another plays", "explicit device"];

    private const int MediaRow = 1;

    /// <summary>Scores one mix.</summary>
    /// <param name="left">The mixed left channel.</param>
    /// <param name="right">The mixed right channel.</param>
    /// <param name="mediaReference">The unredirected baseline's media-element lead-in, or empty when none was given.</param>
    /// <param name="precision">The precision of the path the ramps took.</param>
    /// <returns>One JSON object per row.</returns>
    internal static JsonArray Score(float[] left, float[] right, float[] mediaReference, RampAnalysis.Precision precision)
    {
        var cases = new JsonArray();
        Add(cases, LeftCases, left, RampAnalysis.Find(left, precision), "left", null);
        Add(cases, RightCases, right, RampAnalysis.Find(right, precision), "right", mediaReference);
        return cases;
    }

    /// <summary>Reads the right or left channel of an interleaved stereo float32 capture (mix.f32, a parec capture).</summary>
    /// <param name="bytes">The capture.</param>
    /// <param name="channel">0 for left, 1 for right.</param>
    /// <returns>The channel.</returns>
    internal static float[] Channel(byte[] bytes, int channel)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(bytes.AsSpan(0, bytes.Length / 8 * 8));
        float[] result = new float[samples.Length / 2];
        for (int frame = 0; frame < result.Length; frame++)
        {
            result[frame] = samples[(2 * frame) + channel];
        }

        return result;
    }

    private static void Add(JsonArray cases, string[] names, float[] samples, IReadOnlyList<RampAnalysis.Run> runs, string channel, float[]? mediaReference)
    {
        for (int index = 0; index < Math.Max(names.Length, runs.Count); index++)
        {
            RampAnalysis.Run? run = index < runs.Count ? runs[index] : null;
            var row = new JsonObject
            {
                ["case"] = index < names.Length ? names[index] : "unexpected extra run",
                ["channel"] = channel,
                ["found"] = run is not null,
                ["position"] = run?.Position,
                ["firstIndex"] = run?.FirstIndex,
                ["contiguous"] = run?.Length,
                ["accept"] = run is { Complete: true } && index < names.Length,
            };
            if (mediaReference is not null && index == MediaRow && run is { FirstIndex: > 0 } faded)
            {
                RampAnalysis.LeadInVerdict verdict = RampAnalysis.CompareLeadIn(samples, faded, mediaReference);
                row["leadIn"] = new JsonObject
                {
                    ["against"] = mediaReference.Length > 0 ? "unredirected baseline lead-in" : "no reference given",
                    ["compared"] = verdict.Compared,
                    ["firstMismatch"] = verdict.FirstMismatch,
                    ["detail"] = verdict.Detail,
                };
                row["accept"] = verdict.Accepted;
            }

            cases.Add(row);
        }
    }
}
