// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.MacOS.Browser;
using Xunit;
using Xunit.Abstractions;

namespace Denice.CrossPlatform.Tests;

/// <summary>Validation cases 1 to 7 of the approved per-stream conversion amendment, against the real AudioConverter.</summary>
public sealed class StreamRateConverterTests(ITestOutputHelper output)
{
    private const double SourceRate = 44100;

    [Fact]
    public void Converts44100To48000AtTheSamePitchAndLevel()
    {
        // Case 1: a 1 kHz sine at amplitude 0.5.
        float[] converted = Convert(FakeSource.Sine, 2, [], 512, 96000);
        float[] settled = converted[4800..];
        int crossings = settled.Zip(settled.Skip(1)).Count(pair => (pair.First < 0) != (pair.Second < 0));
        double hertz = crossings / 2.0 * 48000 / settled.Length;
        double peak = Math.Sqrt(settled.Average(sample => (double)sample * sample)) * Math.Sqrt(2);
        output.WriteLine($"pitch {hertz:F2} Hz, peak {peak:F5}");
        Assert.InRange(hertz, 999, 1001);
        Assert.InRange(20 * Math.Log10(peak / 0.5), -0.1, 0.1);
    }

    [Fact]
    public void OutputIsTheSameWhateverTheCycleSize()
    {
        // Case 2: phase stays continuous across pulls of any size.
        float[] reference = Convert(FakeSource.Sine, 2, [], 4096, 48000);
        foreach (int cycle in new[] { 256, 512, 1024, 937 })
        {
            float[] pulled = Convert(FakeSource.Sine, 2, [], cycle, 48000);
            double worst = reference.Zip(pulled).Max(pair => Math.Abs(pair.First - pair.Second));
            output.WriteLine($"cycle {cycle}: largest difference {worst:E2}");
            Assert.True(worst < 1e-5, $"Pulling {cycle} frames at a time changed the output by {worst}.");
        }
    }

    [Fact]
    public void FirstOutputSampleIsTheStreamsFirstSampleAndPrimingIsMeasured()
    {
        // Case 3: a ramp 0.5 * (n + 1) / 44100; the output ramp's line, fitted after the filter settles, must pass through
        // input index 0 at output index 0 (zero latency), and the first output samples are reported as measured.
        float[] converted = Convert(FakeSource.Ramp, 2, [], 512, 9600, out Priming measured);
        double slope = 0.5 / SourceRate * SourceRate / 48000;
        double[] offsets = [.. Enumerable.Range(2000, 4000).Select(k => (converted[k] / slope) - 1 - (k * 1.0))];
        double delayOutputSamples = -offsets.Average();
        output.WriteLine($"prime leading/trailing frames {measured.Leading}/{measured.Trailing}; first output samples {string.Join(", ", converted[..6].Select(v => v.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)))}; expected {string.Join(", ", Enumerable.Range(0, 6).Select(k => ((k + 1) * slope).ToString("E3", System.Globalization.CultureInfo.InvariantCulture)))}; fitted delay {delayOutputSamples:F3} output samples");
        Assert.InRange(delayOutputSamples, -1, 1);
        Assert.True(converted[0] > 0, "The first output sample must already carry the stream's first sample.");
    }

    [Fact]
    public void ChromiumSampleTimeAdvancesByExactlyThePreviousRequest()
    {
        // Case 4: Chromium's glitch accounting needs mSampleTime to advance by the frames it last delivered.
        _ = Convert(FakeSource.Ramp, 2, [], 937, 48000);
        Assert.True(FakeSource.Requests.Count > 10);
        double expected = 0;
        foreach ((double sampleTime, uint frames, ulong host) in FakeSource.Requests.Select(request => (request.SampleTime, request.Frames, request.Host)))
        {
            Assert.Equal(expected, sampleTime);
            Assert.Equal(FakeSource.MixerHostTime, host);
            expected += frames;
        }
    }

    [Theory]
    [InlineData(1, new uint[] { }, new[] { 1f }, new[] { 1f })]
    [InlineData(6, new uint[] { 1, 2, 3, 4, 5, 6 }, new[] { 1f, 0f, 0.70710678f, 0f, 0.70710678f, 0f }, new[] { 0f, 1f, 0.70710678f, 0f, 0f, 0.70710678f })]
    [InlineData(4, new uint[] { 1, 2, 33, 34 }, new[] { 1f, 0f, 0.70710678f, 0f }, new[] { 0f, 1f, 0f, 0.70710678f })]
    public void FoldsTheLayoutToStereo(int channels, uint[] labels, float[] leftGains, float[] rightGains)
    {
        // Case 5: each channel carries 0.1 * (channel + 1); 48 kHz, so only the fold applies.
        float[] left = Convert(FakeSource.PerChannelConstant, channels, labels, 256, 256, out _, 48000, right: true, out float[] right);
        float expectedLeft = leftGains.Select((gain, channel) => gain * 0.1f * (channel + 1)).Sum();
        float expectedRight = rightGains.Select((gain, channel) => gain * 0.1f * (channel + 1)).Sum();
        Assert.Equal(expectedLeft, left[100], 5);
        Assert.Equal(expectedRight, right[100], 5);
    }

