// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>IMMDevice, in vtable order.</summary>
[GeneratedComInterface]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: the COM signature takes unmanaged pointers.")]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal unsafe partial interface IMMDevice
{
    /// <summary>Activate.</summary>
    /// <param name="iid">The interface asked for.</param>
    /// <param name="context">The CLSCTX.</param>
    /// <param name="parameters">The activation PROPVARIANT.</param>
    /// <param name="instance">Receives the interface.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int Activate(Guid* iid, uint context, nint parameters, nint* instance);

    /// <summary>OpenPropertyStore.</summary>
    /// <param name="access">The STGM access.</param>
    /// <param name="store">Receives an IPropertyStore.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int OpenPropertyStore(uint access, nint* store);

    /// <summary>GetId.</summary>
    /// <param name="id">Receives a CoTaskMem string.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetId(nint* id);

    /// <summary>GetState.</summary>
    /// <param name="state">Receives the DEVICE_STATE.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetState(uint* state);
}
