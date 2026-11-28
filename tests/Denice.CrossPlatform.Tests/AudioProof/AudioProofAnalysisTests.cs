// Copyright (c) Denice.ai. All rights reserved.

using Denice.CrossPlatform.Browser;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// The pure parts of the Windows and Linux redirection proofs on synthetic data, with no browser, device or proof code: the
/// queue, the PCM arithmetic of a redirected ALSA handle, speech burst timing, and the ramp and lead-in analysis.
/// </summary>
public sealed class AudioProofAnalysisTests
{
    private const int Lead = 240;
    private static readonly float[] StereoLeft = [0.5f, 1 / 32768f];
    private static readonly float[] StereoRight = [-1f, 2 / 32768f];

    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    public void Ring_WaitsForItsPrebufferThenMixesContiguouslyAndRestartsAfterAnUnderrun()
    {
        var ring = new RedirectedRing(64) { PrebufferFrames = 8 };
        float[] left = new float[4];
        float[] right = new float[4];
        _ = ring.Write(Sequence(6, 0), Sequence(6, 100));
        Assert.Equal(0, ring.MixInto(left, right));
        _ = ring.Write(Sequence(6, 6), Sequence(6, 106));
        Assert.Equal(4, ring.MixInto(left, right));
        Assert.Equal(Sequence(4, 0), left);
        Assert.Equal(Sequence(4, 100), right);
        Array.Clear(left);
        Array.Clear(right);
        Assert.Equal(4, ring.MixInto(left, right));
        Array.Clear(left);
        Array.Clear(right);
        Assert.Equal(4, ring.MixInto(left, right));
        Assert.Equal(Sequence(4, 8), left);
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
        Assert.Equal(8, ring.Write(Sequence(10, 0), Sequence(10, 0)));
        Assert.Equal(2, ring.Overflows);
        ring.RequestFlush();
        Assert.Equal(0, ring.Pending);
        float[] left = new float[4];
        Assert.Equal(0, ring.MixInto(left, new float[4]));
        Assert.Equal(3, ring.Write(Sequence(3, 50), Sequence(3, 50)));
        Assert.Equal(3, ring.MixInto(left, new float[4]));
        Assert.Equal(50f, left[0]);
    }

