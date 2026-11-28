// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.MacOS.Browser;

/// <summary>
/// Turns one Chromium stream at its own rate and channel layout into 48 kHz stereo for the mixer (approved per-stream
/// conversion amendment). Channels are folded to stereo first; a stream at another rate is then converted with
/// AudioToolbox's AudioConverter, which pulls Chromium's render callback from its input procedure. Everything the render
/// thread touches is allocated here, on the control thread.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio interop: AudioConverter and Chromium callbacks exchange stable unmanaged buffers and function pointers.")]
internal sealed unsafe partial class StreamRateConverter : IDisposable
{
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const uint QualityProperty = 0x73726371; // 'srcq'
    private const uint QualityHigh = 0x60;
    private const uint PrimeMethodProperty = 0x70726D6D; // 'prmm'
    private const uint PrimeMethodNormal = 1;
    private const uint PrimeInfoProperty = 0x7072696D; // 'prim'
    private const uint SampleTimeValid = 1;
    private const uint HostTimeValid = 2;
    private const int TimestampSize = 64;
    private const int MaximumFrames = 4096;
    private const float Half = 0.70710678f;
    private readonly RedirectedStream _stream;
    private readonly float[] _leftGains;
    private readonly float[] _rightGains;
    private readonly nint _foldedLeft;
    private readonly nint _foldedRight;
    private readonly nint _outputLeft;
    private readonly nint _outputRight;
    private readonly nint _outputList;
    private readonly nint _timestamp;
    private readonly nint _converter;
    private GCHandle _self;
    private ulong _hostTime;
    private double _sampleTime;
    private bool _disposed;

    private StreamRateConverter(RedirectedStream stream, float[] leftGains, float[] rightGains)
    {
        _stream = stream;
        _leftGains = leftGains;
        _rightGains = rightGains;
        _foldedLeft = Marshal.AllocHGlobal(MaximumFrames * sizeof(float));
        _foldedRight = Marshal.AllocHGlobal(MaximumFrames * sizeof(float));
        _outputLeft = Marshal.AllocHGlobal(MaximumFrames * sizeof(float));
        _outputRight = Marshal.AllocHGlobal(MaximumFrames * sizeof(float));
        _outputList = Marshal.AllocHGlobal(8 + (16 * 2));
        _timestamp = Marshal.AllocHGlobal(TimestampSize);
        new Span<byte>((void*)_timestamp, TimestampSize).Clear();
        *(uint*)(_timestamp + 56) = SampleTimeValid | HostTimeValid;
        double rate = stream.Format.SampleRate;
        if (Math.Abs(rate - BrowserAudioMixer.SampleRate) > 0.5)
        {
            _converter = CreateConverter(rate);
            _self = GCHandle.Alloc(this);
            uint leading = 0;
            uint trailing = 0;
            uint size = 8;
            var info = stackalloc uint[2];
            if (AudioConverterGetProperty(_converter, PrimeInfoProperty, &size, info) == 0)
            {
                leading = info[0];
                trailing = info[1];
            }

            PrimeLeadingFrames = (int)leading;
            PrimeTrailingFrames = (int)trailing;
        }
    }

    /// <summary>Gets the stream's input rate.</summary>
    internal double SourceRate => _stream.Format.SampleRate;

    /// <summary>Gets the stream's channel count.</summary>
    internal uint Channels => (uint)_leftGains.Length;

    /// <summary>Gets a value indicating whether the stream is resampled (otherwise only folded to stereo).</summary>
    internal bool Resamples => _converter != 0;

    /// <summary>Gets the converter's reported leading priming frames, measured from kAudioConverterPrimeInfo.</summary>
    internal int PrimeLeadingFrames { get; }

    /// <summary>Gets the converter's reported trailing priming frames.</summary>
    internal int PrimeTrailingFrames { get; }

    /// <summary>Gets the stream frames Chromium has delivered so far.</summary>
    internal long SourceFrames => (long)_sampleTime;

    /// <summary>Gets the left output for the last pull.</summary>
    internal float* Left => (float*)_outputLeft;

    /// <summary>Gets the right output for the last pull.</summary>
    internal float* Right => (float*)_outputRight;

    /// <summary>Gets the layout that was folded to its first two channels because it is not understood, if any.</summary>
    internal string? UnknownLayout { get; private init; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_converter != 0)
        {
            _ = AudioConverterDispose(_converter);
        }

        if (_self.IsAllocated)
        {
            _self.Free();
        }