    [Fact]
    public void UnknownLayoutTakesTheFirstTwoChannelsAndSaysSo()
    {
        using RedirectedStream stream = FakeSource.Stream(48000, 3, [1, 2, 999]);
        using StreamRateConverter converter = StreamRateConverter.Create(stream);
        Assert.Contains("999", converter.UnknownLayout, StringComparison.Ordinal);
        using RedirectedStream stereo = FakeSource.Stream(48000, 2, []);
        Assert.False(StreamRateConverter.IsNeeded(stereo));
        Assert.False(converter.Resamples);
    }

    [Fact]
    public void ResetDropsTheHistorySoOldSoundIsNotReplayed()
    {
        // Case 6: loud sine, reset, then silence: nothing from before the reset may come out.
        using RedirectedStream stream = FakeSource.Stream(SourceRate, 2, []);
        using StreamRateConverter converter = StreamRateConverter.Create(stream);
        var timestamp = new PinnedTimestamp();
        FakeSource.Select(FakeSource.Sine);
        for (int cycle = 0; cycle < 20; cycle++)
        {
            Assert.True(timestamp.Pull(converter, 512));
        }

        converter.Reset();
        FakeSource.Select(FakeSource.Silence);
        Assert.True(timestamp.Pull(converter, 512));
        float worst = PinnedTimestamp.Left(converter, 512).Max(Math.Abs);

        Assert.True(worst < 1e-6, $"Sound from before the reset came out after it: {worst}.");
    }

    [Fact]
    public void TenThousandPullsAllocateNoManagedMemory()
    {
        // Case 7 (managed half): the render-thread path must not allocate. Native allocation is measured separately.
        using RedirectedStream stream = FakeSource.Stream(SourceRate, 2, []);
        using StreamRateConverter converter = StreamRateConverter.Create(stream);
        var timestamp = new PinnedTimestamp();
        FakeSource.Select(FakeSource.Sine);
        FakeSource.Recording = false;
        for (int cycle = 0; cycle < 100; cycle++)
        {
            _ = timestamp.Pull(converter, 512);
        }

        // One-time runtime work (tiered recompilation, stub creation) can land in a window and allocate a few thousand bytes
        // once (CI run 46100513422 saw 2624). A per-pull allocation shows in every window as at least 10,000 objects, so the
        // render path passes only when every window stays under one byte per pull and a window allocates exactly zero.
        var windows = new List<long>();
        for (int window = 0; window < 3 && (windows.Count == 0 || windows[^1] != 0); window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int cycle = 0; cycle < 10000; cycle++)
            {
                _ = timestamp.Pull(converter, 512);
            }

            windows.Add(GC.GetAllocatedBytesForCurrentThread() - before);
        }