    [Theory]
    [Trait("Category", "AudioRedirectionProof")]
    [InlineData(0f, 0)]
    [InlineData(0.5f, 16384)]
    [InlineData(1f, 32767)]
    [InlineData(2f, 32767)]
    [InlineData(-1f, -32768)]
    [InlineData(-2f, -32768)]
    [InlineData(0.75f, 24575)]
    public void ChromiumFloatToS16_ScalesBySignRoundsToEvenAndClamps(float value, short expected) => Assert.Equal(expected, PcmMath.ChromiumFloatToS16(value));

    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    public void S16ToStereo_ReadsBackBy32768AndFoldsMonoAndExtraChannels()
    {
        short[] stereo = [16384, -32768, 1, 2];
        short[] mono = [8192, -8192];
        short[] six = [100, 200, 300, 400, 500, 600];
        float[] left = new float[2];
        float[] right = new float[2];
        PcmMath.S16ToStereo(stereo, 2, left, right);
        Assert.Equal(StereoLeft, left);
        Assert.Equal(StereoRight, right);
        PcmMath.S16ToStereo(mono, 1, left, right);
        Assert.Equal(left, right);
        Assert.Equal(-0.25f, right[1]);
        float[] oneLeft = new float[1];
        float[] oneRight = new float[1];
        PcmMath.S16ToStereo(six, 6, oneLeft, oneRight);
        Assert.Equal(100 / 32768f, oneLeft[0]);
        Assert.Equal(200 / 32768f, oneRight[0]);
    }

    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    public void AlsaAccounting_ReportsFreeSpaceAndDelayFromTheQueue()
    {
        // Chromium's measured snd_pcm_set_params: 42 666 µs at 48 kHz.
        long buffer = PcmMath.BufferFrames(42_666, 48_000, 240);
        Assert.Equal(2047, buffer);
        Assert.Equal(480, PcmMath.BufferFrames(1_000, 48_000, 240));
        var ring = new RedirectedRing(48_000) { PrebufferFrames = 1 };
        Assert.Equal(buffer, PcmMath.Available(buffer, ring.Pending));
        _ = ring.Write(new float[1500], new float[1500]);
        Assert.Equal(buffer - 1500, PcmMath.Available(buffer, ring.Pending));
        Assert.Equal(1500 + 240, PcmMath.Delay(ring.Pending, 240));
        _ = ring.Write(new float[1000], new float[1000]);
        Assert.Equal(0, PcmMath.Available(buffer, ring.Pending));
        _ = ring.MixInto(new float[2000], new float[2000]);
        Assert.Equal(buffer - 500, PcmMath.Available(buffer, ring.Pending));
        Assert.Equal(740, PcmMath.Delay(ring.Pending, 240));
    }

    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    public void SpeechBursts_RecordCaptureStateAndTheFirstMixOfEachBurst()
    {
        var tracker = new SpeechBurstTracker(1);
        float[] block = new float[3000];
        block[10] = 0.5f;
        block[20] = 0.5f;
        tracker.OnCaptured(block, 1000, 300, 7, 5_000);
        tracker.OnMixed(1010, 6_000);
        Assert.Equal(-1, Assert.Single(tracker.Bursts).MixedHostNanoseconds);
        tracker.OnMixed(1011, 7_000);
        SpeechBurstTracker.Burst burst = Assert.Single(tracker.Bursts);
        Assert.Equal(new SpeechBurstTracker.Burst(1010, 5_000, 300, 7, 7_000), burst);
        float[] next = new float[10];
        next[5] = -0.5f;
        tracker.OnCaptured(next, 4000, 0, 7, 9_000);
        Assert.Equal(1, tracker.Dropped);
    }

    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    public void SpeechBursts_SplitOnlyAfterFiftyMillisecondsOfSilence()
    {
        var tracker = new SpeechBurstTracker(4);
        float[] block = new float[6000];
        block[0] = 0.1f;
        block[SpeechBurstTracker.SilenceFrames] = 0.1f;
        block[(2 * SpeechBurstTracker.SilenceFrames) + 2] = 0.1f;
        tracker.OnCaptured(block, 0, 0, 0, 1);
        Assert.Equal(new long[] { 0, (2 * SpeechBurstTracker.SilenceFrames) + 2 }, tracker.Bursts.Select(burst => burst.Frame));
    }

    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    public void RampAnalysis_FindsWholeAndClippedFloatRampsAndIgnoresChanceMatches()
    {
        float[] channel = new float[60_000];
        Place(channel, 1000, 0, RampAnalysis.Precision.Float);
        Place(channel, 30_000, 3460, RampAnalysis.Precision.Float);
        channel[55_000] = RampAnalysis.Expected(10);
        channel[55_001] = RampAnalysis.Expected(11);
        IReadOnlyList<RampAnalysis.Run> runs = RampAnalysis.Find(channel);
        Assert.Equal(2, runs.Count);
        Assert.True(runs[0].Complete);
        Assert.Equal(1000, runs[0].Position);
        Assert.Equal(new RampAnalysis.Run(30_000, 3460, AudioProofPage.RampSamples - 3460), runs[1]);
    }

    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    public void RampAnalysis_OnTheSixteenBitPathMatchesOnlyTheExactlyQuantizedRamp()
    {
        float[] channel = new float[30_000];
        Place(channel, 500, 0, RampAnalysis.Precision.Signed16);
        Assert.True(Assert.Single(RampAnalysis.Find(channel, RampAnalysis.Precision.Signed16)).Complete);
        Assert.Empty(RampAnalysis.Find(channel, RampAnalysis.Precision.Float));

        // One sample one 16-bit step off splits the ramp there; nothing within a tolerance is accepted.
        channel[500 + 9000] += 1 / 32768f;
        IReadOnlyList<RampAnalysis.Run> split = RampAnalysis.Find(channel, RampAnalysis.Precision.Signed16);
        Assert.Equal(9000, split[0].Length);
        Assert.DoesNotContain(split, run => run.Complete);
    }

    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    public void LeadIn_EqualToTheReferenceFadeAndFollowedByTheExactRampIsAccepted()
    {
        float[] reference = Fade();
        float[] channel = FadedRamp(100);
        RampAnalysis.Run run = Assert.Single(RampAnalysis.Find(channel));
        Assert.Equal(new RampAnalysis.Run(100 + Lead, Lead, AudioProofPage.RampSamples - Lead), run);
        RampAnalysis.LeadInVerdict verdict = RampAnalysis.CompareLeadIn(channel, run, reference);
        Assert.True(verdict.Accepted, verdict.Detail);
        Assert.Equal(Lead, verdict.Compared);
        Assert.Equal(reference, RampAnalysis.ReferenceLeadIn(channel, Lead));
    }

