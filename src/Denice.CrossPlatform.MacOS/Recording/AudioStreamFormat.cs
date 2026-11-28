// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.MacOS.Recording;

/// <summary>Core Audio's interleaved 48 kHz stereo float PCM format.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct AudioStreamFormat
{
    /// <summary>Samples per second.</summary>
    internal readonly double SampleRate;

    /// <summary>Linear PCM four-character code.</summary>
    internal readonly uint Format;

    /// <summary>Float and packed format flags.</summary>
    internal readonly uint Flags;

    /// <summary>Interleaved bytes per packet.</summary>
    internal readonly uint BytesPerPacket;

    /// <summary>One frame per PCM packet.</summary>
    internal readonly uint FramesPerPacket;

    /// <summary>Interleaved bytes per frame.</summary>
    internal readonly uint BytesPerFrame;

    /// <summary>Number of interleaved channels.</summary>
    internal readonly uint Channels;

    /// <summary>Bits per floating-point sample.</summary>
    internal readonly uint Bits;

    /// <summary>Reserved ABI padding.</summary>
    internal readonly uint Reserved;

    /// <summary>Initializes a new instance of the <see cref="AudioStreamFormat"/> struct.</summary>
    /// <param name="sampleRate">The samples per second.</param>
    /// <param name="channels">The channel count.</param>
    internal AudioStreamFormat(int sampleRate, int channels)
    {
        SampleRate = sampleRate;
        Format = 0x6C70636D;
        Flags = 9;
        BytesPerPacket = checked((uint)channels * 4);
        FramesPerPacket = 1;
        BytesPerFrame = BytesPerPacket;
        Channels = (uint)channels;
        Bits = 32;
        Reserved = 0;
    }
}
