// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>ISpStreamFormat (sapi.h), in vtable order.</summary>
[GeneratedComInterface]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: the COM signature takes unmanaged pointers.")]
[Guid("BED530BE-2606-4F4D-A1C0-54C5CDA5566F")]
internal unsafe partial interface ISpStreamFormat : IStream
{
    /// <summary>GetFormat.</summary>
    /// <param name="formatId">Receives the format GUID.</param>
    /// <param name="format">Receives a CoTaskMem WAVEFORMATEX.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetFormat(Guid* formatId, nint* format);
}
