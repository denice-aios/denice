// Copyright (c) Denice.ai. All rights reserved.

using Denice.CrossPlatform.MacOS.Browser;
using Xunit;

namespace Denice.CrossPlatform.Tests;

public sealed class BrowserAudioTests
{
    private static readonly float[] FirstLeft = [1f, 2f, 3f];
    private static readonly float[] FirstRight = [-1f, -2f, -3f];
    private static readonly float[] SecondLeft = [4f, 5f];

    [Fact]
    public void MixedAudioQueue_ReturnsEachBlockUnchangedInOrder()
    {
        var queue = new MixedAudioQueue(16, 4);
        Assert.True(queue.TryWrite(FirstLeft, FirstRight, 100));
        Assert.True(queue.TryWrite([4f, 5f], [-4f, -5f], 200));
        var left = new float[8];
        var right = new float[8];

        Assert.True(queue.TryRead(left, right, out int frames, out long host));
        Assert.Equal(3, frames);
        Assert.Equal(100, host);
        Assert.Equal(FirstLeft, left[..3]);
        Assert.Equal(FirstRight, right[..3]);
        Assert.True(queue.TryRead(left, right, out frames, out host));
        Assert.Equal((2, 200L), (frames, host));
        Assert.Equal(SecondLeft, left[..2]);
        Assert.False(queue.TryRead(left, right, out _, out _));
        Assert.Equal(0, queue.Dropped);
    }

    [Fact]
    public void MixedAudioQueue_KeepsSamplesExactAcrossTheRingEnd()
    {
        var queue = new MixedAudioQueue(8, 4);
        var left = new float[8];
        var right = new float[8];
        for (int round = 0; round < 10; round++)
        {
            float[] block = [round * 10f, (round * 10f) + 1, (round * 10f) + 2, (round * 10f) + 3, (round * 10f) + 4];
            Assert.True(queue.TryWrite(block, block, round));
            Assert.True(queue.TryRead(left, right, out int frames, out long host));
            Assert.Equal(5, frames);
            Assert.Equal(round, host);
            Assert.Equal(block, left[..5]);
            Assert.Equal(block, right[..5]);
        }
    }

    [Fact]
    public void MixedAudioQueue_DropsAndCountsWhenTheReaderFallsBehind()
    {
        var queue = new MixedAudioQueue(8, 2);
        Assert.True(queue.TryWrite([1f, 1f, 1f, 1f], [1f, 1f, 1f, 1f], 1));
        Assert.True(queue.TryWrite([2f, 2f, 2f, 2f], [2f, 2f, 2f, 2f], 2));
        Assert.False(queue.TryWrite([3f], [3f], 3));
        Assert.Equal(1, queue.Dropped);
        var left = new float[4];
        var right = new float[4];
        Assert.True(queue.TryRead(left, right, out _, out long host));
        Assert.Equal(1, host);
        Assert.True(queue.TryWrite([4f], [4f], 4));
    }

    [Fact]
    public void LinearResampler_DoublesTheRateAlongTheSameLine()
    {
        var resampler = new LinearResampler(24000, 48000);
        float[] ramp = [.. Enumerable.Range(0, 100).Select(i => i / 100f)];
        float[] output = resampler.Process(ramp);
        Assert.InRange(output.Length, 197, 199);
        for (int index = 0; index < output.Length; index++)
        {
            Assert.Equal(index / 200f, output[index], 5);
        }
    }

    [Fact]
    public void LinearResampler_SplitBuffersMatchOneBuffer()
    {
        float[] source = [.. Enumerable.Range(0, 2205).Select(i => MathF.Sin(i * 0.05f))];
        float[] whole = new LinearResampler(22050, 48000).Process(source);
        var split = new LinearResampler(22050, 48000);
        float[] parts = [.. split.Process(source.AsSpan(0, 700)), .. split.Process(source.AsSpan(700, 1)), .. split.Process(source.AsSpan(701))];
        Assert.Equal(whole.Length, parts.Length);
        for (int index = 0; index < whole.Length; index++)
        {
            Assert.Equal(whole[index], parts[index], 5);
        }
    }

    [Fact]
    public void LinearResampler_KeepsAConstantSignalConstant()
    {
        float[] output = new LinearResampler(22050, 48000).Process(Enumerable.Repeat(0.25f, 512).ToArray());
        Assert.All(output, sample => Assert.Equal(0.25f, sample, 6));
    }

    [Fact]
    public void LinearResampler_RejectsANonPositiveRate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinearResampler(0, 48000));
    }
}