        FakeSource.Recording = true;
        output.WriteLine($"managed bytes allocated per 10000-pull window: {string.Join(", ", windows)}");
        Assert.All(windows, allocated => Assert.True(allocated < 10000, $"A 10000-pull window allocated {allocated} bytes."));
        Assert.Equal(0, windows[^1]);
    }

    private static float[] Convert(nint source, int channels, uint[] labels, int cycle, int total) => Convert(source, channels, labels, cycle, total, out _);

    private static float[] Convert(nint source, int channels, uint[] labels, int cycle, int total, out Priming measured) =>
        Convert(source, channels, labels, cycle, total, out measured, SourceRate, right: false, out _);

    private static float[] Convert(nint source, int channels, uint[] labels, int cycle, int total, out Priming measured, double rate, bool right, out float[] rightOutput)
    {
        using RedirectedStream stream = FakeSource.Stream(rate, channels, labels);
        using StreamRateConverter converter = StreamRateConverter.Create(stream);
        measured = new Priming(converter.PrimeLeadingFrames, converter.PrimeTrailingFrames);
        FakeSource.Select(source);
        var timestamp = new PinnedTimestamp();
        var left = new List<float>(total);
        var rightList = new List<float>(right ? total : 0);
        while (left.Count < total)
        {
            int frames = Math.Min(cycle, total - left.Count);
            Assert.True(timestamp.Pull(converter, frames));
            left.AddRange(PinnedTimestamp.Left(converter, frames));
            if (right)
            {
                rightList.AddRange(PinnedTimestamp.Right(converter, frames));
            }
        }

        rightOutput = [.. rightList];
        return [.. left];
    }

    /// <summary>The converter's reported priming frames.</summary>
    /// <param name="Leading">Leading frames.</param>
    /// <param name="Trailing">Trailing frames.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Priming(int Leading, int Trailing);

    /// <summary>One render request Chromium received.</summary>
    /// <param name="SampleTime">The sample time it was given.</param>
    /// <param name="Frames">The frames it was asked for.</param>
    /// <param name="Host">The host time it was given.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Request(double SampleTime, uint Frames, ulong Host);

    /// <summary>Fake Chromium render callbacks whose signal is indexed by the sample time Chromium is given.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Chromium's render callback takes unmanaged Core Audio structures.")]
    private static unsafe class FakeSource
    {
        internal const ulong MixerHostTime = 123456789;

        internal static nint Sine => (nint)(delegate* unmanaged<nint, uint*, nint, uint, uint, nint, int>)&WriteSine;

        internal static nint Ramp => (nint)(delegate* unmanaged<nint, uint*, nint, uint, uint, nint, int>)&WriteRamp;

        internal static nint Silence => (nint)(delegate* unmanaged<nint, uint*, nint, uint, uint, nint, int>)&WriteSilence;

        internal static nint PerChannelConstant => (nint)(delegate* unmanaged<nint, uint*, nint, uint, uint, nint, int>)&WritePerChannel;

        internal static List<Request> Requests { get; } = [];

        internal static bool Recording { get; set; } = true;

        private static nint Selected { get; set; }

        internal static void Select(nint callback)
        {
            Selected = callback;
            Requests.Clear();
        }

        internal static RedirectedStream Stream(double rate, int channels, uint[] labels)
        {
            RedirectedStream stream = RedirectedStream.Create(1);
            stream.Format = new RedirectedStream.StreamFormat(rate, 0x29, (uint)channels, 32);
            stream.ChannelLabels = labels;
            stream.RenderCallback = new RedirectedStream.RenderCallbackTarget((nint)(delegate* unmanaged<nint, uint*, nint, uint, uint, nint, int>)&Dispatch, 0);
            return stream;
        }

        [UnmanagedCallersOnly]
        private static int Dispatch(nint context, uint* flags, nint timestamp, uint bus, uint frames, nint bufferList)
        {
            if (Recording)
            {
                Requests.Add(new Request(*(double*)timestamp, frames, *(ulong*)(timestamp + 8)));
            }

            return ((delegate* unmanaged<nint, uint*, nint, uint, uint, nint, int>)Selected)(context, flags, timestamp, bus, frames, bufferList);
        }

        [UnmanagedCallersOnly]
        private static int WriteSine(nint context, uint* flags, nint timestamp, uint bus, uint frames, nint bufferList) => Fill(timestamp, frames, bufferList, n => (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * n / SourceRate)));

        [UnmanagedCallersOnly]
        private static int WriteRamp(nint context, uint* flags, nint timestamp, uint bus, uint frames, nint bufferList) => Fill(timestamp, frames, bufferList, n => (float)(0.5 * (n + 1) / SourceRate));

        [UnmanagedCallersOnly]
        private static int WriteSilence(nint context, uint* flags, nint timestamp, uint bus, uint frames, nint bufferList) => Fill(timestamp, frames, bufferList, _ => 0f);

        [UnmanagedCallersOnly]
        private static int WritePerChannel(nint context, uint* flags, nint timestamp, uint bus, uint frames, nint bufferList)
        {
            int channels = *(int*)bufferList;
            for (int channel = 0; channel < channels; channel++)
            {
                float* samples = *(float**)(bufferList + 8 + (16 * channel) + 8);
                for (int frame = 0; frame < frames; frame++)
                {
                    samples[frame] = 0.1f * (channel + 1);
                }
            }

            return 0;
        }

        private static int Fill(nint timestamp, uint frames, nint bufferList, Func<double, float> signal)
        {
            double start = *(double*)timestamp;
            int channels = *(int*)bufferList;
            for (int channel = 0; channel < channels; channel++)
            {
                float* samples = *(float**)(bufferList + 8 + (16 * channel) + 8);
                for (int frame = 0; frame < frames; frame++)
                {
                    samples[frame] = signal(start + frame);
                }
            }

            return 0;
        }
    }

    /// <summary>A pinned mixer AudioTimeStamp with a known host time.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "The converter takes the mixer's unmanaged AudioTimeStamp.")]
    private sealed unsafe class PinnedTimestamp
    {
        private readonly byte[] _bytes = GC.AllocateArray<byte>(64, pinned: true);

        internal PinnedTimestamp()
        {
            fixed (byte* bytes = _bytes)
            {
                *(ulong*)(bytes + 8) = FakeSource.MixerHostTime;
            }
        }

        internal static float[] Left(StreamRateConverter converter, int frames) => new Span<float>(converter.Left, frames).ToArray();

        internal static float[] Right(StreamRateConverter converter, int frames) => new Span<float>(converter.Right, frames).ToArray();

        internal bool Pull(StreamRateConverter converter, int frames)
        {
            fixed (byte* bytes = _bytes)
            {
                return converter.Pull((nint)bytes, frames);
            }
        }
    }
}
