// Copyright (c) Denice.ai. All rights reserved.

using Denice.CrossPlatform.Browser;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>The OS-agnostic parts of the Windows and Linux redirection proofs, with no browser and no device.</summary>
public sealed class AudioProofUnitTests
{
    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    public void Ring_WaitsForItsPrebufferThenMixesContiguouslyAndRestartsAfterAnUnderrun()
    {
        var ring = new RedirectedRing(64) { PrebufferFrames = 8 };
        float[] left = new float[4];
        float[] right = new float[4];
        _ = ring.Write(Ramp(6, 0), Ramp(6, 100));
        Assert.Equal(0, ring.MixInto(left, right));
        _ = ring.Write(Ramp(6, 6), Ramp(6, 106));
        Assert.Equal(4, ring.MixInto(left, right));
        Assert.Equal(Ramp(4, 0), left);
        Assert.Equal(Ramp(4, 100), right);
        Array.Clear(left);
        Array.Clear(right);
        Assert.Equal(4, ring.MixInto(left, right));
        Array.Clear(left);
        Array.Clear(right);
        Assert.Equal(4, ring.MixInto(left, right));
        Assert.Equal(Ramp(4, 8), left);
        Assert.Equal(0, ring.Underruns);
        Assert.Equal(0, ring.MixInto(left, right));
        Assert.Equal(1, ring.Underruns);
        Assert.Equal(12, ring.Mixed);
    }

    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    public void Ring_FlushDiscardsWhatWasWrittenAndOverflowIsCounted()
    {
        var ring = new RedirectedRing(8) { PrebufferFrames = 1 };
        Assert.Equal(8, ring.Write(Ramp(10, 0), Ramp(10, 0)));
        Assert.Equal(2, ring.Overflows);
        ring.RequestFlush();
        Assert.Equal(0, ring.Pending);
        float[] left = new float[4];
        Assert.Equal(0, ring.MixInto(left, new float[4]));
        Assert.Equal(3, ring.Write(Ramp(3, 50), Ramp(3, 50)));
        Assert.Equal(3, ring.MixInto(left, new float[4]));
        Assert.Equal(50f, left[0]);
    }

    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    public void RampAnalysis_FindsWholeAndClippedRampsAndIgnoresChanceMatches()
    {
        float[] channel = new float[60000];
        for (int index = 0; index < AudioProofPage.RampSamples; index++)
        {
            channel[1000 + index] = 0.5f * (index + 1) / AudioProofPage.RampSamples;
        }

        for (int index = 3460; index < AudioProofPage.RampSamples; index++)
        {
            channel[30000 + index - 3460] = 0.5f * (index + 1) / AudioProofPage.RampSamples;
        }

        channel[55000] = 0.5f * 11 / AudioProofPage.RampSamples;
        channel[55001] = 0.5f * 12 / AudioProofPage.RampSamples;
        IReadOnlyList<RampAnalysis.Run> runs = RampAnalysis.Find(channel);
        Assert.Equal(2, runs.Count);
        Assert.True(runs[0].Complete);
        Assert.Equal(1000, runs[0].Position);
        Assert.Equal(3460, runs[1].FirstIndex);
        Assert.False(runs[1].Complete);
    }

    private static float[] Ramp(int length, int start) => [.. Enumerable.Range(start, length).Select(value => (float)value)];
}
