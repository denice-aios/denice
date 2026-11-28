// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>IStream, in vtable order.</summary>
[GeneratedComInterface]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: the COM signature takes unmanaged pointers.")]
[Guid("0000000c-0000-0000-C000-000000000046")]
internal unsafe partial interface IStream : ISequentialStream
{
    /// <summary>Seek.</summary>
    /// <param name="move">The offset.</param>
    /// <param name="origin">The STREAM_SEEK origin.</param>
    /// <param name="position">Receives the new position.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int Seek(long move, uint origin, ulong* position);

    /// <summary>SetSize.</summary>
    /// <param name="size">The size.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int SetSize(ulong size);

    /// <summary>CopyTo.</summary>
    /// <param name="target">The target stream.</param>
    /// <param name="bytes">The bytes.</param>
    /// <param name="read">Receives the bytes read.</param>
    /// <param name="written">Receives the bytes written.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int CopyTo(nint target, ulong bytes, ulong* read, ulong* written);

    /// <summary>Commit.</summary>
    /// <param name="flags">The STGC flags.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int Commit(uint flags);

    /// <summary>Revert.</summary>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int Revert();

    /// <summary>LockRegion.</summary>
    /// <param name="offset">The offset.</param>
    /// <param name="bytes">The bytes.</param>
    /// <param name="kind">The lock type.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int LockRegion(ulong offset, ulong bytes, uint kind);

    /// <summary>UnlockRegion.</summary>
    /// <param name="offset">The offset.</param>
    /// <param name="bytes">The bytes.</param>
    /// <param name="kind">The lock type.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int UnlockRegion(ulong offset, ulong bytes, uint kind);

    /// <summary>Stat.</summary>
    /// <param name="statistics">Receives the STATSTG.</param>
    /// <param name="flags">The STATFLAG.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int Stat(void* statistics, uint flags);

    /// <summary>Clone.</summary>
    /// <param name="clone">Receives the clone.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int Clone(nint* clone);
}
