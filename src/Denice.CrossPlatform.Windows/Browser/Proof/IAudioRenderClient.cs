// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>IAudioRenderClient, in vtable order.</summary>
[GeneratedComInterface]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: the COM signature takes unmanaged pointers.")]
[Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
internal unsafe partial interface IAudioRenderClient
{
    /// <summary>GetBuffer.</summary>
    /// <param name="frames">The frames asked for.</param>
    /// <param name="data">Receives the buffer.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetBuffer(uint frames, nint* data);

    /// <summary>ReleaseBuffer.</summary>
    /// <param name="frames">The frames written.</param>
    /// <param name="flags">The AUDCLNT_BUFFERFLAGS.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int ReleaseBuffer(uint frames, uint flags);
}
