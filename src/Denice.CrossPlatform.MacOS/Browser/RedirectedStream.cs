// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.MacOS.Browser;

/// <summary>One Chromium output stream the redirection has taken over, with what the mixer needs to pull its samples.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio interop: render callbacks exchange stable unmanaged buffers with Chromium.")]
internal sealed unsafe class RedirectedStream : IRedirectedSource, IDisposable
{
    private const int MaximumFrames = 4096;
    private const int MaximumChannels = 8;
    private readonly nint _bufferList;
    private readonly nint[] _channels = new nint[MaximumChannels];
    private bool _disposed;

    private RedirectedStream(nint unit)
    {
        Unit = unit;

        // AudioBufferList: a 4-byte count padded to 8, then one 16-byte AudioBuffer per channel.
        _bufferList = Marshal.AllocHGlobal(8 + (16 * MaximumChannels));
        for (int channel = 0; channel < MaximumChannels; channel++)
        {
            _channels[channel] = Marshal.AllocHGlobal(MaximumFrames * sizeof(float));
        }
    }

    /// <summary>Gets the largest frame count one pull may request.</summary>
    internal static int FrameCapacity => MaximumFrames;

    /// <summary>Gets the real, never-started HAL output unit Chromium created.</summary>
    internal nint Unit { get; }

    /// <summary>Gets or sets the render callback and its context that Chromium passed with kAudioUnitProperty_SetRenderCallback.</summary>
    internal RenderCallbackTarget RenderCallback { get; set; }

    /// <summary>Gets or sets the stream format Chromium set: the device's rate, 1 to 8 non-interleaved float channels.</summary>
    internal StreamFormat Format { get; set; }

    /// <summary>Gets or sets the channel labels Chromium set with kAudioUnitProperty_AudioChannelLayout, if any.</summary>
    internal uint[] ChannelLabels { get; set; } = [];

    /// <summary>Gets or sets the stream's converter to 48 kHz stereo, or null when it is already 48 kHz stereo.</summary>
    internal StreamRateConverter? Converter { get; set; }

    /// <summary>Gets or sets the output device Chromium selected, default or explicit.</summary>
    internal uint Device { get; set; }

    /// <summary>Gets or sets where the stream is in its lifetime.</summary>
    internal StreamState State { get; set; }

#if DENICE_AUDIO_DIAGNOSTICS
    /// <summary>Gets or sets the mixer cycle of the first pull, or -1; written only by the render thread.</summary>
    internal long FirstPullCycle { get; set; } = -1;

    /// <summary>Gets or sets the mixer cycle of the latest pull, or -1.</summary>
    internal long LastPullCycle { get; set; } = -1;

    /// <summary>Gets or sets the first mixer cycle whose pulled samples were not all zero, or -1.</summary>
    internal long FirstSoundCycle { get; set; } = -1;

    /// <summary>Gets or sets the latest mixer cycle whose pulled samples were not all zero, or -1.</summary>
    internal long LastSoundCycle { get; set; } = -1;

    /// <summary>Gets or sets the frame within that cycle where the stream's first non-zero sample sits, or -1.</summary>
    internal int FirstSoundFrame { get; set; } = -1;

    /// <summary>Gets or sets the frame within its last sounding cycle after which the stream's samples stayed zero.</summary>
    internal int LastSoundFrameEnd { get; set; } = -1;
#endif

    /// <summary>Gets or sets the Core Audio cycle whose AudioTimeStamp this stream's callback receives; set at registration.</summary>
    internal CoreAudioCycle? Cycle { get; set; }

    /// <summary>Gets or sets the stream's latest registration in the mixer.</summary>
    internal MixerClaim? Claim { get; set; }

    /// <summary>Gets a value indicating whether the render thread is inside this stream's Chromium callback.</summary>
    internal bool InCallback => Claim?.InCallback ?? false;

    /// <summary>Gets the preallocated non-interleaved buffer list handed to Chromium's render callback.</summary>
    internal nint BufferList => _bufferList;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Converter?.Dispose();
        foreach (nint channel in _channels)
        {
            Marshal.FreeHGlobal(channel);
        }

