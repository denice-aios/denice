// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>IMMDeviceCollection, in vtable order.</summary>
[GeneratedComInterface]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: the COM signature takes unmanaged pointers.")]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
internal unsafe partial interface IMMDeviceCollection
{
    /// <summary>GetCount.</summary>
    /// <param name="count">Receives the count.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetCount(uint* count);

    /// <summary>Item.</summary>
    /// <param name="index">The index.</param>
    /// <param name="device">Receives an IMMDevice.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int Item(uint index, nint* device);
}
