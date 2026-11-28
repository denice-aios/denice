// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// Finds the proof page's ramps in one mixed channel. A run is a stretch of samples that each equal
/// 0.5 × (i + 1) / 19200 for consecutive source indices i; its first index says how many leading samples were lost.
/// </summary>
internal static class RampAnalysis
{
    private const double Step = 0.5 / AudioProofPage.RampSamples;
    private const double Tolerance = 2e-6;
    private const int MinimumRun = 64;

    /// <summary>Finds every ramp run in a channel.</summary>
    /// <param name="samples">The mixed channel.</param>
    /// <returns>The runs in order.</returns>
    internal static IReadOnlyList<Run> Find(float[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var runs = new List<Run>();
        int position = 0;
        while (position < samples.Length - 1)
        {
            double value = samples[position];
            double index = (value / Step) - 1;
            bool starts = value > 0 && Math.Abs(index - Math.Round(index)) < 0.05 && Math.Abs(samples[position + 1] - value - Step) < Tolerance;
            if (!starts)
            {
                position++;
                continue;
            }

            int first = (int)Math.Round(index);
            int length = 1;
            while (position + length < samples.Length && first + length < AudioProofPage.RampSamples
                && Math.Abs(samples[position + length] - (Step * (first + length + 1))) < Tolerance)
            {
                length++;
            }

            // Two or three matching steps also occur by chance in speech; a ramp run is at least MinimumRun samples.
            if (length >= MinimumRun)
            {
                runs.Add(new Run(position, first, length));
            }

            position += length;
        }

        return runs;
    }

    /// <summary>One run of ramp samples in the mix.</summary>
    /// <param name="Position">Its first sample's position in the channel.</param>
    /// <param name="FirstIndex">The source index of its first sample (0 when nothing leading was lost).</param>
    /// <param name="Length">The contiguous samples matched.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Run(int Position, int FirstIndex, int Length)
    {
        /// <summary>Gets a value indicating whether the run is one whole ramp from index 0.</summary>
        internal bool Complete => FirstIndex == 0 && Length == AudioProofPage.RampSamples;
    }
}
