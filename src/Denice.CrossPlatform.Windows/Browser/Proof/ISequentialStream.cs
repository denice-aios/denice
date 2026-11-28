// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>ISequentialStream, in vtable order.</summary>
[GeneratedComInterface]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: the COM signature takes unmanaged pointers.")]
[Guid("0c733a30-2a1c-11ce-ade5-00aa0044773d")]
internal unsafe partial interface ISequentialStream
{
    /// <summary>Read.</summary>
    /// <param name="data">The buffer.</param>
    /// <param name="bytes">The bytes asked for.</param>
    /// <param name="read">Receives the bytes read.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int Read(void* data, uint bytes, uint* read);

    /// <summary>Write.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="bytes">Their count.</param>
    /// <param name="written">Receives the bytes written.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int Write(void* data, uint bytes, uint* written);
}