        Marshal.FreeHGlobal(_bufferList);
    }

    /// <inheritdoc />
    public void MixInto(Span<float> left, Span<float> right, long hostNanoseconds, long cycle)
    {
        // Called with the stream's claim held, so Chromium has not freed the callback's context.
        nint procedure = RenderCallback.Procedure;
        nint timestamp = Cycle?.Timestamp ?? 0;
        if (procedure == 0 || timestamp == 0)
        {
            return;
        }

        int frames = Math.Min(left.Length, right.Length);
        if (Converter is { } converter)
        {
            _ = converter.Pull(timestamp, frames);
            Add(left, right, converter.Left, converter.Right, frames);
            NotePull(cycle, converter.Left, converter.Right, frames);
            return;
        }

        int channels = (int)Math.Clamp(Format.ChannelsPerFrame, 1, 8);
        PrepareBufferList(channels, frames);
        uint flags = 0;
        int status = ((delegate* unmanaged<nint, uint*, nint, uint, uint, nint, int>)procedure)(RenderCallback.Context, &flags, timestamp, 0, (uint)frames, _bufferList);
        if (status != 0)
        {
            return;
        }

        float* first = (float*)Channel(0);
        float* second = (float*)Channel(channels > 1 ? 1 : 0);
        Add(left, right, first, second, frames);
        NotePull(cycle, first, second, frames);
    }

    /// <summary>Creates a tracked stream for a new HAL output unit.</summary>
    /// <param name="unit">The real unit.</param>
    /// <returns>The tracked stream.</returns>
    internal static RedirectedStream Create(nint unit) => new(unit);

    /// <summary>Gets one channel's buffer.</summary>
    /// <param name="channel">The channel index.</param>
    /// <returns>The buffer address.</returns>
    internal nint Channel(int channel) => _channels[channel];

    /// <summary>Points the buffer list at this stream's channel buffers for one pull.</summary>
    /// <param name="channels">The channel count.</param>
    /// <param name="frames">The frames requested.</param>
    internal void PrepareBufferList(int channels, int frames)
    {
        *(int*)_bufferList = channels;
        for (int channel = 0; channel < channels; channel++)
        {
            nint buffer = _bufferList + 8 + (16 * channel);
            *(int*)buffer = 1;
            *(int*)(buffer + 4) = frames * sizeof(float);
            *(nint*)(buffer + 8) = _channels[channel];
        }
    }

    /// <summary>Records one pull for diagnostic builds; removed from normal builds. Render thread: plain stores only.</summary>
    /// <param name="cycle">The mixer cycle.</param>
    /// <param name="first">The first channel.</param>
    /// <param name="second">The second channel.</param>
    /// <param name="frames">The frames pulled.</param>
    [System.Diagnostics.Conditional("DENICE_AUDIO_DIAGNOSTICS")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Uses instance data in diagnostic builds (DeniceAudioDiagnostics); empty otherwise.")]
    internal void NotePull(long cycle, float* first, float* second, int frames)
    {
#if DENICE_AUDIO_DIAGNOSTICS
        int firstSound = -1;
        int lastSound = -1;
        for (int frame = 0; frame < frames; frame++)
        {
            if (first[frame] != 0 || second[frame] != 0)
            {
                lastSound = frame;
                firstSound = firstSound < 0 ? frame : firstSound;
            }
        }

        if (FirstPullCycle < 0)
        {
            FirstPullCycle = cycle;
        }

        LastPullCycle = cycle;
        if (lastSound >= 0)
        {
            if (FirstSoundCycle < 0)
            {
                FirstSoundCycle = cycle;
                FirstSoundFrame = firstSound;
            }

            LastSoundCycle = cycle;
            LastSoundFrameEnd = lastSound + 1;
        }
#endif
    }

    private static void Add(Span<float> left, Span<float> right, float* first, float* second, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            left[frame] += first[frame];
            right[frame] += second[frame];
        }
    }

    /// <summary>The render callback and context Chromium registered.</summary>
    /// <param name="Procedure">The AURenderCallback.</param>
    /// <param name="Context">Its refCon.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct RenderCallbackTarget(nint Procedure, nint Context);

    /// <summary>The input-scope stream format Chromium set.</summary>
    /// <param name="SampleRate">The sample rate.</param>
    /// <param name="Flags">The format flags.</param>
    /// <param name="ChannelsPerFrame">The channel count.</param>
    /// <param name="BitsPerChannel">The bits per sample.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct StreamFormat(double SampleRate, uint Flags, uint ChannelsPerFrame, uint BitsPerChannel);
}
