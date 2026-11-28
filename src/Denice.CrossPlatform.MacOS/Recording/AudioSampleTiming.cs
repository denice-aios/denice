// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.MacOS.Recording;

/// <summary>Core Media timing for a packet of equally spaced PCM samples.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct AudioSampleTiming
{
    /// <summary>Duration of one PCM sample.</summary>
    internal readonly MediaTime Duration;

    /// <summary>Presentation time of the first sample.</summary>
    internal readonly MediaTime Presentation;

    /// <summary>Invalid decode timestamp for uncompressed audio.</summary>
    internal readonly MediaTime Decode;

    /// <summary>Initializes a new instance of the <see cref="AudioSampleTiming"/> struct.</summary>
    /// <param name="samplePosition">The first sample timestamp.</param>
    /// <param name="sampleRate">The samples per second.</param>
    internal AudioSampleTiming(long samplePosition, int sampleRate)
    {
        Duration = new MediaTime(1, sampleRate);
        Presentation = new MediaTime(samplePosition, sampleRate);
        Decode = default;
    }
}