    [Theory]
    [Trait("Category", "AudioRedirectionProof")]
    [InlineData("missing", 100)]
    [InlineData("swapped", 50)]
    [InlineData("scaled", 239)]
    public void LeadIn_AMissingReorderedOrDifferentLeadingSampleIsRejectedWhereItDiffers(string damage, int at)
    {
        float[] channel = FadedRamp(100);
        int start = 100;
        switch (damage)
        {
            case "missing":
                channel[start + at] = 0;
                break;
            case "swapped":
                (channel[start + at], channel[start + at + 1]) = (channel[start + at + 1], channel[start + at]);
                break;
            default:
                channel[start + at] *= 1.0001f;
                break;
        }

        RampAnalysis.Run run = RampAnalysis.Find(channel).Single(candidate => candidate.FirstIndex == Lead);
        RampAnalysis.LeadInVerdict verdict = RampAnalysis.CompareLeadIn(channel, run, Fade());
        Assert.False(verdict.Accepted);
        Assert.Equal(at, verdict.FirstMismatch);
    }

    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    public void LeadIn_IsRejectedWhenTheRampAfterItIsCutOrALeadingSampleIsDropped()
    {
        float[] cut = FadedRamp(100);
        cut[100 + 10_000] = 0;
        RampAnalysis.Run first = RampAnalysis.Find(cut)[0];
        Assert.False(RampAnalysis.CompareLeadIn(cut, first, Fade()).Accepted);

        // Dropping one leading sample shortens the lead-in to 239 samples before index 240.
        float[] dropped = [.. FadedRamp(100).Where((_, index) => index != 150)];
        RampAnalysis.Run run = RampAnalysis.Find(dropped).Single(candidate => candidate.FirstIndex == Lead);
        RampAnalysis.LeadInVerdict verdict = RampAnalysis.CompareLeadIn(dropped, run, Fade());
        Assert.False(verdict.Accepted);
        Assert.NotEqual(-1, verdict.FirstMismatch);
        Assert.False(RampAnalysis.CompareLeadIn(dropped, run, []).Accepted);
    }

    [Fact]
    [Trait("Category", "AudioRedirectionProof")]
    public void Score_JudgesTheMediaRowByItsLeadInAndEveryOtherRowByAWholeRamp()
    {
        float[] left = new float[50_000];
        Place(left, 100, 0, RampAnalysis.Precision.Float);
        float[] right = new float[100_000];
        Place(right, 100, 0, RampAnalysis.Precision.Float);
        Array.Copy(FadedRamp(0), 0, right, 30_000, AudioProofPage.RampSamples);
        Place(right, 70_000, 0, RampAnalysis.Precision.Float);
        System.Text.Json.Nodes.JsonArray cases = AudioProofScore.Score(left, right, Fade(), RampAnalysis.Precision.Float);
        Assert.Equal(AudioProofScore.LeftCases.Length + AudioProofScore.RightCases.Length, cases.Count);
        Assert.True((bool)cases[0]!["accept"]!);
        Assert.False((bool)cases[1]!["accept"]!);
        Assert.True((bool)cases[AudioProofScore.LeftCases.Length + 1]!["accept"]!);
        Assert.Equal(Lead, (int)cases[AudioProofScore.LeftCases.Length + 1]!["leadIn"]!["compared"]!);
        Assert.False((bool)AudioProofScore.Score(left, right, [], RampAnalysis.Precision.Float)[AudioProofScore.LeftCases.Length + 1]!["accept"]!);
    }

    private static float[] Sequence(int length, int start) => [.. Enumerable.Range(start, length).Select(value => (float)value)];

    private static void Place(float[] channel, int position, int firstIndex, RampAnalysis.Precision precision)
    {
        for (int index = firstIndex; index < AudioProofPage.RampSamples; index++)
        {
            channel[position + index - firstIndex] = RampAnalysis.Expected(index, precision);
        }
    }

    // The fade measured in the unredirected baseline: ramp sample i scaled by i / 240 for the first 240 samples.
    private static float[] Fade() => [.. Enumerable.Range(0, Lead).Select(index => RampAnalysis.Expected(index) * index / Lead)];

    private static float[] FadedRamp(int position)
    {
        float[] channel = new float[position + AudioProofPage.RampSamples + 1000];
        Array.Copy(Fade(), 0, channel, position, Lead);
        for (int index = Lead; index < AudioProofPage.RampSamples; index++)
        {
            channel[position + index] = RampAnalysis.Expected(index);
        }

        return channel;
    }
}
