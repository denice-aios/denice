// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.MacOS.Recording;

/// <summary>Blittable Core Media CMTime value.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct MediaTime
{
    /// <summary>Timestamp numerator.</summary>
    internal readonly long Value;

    /// <summary>Timestamp denominator.</summary>
    internal readonly int Timescale;

    /// <summary>Core Media validity flags.</summary>
    internal readonly uint Flags;

    /// <summary>Timeline epoch.</summary>
    internal readonly long Epoch;

    /// <summary>Initializes a new instance of the <see cref="MediaTime"/> struct.</summary>
    /// <param name="value">The native value.</param>
    /// <param name="timescale">The timestamp timescale.</param>
    internal MediaTime(long value, int timescale)
    {
        Value = value;
        Timescale = timescale;
        Flags = 1;
        Epoch = 0;
    }
}
