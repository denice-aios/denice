// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Windows;

/// <summary>
/// The blittable <c>OVERLAPPED</c> <c>LockFileEx</c> reads the locked range's start offset from. Field order and types
/// mirror the Windows SDK header (<c>minwinbase.h</c>), with the offset union spelled as its two <c>DWORD</c>s; sequential
/// layout reproduces its native alignment.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Overlapped
{
    /// <summary>Internal: the status code of the request (set by the system).</summary>
    public UIntPtr Internal;

    /// <summary>InternalHigh: the bytes transferred (set by the system).</summary>
    public UIntPtr InternalHigh;

    /// <summary>Offset: the low 32 bits of the start offset.</summary>
    public uint Offset;

    /// <summary>OffsetHigh: the high 32 bits of the start offset.</summary>
    public uint OffsetHigh;

    /// <summary>hEvent: an event to signal, or <see cref="IntPtr.Zero"/> for none.</summary>
    public IntPtr Event;
}
