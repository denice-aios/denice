// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.MacOS.Recording;

/// <summary>Copies the browser's sole output stream into the MP4's AAC track.</summary>
internal sealed class RecordedAudio : IDisposable
{
    private static readonly float[] SilentSamples = new float[2048];
    private readonly MediaHandle _input;
    private readonly TimeProvider _clock;
    private readonly AudioPacketQueue _pending = AudioPacketQueue.Create(96000);
    private MediaHandle? _description;
    private bool _disposed;
    private long _encodedSamplePosition;
    private long _silenceTarget;

    private RecordedAudio(MediaHandle writer, nint framework, TimeProvider clock)
    {
        _clock = clock;
        nint settings = RecordingNative.Send(RecordingNative.GetClass("NSMutableDictionary"), "dictionary");
        Set(settings, framework, "AVFormatIDKey", 0x61616320);
        Set(settings, framework, "AVSampleRateKey", 48000);
        Set(settings, framework, "AVNumberOfChannelsKey", 2);
        Set(settings, framework, "AVEncoderBitRateKey", 128000);
        nint input = RecordingNative.MessageTwo(RecordingNative.GetClass("AVAssetWriterInput"), RecordingNative.Selector("assetWriterInputWithMediaType:outputSettings:"), RecordingNative.Constant(framework, "AVMediaTypeAudio"), settings);
        _input = MediaHandle.Own(RecordingNative.Send(input, "retain"));
        _ = RecordingNative.Send(_input, "setExpectsMediaDataInRealTime:", 1);
        if (RecordingNative.BoolArgument(writer, RecordingNative.Selector("canAddInput:"), _input) == 0)
        {
            Dispose();
            throw new IOException("The MP4 writer cannot accept AAC audio.");
        }

        _ = RecordingNative.Send(writer, "addInput:", _input);
        var format = new AudioStreamFormat(48000, 2);
        Check(AudioNative.CMAudioFormatDescriptionCreate(0, in format, 0, 0, 0, 0, 0, out nint description));
        _description = MediaHandle.Own(description, coreFoundation: true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        GC.SuppressFinalize(this);
        _disposed = true;
        _pending.Clear();
        _description?.Dispose();
        _description = null;
        _input.Dispose();
    }

    /// <summary>Creates the encoder audio input before writing begins.</summary>
    /// <param name="writer">The owning asset writer.</param>
    /// <param name="framework">The loaded AVFoundation framework.</param>
    /// <param name="clock">The shared recording clock.</param>
    /// <returns>The native result.</returns>
    internal static RecordedAudio Create(MediaHandle writer, nint framework, TimeProvider clock) => new(writer, framework, clock);

    /// <summary>Copies and appends the browser audio samples.</summary>
    /// <param name="planes">The array of planar float channel pointers.</param>
    /// <param name="channels">The channel count.</param>
    /// <param name="frames">The samples per channel.</param>
    /// <param name="sampleRate">The samples per second.</param>
    /// <param name="samplePosition">The first sample timestamp.</param>
    internal void Append(nint planes, int channels, int frames, int sampleRate, long samplePosition)
    {
        if (channels != 2 || sampleRate != 48000)
        {
            throw new IOException("Recording audio requires the configured 48 kHz stereo browser output.");
        }

        var interleaved = new float[checked(frames * channels)];
        for (int channel = 0; channel < channels; channel++)
        {
            var source = new float[frames];
            Marshal.Copy(Marshal.ReadIntPtr(planes, channel * nint.Size), source, 0, frames);
            for (int frame = 0; frame < frames; frame++)
            {
                interleaved[(frame * channels) + channel] = source[frame];
            }
        }

        _pending.Enqueue(AudioPacket.Create(interleaved, frames, samplePosition));
        Drain();
    }

    /// <summary>Submits queued PCM while the native encoder can accept it.</summary>
    internal void Drain()
    {
        _pending.Drain(TryEncode);
        if (_pending.Count == 0)
        {
            _ = FillSilence(_silenceTarget);
        }
    }

    /// <summary>Extends known inactive output with silence without allocating a gap-sized buffer.</summary>
    /// <param name="samplePosition">The end of the known silent interval.</param>
    internal void AdvanceSilenceTo(long samplePosition)
    {
        _silenceTarget = Math.Max(_silenceTarget, samplePosition);
        Drain();
    }

    /// <summary>Drains captured PCM before marking the track complete.</summary>
    internal void Finish()
    {
        long started = _clock.GetTimestamp();
        while (_pending.Count > 0 || _encodedSamplePosition < _silenceTarget)
        {
            Drain();
            if (_pending.Count > 0 || _encodedSamplePosition < _silenceTarget)
            {
                if (_clock.GetElapsedTime(started) > TimeSpan.FromSeconds(5))
                {
                    throw new IOException("The audio encoder did not drain within five seconds; recoverable movie fragments are retained.");
                }

                Task.Delay(TimeSpan.FromMilliseconds(1), _clock).GetAwaiter().GetResult();
            }
        }

        _ = RecordingNative.Send(_input, "markAsFinished");
    }

    private static void Set(nint settings, nint framework, string key, long value) =>
        _ = RecordingNative.MessageTwo(settings, RecordingNative.Selector("setObject:forKey:"), RecordingNative.Number(value), RecordingNative.Constant(framework, key));

    private static void Check(int status)
    {
        if (status != 0)
        {
            throw new IOException($"Core Media audio recording failed: {status}.");
        }
    }

    private bool TryEncode(AudioPacket packet)
    {
        if (!FillSilence(packet.Position))
        {
            return false;
        }

        if (packet.Position < _encodedSamplePosition)
        {
            throw new IOException("Captured audio overlaps samples already submitted to the encoder.");
        }

        if (RecordingNative.BoolMessage(_input, RecordingNative.Selector("isReadyForMoreMediaData")) == 0)
        {
            return false;
        }

        Encode(packet);
        _encodedSamplePosition = packet.Position + packet.Frames;
        return true;
    }

    private bool FillSilence(long target)
    {
        int submitted = 0;
        while (_encodedSamplePosition < target)
        {
            if (submitted == 8 || RecordingNative.BoolMessage(_input, RecordingNative.Selector("isReadyForMoreMediaData")) == 0)
            {
                return false;
            }

            int frames = checked((int)Math.Min(1024, target - _encodedSamplePosition));
            Encode(AudioPacket.Create(SilentSamples, frames, _encodedSamplePosition));
            _encodedSamplePosition += frames;
            submitted++;
        }

        return true;
    }

    private void Encode(AudioPacket packet)
    {
        float[] interleaved = packet.Samples;
        nuint bytes = checked((nuint)packet.Frames * 8);
        Check(AudioNative.CMBlockBufferCreateWithMemoryBlock(0, 0, bytes, 0, 0, 0, bytes, 1, out nint block));
        try
        {
            GCHandle pinned = GCHandle.Alloc(interleaved, GCHandleType.Pinned);
            try
            {
                Check(AudioNative.CMBlockBufferReplaceDataBytes(pinned.AddrOfPinnedObject(), block, 0, bytes));
            }
            finally
            {
                pinned.Free();
            }

            var timing = new AudioSampleTiming(packet.Position, 48000);
            nuint sampleSize = 8;
            Check(AudioNative.CMSampleBufferCreateReady(0, block, _description!, packet.Frames, 1, in timing, 1, in sampleSize, out nint sampleBuffer));
            try
            {
                if (RecordingNative.BoolArgument(_input, RecordingNative.Selector("appendSampleBuffer:"), sampleBuffer) == 0)
                {
                    throw new IOException("Cannot append recorded audio sample.");
                }
            }
            finally
            {
                RecordingNative.CFRelease(sampleBuffer);
            }
        }
        finally
        {
            RecordingNative.CFRelease(block);
        }
    }
}
