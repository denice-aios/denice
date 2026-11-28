// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.MacOS.Recording;
using Denice.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace Denice.CrossPlatform.Tests;

public sealed class RecordingSpecTests : IDisposable
{
    private static readonly float[] ExpectedFirstSamples = [0.1f, 0.2f, 0.3f, 0.4f];

    private readonly ISystemServices _services = SystemServicesBuilder.Real().Build();
    private readonly string _directory;
    private readonly ITestOutputHelper _output;

    public RecordingSpecTests(ITestOutputHelper output)
    {
        _output = output;
        _directory = _services.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-recording-spec-");
    }

    public void Dispose() => _services.FileSystem.GetDirectoryWriter().DeleteDirectory(_directory, recursive: true);

    [Fact]
    public void ExistingDestinationIsNeverOverwritten()
    {
        string path = Path.Combine(_directory, "existing.mp4");
        _services.FileSystem.GetFileWriter().WriteAllText(path, "existing data");
        Assert.Throws<IOException>(() => Mp4Recorder.Create(path, 64, 64, _services.Clock));
        Assert.Equal("existing data", _services.FileSystem.GetFileReader().ReadAllText(path));
    }

    [Fact]
    public void EmptyRecordingCannotReportSuccessfulFinalization()
    {
        using var recorder = Mp4Recorder.Create(Path.Combine(_directory, "empty.mp4"), 64, 64, _services.Clock);
        Assert.Throws<IOException>(recorder.Finish);
    }

    [Fact]
    public void HeldFrameCatchupIsBoundedAndFinalizesLongGap()
    {
        using var recorder = Mp4Recorder.Create(Path.Combine(_directory, "held-gap.mp4"), 64, 64, _services.Clock);
        var pixels = new byte[64 * 64 * 4];
        GCHandle video = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            recorder.AppendVideo(video.AddrOfPinnedObject(), 64 * 4, 0);
            recorder.AppendVideo(video.AddrOfPinnedObject(), 64 * 4, 600);
            Assert.Equal(1, recorder.FramesWritten);
            Assert.InRange(recorder.EncodedFrames, 1, 9);
            recorder.Finish();
            Assert.Equal(601, recorder.EncodedFrames);
            Assert.Equal(2, recorder.FramesWritten);
        }
        finally
        {
            video.Free();
        }
    }

    [Theory]
    [InlineData(64, 64, 60, 60, false, 0, 0)]
    [InlineData(1024, 768, 60, 60, false, 0, 0)]
    [InlineData(1024, 768, 240, 162, true, 0, 0)]
    [InlineData(2048, 1450, 240, 230, false, 72, 65)]
    public async Task NativeEncoderFinalizesVideoAndCapturedPcm(int width, int height, int totalFrames, int audioFrameLimit, bool audioBeforeVideo, int audioStartFrame, int startupSkipFrames)
    {
        string path = Path.Combine(_directory, $"encoder-fixture-{width}x{height}-{totalFrames}-{audioBeforeVideo}.mp4");
        using var recorder = Mp4Recorder.Create(path, width, height, _services.Clock);
        var pixels = new byte[width * height * 4];
        var left = new float[800];
        var right = new float[800];
        GCHandle video = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        GCHandle first = GCHandle.Alloc(left, GCHandleType.Pinned);
        GCHandle second = GCHandle.Alloc(right, GCHandleType.Pinned);
        nint[] channelAddresses = [first.AddrOfPinnedObject(), second.AddrOfPinnedObject()];
        GCHandle channels = GCHandle.Alloc(channelAddresses, GCHandleType.Pinned);
        long started = _services.Clock.GetTimestamp();
        try
        {
            for (int frame = 0; frame < totalFrames; frame++)
            {
                Array.Fill(pixels, (byte)((frame % 64) * 4));
                if (frame >= audioStartFrame)
                {
                    FillTone(left, right, frame);
                }

                if (audioBeforeVideo && frame >= audioStartFrame && frame < audioFrameLimit)
                {
                    recorder.AppendAudio(channels.AddrOfPinnedObject(), 2, 800, 48000, 100000 + (frame * 1000 / 60));
                }

                if (frame == 0 || frame > startupSkipFrames)
                {
                    recorder.AppendVideo(video.AddrOfPinnedObject(), width * 4, frame);
                }

                if (!audioBeforeVideo && frame >= audioStartFrame && frame < audioFrameLimit)
                {
                    recorder.AppendAudio(channels.AddrOfPinnedObject(), 2, 800, 48000, 100000 + (frame * 1000 / 60));
                }

                TimeSpan wait = TimeSpan.FromSeconds((frame + 1d) / 60) - _services.Clock.GetElapsedTime(started);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, _services.Clock, CancellationToken.None).ConfigureAwait(true);
                }
            }

            _output.WriteLine($"Submitted {totalFrames} frames; accepted {recorder.FramesWritten}; encoded {recorder.EncodedFrames}; elapsed {_services.Clock.GetElapsedTime(started).TotalMilliseconds:F2} ms at {width}x{height}.");
            recorder.Finish();
        }
        finally
        {
            channels.Free();
            second.Free();
            first.Free();
            video.Free();
        }

        InspectFixture(path);
    }

    [Fact]
    public void AudioBackpressureRetainsPcmAndOverflowPreservesEarlierPackets()
    {
        var queue = AudioPacketQueue.Create(4);
        var first = AudioPacket.Create([0.1f, 0.2f, 0.3f, 0.4f], 2, 0);
        var second = AudioPacket.Create([0.5f, 0.6f, 0.7f, 0.8f], 2, 2);
        queue.Enqueue(first);
        queue.Enqueue(second);
        queue.Drain(_ => false);
        Assert.Equal(2, queue.Count);
        Assert.Throws<IOException>(() => queue.Enqueue(AudioPacket.Create([1f, 1f], 1, 4)));
        var accepted = new List<AudioPacket>();
        queue.Drain(packet =>
        {
            accepted.Add(packet);
            return true;
        });
        Assert.Equal(0, queue.Count);
        Assert.Equal([first, second], accepted);
        Assert.Equal(ExpectedFirstSamples, accepted[0].Samples);
        Assert.Equal(2, accepted[1].Position);
    }

    private static void FillTone(float[] left, float[] right, int frame)
    {
        for (int sample = 0; sample < left.Length; sample++)
        {
            float value = (float)(0.1 * Math.Sin(2 * Math.PI * 440 * ((frame * 800) + sample) / 48000));
            left[sample] = value;
            right[sample] = value;
        }
    }

    private void InspectFixture(string path)
    {
        byte[] encoded = _services.FileSystem.GetFileReader().ReadAllBytes(path);
        Assert.True(encoded.Length > 1000);
        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(encoded, 4, 4));
        Assert.Contains("avc1", System.Text.Encoding.Latin1.GetString(encoded), StringComparison.Ordinal);
        Assert.Contains("mp4a", System.Text.Encoding.Latin1.GetString(encoded), StringComparison.Ordinal);

        string artifacts = Path.Combine(_services.Environment.GetBaseDirectory(), "TestArtifacts");
        _services.FileSystem.GetDirectoryWriter().CreateDirectory(artifacts);
        _services.FileSystem.GetFileWriter().Copy(path, Path.Combine(artifacts, Path.GetFileName(path)), overwrite: true);
    }
}