        Marshal.FreeHGlobal(_foldedLeft);
        Marshal.FreeHGlobal(_foldedRight);
        Marshal.FreeHGlobal(_outputLeft);
        Marshal.FreeHGlobal(_outputRight);
        Marshal.FreeHGlobal(_outputList);
        Marshal.FreeHGlobal(_timestamp);
    }

    /// <summary>Whether a stream needs a converter: anything but 48 kHz stereo.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>Whether to build one.</returns>
    internal static bool IsNeeded(RedirectedStream stream) =>
        Math.Abs(stream.Format.SampleRate - BrowserAudioMixer.SampleRate) > 0.5 || stream.Format.ChannelsPerFrame != 2;

    /// <summary>Control thread: builds the converter for a stream's current format and channel layout.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The converter.</returns>
    internal static StreamRateConverter Create(RedirectedStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        int channels = (int)stream.Format.ChannelsPerFrame;
        if (channels is < 1 or > 8)
        {
            throw new InvalidOperationException($"A browser audio stream has {channels} channels; 1 to 8 are supported.");
        }

        FoldPlan plan = FoldGains(stream.ChannelLabels, channels);
        return new StreamRateConverter(stream, plan.Left, plan.Right) { UnknownLayout = plan.Unknown };
    }

    /// <summary>Render thread: produces the cycle's 48 kHz stereo frames into <see cref="Left"/> and <see cref="Right"/>.</summary>
    /// <param name="mixerTimestamp">The mixer cycle's AudioTimeStamp, whose host time is passed on to Chromium.</param>
    /// <param name="frames">The frames to produce.</param>
    /// <returns>Whether the frames were produced.</returns>
    internal bool Pull(nint mixerTimestamp, int frames)
    {
        _hostTime = *(ulong*)(mixerTimestamp + 8);
        if (_converter == 0)
        {
            return Fold(frames, (float*)_outputLeft, (float*)_outputRight);
        }

        *(int*)_outputList = 2;
        SetBuffer(_outputList, 0, _outputLeft, frames);
        SetBuffer(_outputList, 1, _outputRight, frames);
        uint produced = (uint)frames;
        int status = AudioConverterFillComplexBuffer(_converter, (nint)(delegate* unmanaged<nint, uint*, nint, nint*, nint, int>)&ProvideInput, GCHandle.ToIntPtr(_self), &produced, _outputList, 0);
        if (status != 0 || produced < frames)
        {
            new Span<float>((void*)(_outputLeft + ((int)produced * sizeof(float))), frames - (int)produced).Clear();
            new Span<float>((void*)(_outputRight + ((int)produced * sizeof(float))), frames - (int)produced).Clear();
        }

        return status == 0;
    }

    /// <summary>Control thread: forgets the converter's history so samples from before a stop are not replayed.</summary>
    internal void Reset()
    {
        if (_converter != 0)
        {
            _ = AudioConverterReset(_converter);
        }
    }

    private static FoldPlan FoldGains(uint[] labels, int channels)
    {
        var left = new float[channels];
        var right = new float[channels];
        if (channels == 1)
        {
            left[0] = right[0] = 1;
            return new FoldPlan(left, right, null);
        }

        bool understood = labels.Length == channels;
        for (int channel = 0; understood && channel < channels; channel++)
        {
            switch (labels[channel])
            {
                case 1 or 7: left[channel] = 1; break; // Left, LeftCenter
                case 2 or 8: right[channel] = 1; break; // Right, RightCenter
                case 3 or 9 or 42: left[channel] = right[channel] = Half; break; // Center, CenterSurround, Mono
                case 5 or 10 or 33: left[channel] = Half; break; // LeftSurround, LeftSurroundDirect, RearSurroundLeft
                case 6 or 11 or 34: right[channel] = Half; break; // RightSurround, RightSurroundDirect, RearSurroundRight
                case 4: break; // LFEScreen, dropped
                default: understood = false; break;
            }
        }

        if (understood || (channels == 2 && labels.Length == 0))
        {
            if (!understood)
            {
                left[0] = right[1] = 1;
            }

            return new FoldPlan(left, right, null);
        }

        Array.Clear(left);
        Array.Clear(right);
        left[0] = 1;
        right[1] = 1;
        return new FoldPlan(left, right, $"{channels} channels with labels [{string.Join(", ", labels)}]");
    }

    private static void SetBuffer(nint list, int index, nint data, int frames)
    {
        nint buffer = list + 8 + (16 * index);
        *(int*)buffer = 1;
        *(int*)(buffer + 4) = frames * sizeof(float);
        *(nint*)(buffer + 8) = data;
    }

    [UnmanagedCallersOnly]
    private static int ProvideInput(nint converter, uint* packets, nint data, nint* descriptions, nint context)
    {
        // AudioConverter's input procedure, on the render thread: one Chromium pull, folded to stereo.
        if (GCHandle.FromIntPtr(context).Target is not StreamRateConverter self)
        {
            *packets = 0;
            return -50;
        }

        int frames = (int)Math.Min(*packets, MaximumFrames);

        // A failed Chromium pull is zero-filled by Fold; the converter still receives the frames it asked for.
        _ = self.Fold(frames, (float*)self._foldedLeft, (float*)self._foldedRight);
        *(int*)data = 2;
        SetBuffer(data, 0, self._foldedLeft, frames);
        SetBuffer(data, 1, self._foldedRight, frames);
        *packets = (uint)frames;
        return 0;
    }

    private static nint CreateConverter(double rate)
    {
        var input = new AudioOutputRedirection.StreamDescription(rate, 0x6C70636D, 0x29, 4, 1, 4, 2, 32, 0);
        var output = new AudioOutputRedirection.StreamDescription(BrowserAudioMixer.SampleRate, 0x6C70636D, 0x29, 4, 1, 4, 2, 32, 0);
        nint converter;
        int status = AudioConverterNew(&input, &output, &converter);
        if (status != 0)
        {
            throw new InvalidOperationException($"AudioConverterNew failed for {rate} Hz: OSStatus {status}.");
        }

        uint quality = QualityHigh;
        uint prime = PrimeMethodNormal;
        _ = AudioConverterSetProperty(converter, QualityProperty, sizeof(uint), &quality);
        _ = AudioConverterSetProperty(converter, PrimeMethodProperty, sizeof(uint), &prime);
        return converter;
    }

    [LibraryImport(AudioToolbox)]
    private static partial int AudioConverterNew(AudioOutputRedirection.StreamDescription* source, AudioOutputRedirection.StreamDescription* destination, nint* converter);

    [LibraryImport(AudioToolbox)]
    private static partial int AudioConverterDispose(nint converter);

    [LibraryImport(AudioToolbox)]
    private static partial int AudioConverterReset(nint converter);

    [LibraryImport(AudioToolbox)]
    private static partial int AudioConverterSetProperty(nint converter, uint property, uint size, void* data);

    [LibraryImport(AudioToolbox)]
    private static partial int AudioConverterGetProperty(nint converter, uint property, uint* size, void* data);

    [LibraryImport(AudioToolbox)]
    private static partial int AudioConverterFillComplexBuffer(nint converter, nint inputProcedure, nint context, uint* packets, nint output, nint descriptions);

    private bool Fold(int frames, float* left, float* right)
    {
        // Calls Chromium's render callback for this stream's own frames, with a sample time in those frames.
        RedirectedStream stream = _stream;
        int channels = _leftGains.Length;
        stream.PrepareBufferList(channels, frames);
        *(double*)_timestamp = _sampleTime;
        *(ulong*)(_timestamp + 8) = _hostTime;
        uint flags = 0;
        int status = ((delegate* unmanaged<nint, uint*, nint, uint, uint, nint, int>)stream.RenderCallback.Procedure)(stream.RenderCallback.Context, &flags, _timestamp, 0, (uint)frames, stream.BufferList);
        _sampleTime += frames;
        if (status != 0)
        {
            new Span<float>(left, frames).Clear();
            new Span<float>(right, frames).Clear();
            return false;
        }

        new Span<float>(left, frames).Clear();
        new Span<float>(right, frames).Clear();
        for (int channel = 0; channel < channels; channel++)
        {
            float l = _leftGains[channel];
            float r = _rightGains[channel];
            if (l == 0 && r == 0)
            {
                continue;
            }

            float* source = (float*)stream.Channel(channel);
            for (int frame = 0; frame < frames; frame++)
            {
                left[frame] += source[frame] * l;
                right[frame] += source[frame] * r;
            }
        }

        return true;
    }

    /// <summary>How each channel contributes to left and right.</summary>
    /// <param name="Left">The left gain per channel.</param>
    /// <param name="Right">The right gain per channel.</param>
    /// <param name="Unknown">The layout, when it was not understood.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct FoldPlan(float[] Left, float[] Right, string? Unknown);
}
