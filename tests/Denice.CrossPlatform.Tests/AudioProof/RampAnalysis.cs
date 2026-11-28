// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// Finds the proof page's ramps in one mixed channel, by exact sample equality. On a float path sample i of a ramp is the
/// page's float32 value of 0.5 × (i + 1) / 19200. On Chromium's 16-bit ALSA path it is that value quantized as Chromium
/// quantizes it and read back as DENICE reads it (<see cref="PcmMath"/>), so consecutive indices may share a value and the
/// run that matches longest is taken. A run's first index says how many leading samples were not exact ramp samples; a
/// media element's leading samples are then compared with a reference lead-in, sample by sample.
/// </summary>
internal static class RampAnalysis
{
    private const int MinimumRun = 64;
    private static readonly float[] FloatRamp = Build(index => (float)(0.5 * (index + 1) / AudioProofPage.RampSamples));
    private static readonly float[] S16Ramp = Build(index => PcmMath.S16ToFloat(PcmMath.ChromiumFloatToS16(FloatRamp[index])));
    private static readonly Dictionary<float, int[]> FloatIndex = IndexOf(FloatRamp);
    private static readonly Dictionary<float, int[]> S16Index = IndexOf(S16Ramp);

    /// <summary>Which ramp samples a path delivers.</summary>
    internal enum Precision
    {
        /// <summary>The page's float32 samples, unchanged.</summary>
        Float,

        /// <summary>Chromium's 16-bit quantization, read back by DENICE.</summary>
        Signed16,
    }

    /// <summary>Gets one expected ramp sample.</summary>
    /// <param name="index">The ramp index.</param>
    /// <param name="precision">The path's precision.</param>
    /// <returns>The exact sample.</returns>
    internal static float Expected(int index, Precision precision = Precision.Float) => (precision == Precision.Float ? FloatRamp : S16Ramp)[index];

    /// <summary>Finds every ramp run of at least 64 exact samples in a channel.</summary>
    /// <param name="samples">The mixed channel.</param>
    /// <param name="precision">The path's precision.</param>
    /// <returns>The runs in order.</returns>
    internal static IReadOnlyList<Run> Find(float[] samples, Precision precision = Precision.Float)
    {
        ArgumentNullException.ThrowIfNull(samples);
        float[] ramp = precision == Precision.Float ? FloatRamp : S16Ramp;
        Dictionary<float, int[]> index = precision == Precision.Float ? FloatIndex : S16Index;
        var runs = new List<Run>();
        int position = 0;
        while (position < samples.Length)
        {
            Run best = Longest(samples, position, ramp, index);
            if (best.Length >= MinimumRun)
            {
                runs.Add(best);
                position += best.Length;
            }
            else
            {
                position++;
            }
        }

        return runs;
    }

    /// <summary>Gets the samples that precede a run in place of its missing leading indices.</summary>
    /// <param name="samples">The mixed channel.</param>
    /// <param name="run">A run that starts after index 0.</param>
    /// <returns>The run's first index of samples ending where the run starts, or empty when the channel starts too late.</returns>
    internal static float[] LeadIn(float[] samples, Run run)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return run.FirstIndex > 0 && run.Position >= run.FirstIndex ? samples[(run.Position - run.FirstIndex)..run.Position] : [];
    }

    /// <summary>Takes a reference lead-in from a capture: the samples before the first run that starts at the given index.</summary>
    /// <param name="samples">The reference channel, such as the unredirected baseline's sink capture.</param>
    /// <param name="leadFrames">The run's first index.</param>
    /// <returns>The reference lead-in, or empty when the capture has no such run.</returns>
    internal static float[] ReferenceLeadIn(float[] samples, int leadFrames)
    {
        Run? run = Find(samples).Cast<Run?>().FirstOrDefault(candidate => candidate!.Value.FirstIndex == leadFrames && candidate.Value.Position >= leadFrames);
        return run is { } found ? LeadIn(samples, found) : [];
    }

    /// <summary>
    /// Judges a ramp that starts after index 0 against a reference lead-in: every leading sample must be present and equal to
    /// the reference sample at the same position, in order, directly followed by exact ramp samples from the run's first
    /// index to the ramp's end.
    /// </summary>
    /// <param name="samples">The mixed channel.</param>
    /// <param name="run">The run.</param>
    /// <param name="reference">The reference lead-in.</param>
    /// <returns>The verdict.</returns>
    internal static LeadInVerdict CompareLeadIn(float[] samples, Run run, float[] reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        float[] lead = LeadIn(samples, run);
        if (reference.Length == 0 || run.FirstIndex != reference.Length || lead.Length != reference.Length)
        {
            return new LeadInVerdict(false, 0, 0, $"lead-in of {lead.Length} samples against a reference of {reference.Length}");
        }

        if (run.FirstIndex + run.Length != AudioProofPage.RampSamples)
        {
            return new LeadInVerdict(false, lead.Length, -1, $"exact ramp ends at index {run.FirstIndex + run.Length}, not {AudioProofPage.RampSamples}");
        }

        for (int index = 0; index < lead.Length; index++)
        {
            if (!Same(lead[index], reference[index]))
            {
                return new LeadInVerdict(false, lead.Length, index, $"lead-in sample {index} is {lead[index]:R}, reference {reference[index]:R}");
            }
        }

        return new LeadInVerdict(true, lead.Length, -1, "every leading sample equals the reference");
    }

    private static Run Longest(float[] samples, int position, float[] ramp, Dictionary<float, int[]> index)
    {
        var best = new Run(position, 0, 0);
        if (BitConverter.SingleToInt32Bits(samples[position]) == 0 || !index.TryGetValue(samples[position], out int[]? candidates))
        {
            return best;
        }

        foreach (int first in candidates)
        {
            int length = 0;
            while (position + length < samples.Length && first + length < ramp.Length && Same(samples[position + length], ramp[first + length]))
            {
                length++;
            }

            if (length > best.Length)
            {
                best = new Run(position, first, length);
            }
        }

        return best;
    }

    // Exact sample equality, bit for bit: the analysis accepts no tolerance.
    private static bool Same(float first, float second) => BitConverter.SingleToInt32Bits(first) == BitConverter.SingleToInt32Bits(second);

    private static float[] Build(Func<int, float> sample) => [.. Enumerable.Range(0, AudioProofPage.RampSamples).Select(sample)];

    private static Dictionary<float, int[]> IndexOf(float[] ramp) =>
        Enumerable.Range(0, ramp.Length).GroupBy(index => ramp[index]).ToDictionary(group => group.Key, group => group.ToArray());

    /// <summary>One run of exact ramp samples in the mix.</summary>
    /// <param name="Position">Its first sample's position in the channel.</param>
    /// <param name="FirstIndex">The ramp index of its first sample (0 when nothing leading differs).</param>
    /// <param name="Length">The contiguous exact samples.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Run(int Position, int FirstIndex, int Length)
    {
        /// <summary>Gets a value indicating whether the run is one whole ramp from index 0.</summary>
        internal bool Complete => FirstIndex == 0 && Length == AudioProofPage.RampSamples;
    }

    /// <summary>The verdict on a ramp's leading samples against a reference.</summary>
    /// <param name="Accepted">Whether every leading sample is present, equal to the reference and followed by the exact ramp.</param>
    /// <param name="Compared">The leading samples compared.</param>
    /// <param name="FirstMismatch">The first leading sample that differs, or -1.</param>
    /// <param name="Detail">Why.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct LeadInVerdict(bool Accepted, int Compared, int FirstMismatch, string Detail);
}
