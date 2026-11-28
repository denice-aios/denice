// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Tests;

/// <summary>Writes one proof run's evidence: report.json, calls.log and the mixer's drained output as mix.f32.</summary>
internal static class AudioProofReport
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Writes the evidence files and returns the report.</summary>
    /// <param name="services">The real system services.</param>
    /// <param name="directory">The evidence directory.</param>
    /// <param name="evidence">The run's evidence.</param>
    /// <param name="core">The mixer core.</param>
    /// <param name="facts">OS-specific measured facts.</param>
    /// <param name="mediaReference">The unredirected baseline's media-element lead-in, or empty.</param>
    /// <param name="precision">The precision of the path the ramps took.</param>
    /// <returns>The report.</returns>
    internal static JsonObject Write(ISystemServices services, string directory, AudioProofDriver.Evidence evidence, MixerCore core, JsonObject facts, float[] mediaReference, RampAnalysis.Precision precision)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(core);
        services.FileSystem.GetDirectoryWriter().CreateDirectory(directory);
        var results = new JsonObject();
        foreach (KeyValuePair<string, string> result in evidence.Results)
        {
            results[result.Key] = Parse(result.Value);
        }

        var report = new JsonObject
        {
            ["cases"] = AudioProofScore.Score(evidence.Left, evidence.Right, mediaReference, precision),
            ["pageResults"] = results,
            ["mixer"] = new JsonObject
            {
                ["failure"] = core.Failure,
                ["cycles"] = core.Cycles,
                ["lastCycleFrames"] = core.LastCycleFrames,
                ["speechFramesMixed"] = core.SpeechFramesMixed,
                ["drainedFrames"] = evidence.Left.Length,
                ["sinkErrors"] = new JsonArray([.. evidence.Log.Errors.Select(error => JsonValue.Create(error))]),
                ["sinkStopped"] = evidence.Log.Stopped,
            },
            ["cpuPaints"] = evidence.CpuPaints,
            ["pageClock"] = new JsonObject { ["hostMinusPageMs"] = evidence.PageOffsetMilliseconds, ["uncertaintyMs"] = evidence.PageOffsetUncertaintyMilliseconds },
            ["speechInMix"] = SpeechSegments(evidence),
            ["counters"] = AudioProofLog.CounterReport,
            ["facts"] = facts,
        };
        services.FileSystem.GetFileWriter().WriteAllText(Path.Combine(directory, "report.json"), report.ToJsonString(Indented));
        services.FileSystem.GetFileWriter().WriteAllText(Path.Combine(directory, "calls.log"), AudioProofLog.Events + "\n");
        float[] interleaved = new float[evidence.Left.Length * 2];
        for (int frame = 0; frame < evidence.Left.Length; frame++)
        {
            interleaved[2 * frame] = evidence.Left[frame];
            interleaved[(2 * frame) + 1] = evidence.Right[frame];
        }

        services.FileSystem.GetFileWriter().WriteAllBytesAsync(Path.Combine(directory, "mix.f32"), MemoryMarshal.AsBytes(interleaved.AsSpan()).ToArray(), CancellationToken.None).GetAwaiter().GetResult();
        return report;
    }

    /// <summary>
    /// Reads the media-element reference lead-in from the unredirected baseline capture named by
    /// DENICE_AUDIO_PROOF_MEDIA_REFERENCE (an interleaved stereo float32 sink capture such as the baseline pass's
    /// out-sink.f32); empty when none is named, which leaves the media row unaccepted.
    /// </summary>
    /// <param name="services">The real system services.</param>
    /// <returns>The reference lead-in, or empty.</returns>
    internal static float[] MediaReference(ISystemServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        string? path = services.Environment.GetEnvironmentVariable("DENICE_AUDIO_PROOF_MEDIA_REFERENCE");
        return string.IsNullOrEmpty(path) || !services.FileSystem.GetFileReader().Exists(path)
            ? []
            : RampAnalysis.ReferenceLeadIn(AudioProofScore.Channel(services.FileSystem.GetFileReader().ReadAllBytes(path), 1), 240);
    }

    private static JsonArray SpeechSegments(AudioProofDriver.Evidence evidence)
    {
        // Speech is mono on both channels; every ramp and tone is on one channel only. A segment is a run of equal, non-silent
        // left and right samples, joined across pauses shorter than 50 ms; its times are host milliseconds, and page times
        // are host times minus pageClock.hostMinusPageMs.
        var segments = new JsonArray();
        long[] starts = new long[evidence.Log.Packets.Count + 1];
        for (int index = 0; index < evidence.Log.Packets.Count; index++)
        {
            starts[index + 1] = starts[index] + evidence.Log.Packets[index].Length;
        }

        int packet = 0;
        long first = -1;
        long last = -1;
        for (long frame = 0; frame < evidence.Left.Length; frame++)
        {
            float left = evidence.Left[frame];
            if (Math.Abs(left) < 1e-3 || Math.Abs(left - evidence.Right[frame]) > 1e-6)
            {
                continue;
            }

            if (first >= 0 && frame - last > 2400)
            {
                segments.Add(Segment(evidence, starts, ref packet, first, last));
                first = -1;
            }

            first = first < 0 ? frame : first;
            last = frame;
        }

        if (first >= 0)
        {
            segments.Add(Segment(evidence, starts, ref packet, first, last));
        }

        return segments;
    }

    private static JsonObject Segment(AudioProofDriver.Evidence evidence, long[] starts, ref int packet, long first, long last)
    {
        double start = Time(evidence, starts, ref packet, first);
        double end = Time(evidence, starts, ref packet, last);
        return new JsonObject { ["startHostMs"] = start, ["endHostMs"] = end, ["startPageMs"] = start - evidence.PageOffsetMilliseconds, ["endPageMs"] = end - evidence.PageOffsetMilliseconds, ["frames"] = last - first + 1 };
    }

    private static double Time(AudioProofDriver.Evidence evidence, long[] starts, ref int packet, long frame)
    {
        while (packet + 1 < starts.Length - 1 && starts[packet + 1] <= frame)
        {
            packet++;
        }

        return evidence.Log.PacketTimes[packet] + ((frame - starts[packet]) * 1000.0 / MixerCore.SampleRate);
    }

    private static JsonNode? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return JsonValue.Create(json);
        }
    }
}
