// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>IAudioClock, in vtable order.</summary>
[GeneratedComInterface]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: the COM signature takes unmanaged pointers.")]
[Guid("CD63314F-3FBA-4a1b-812C-EF96358728E7")]
internal unsafe partial interface IAudioClock
{
    /// <summary>GetFrequency.</summary>
    /// <param name="frequency">Receives positions per second.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetFrequency(ulong* frequency);

    /// <summary>GetPosition.</summary>
    /// <param name="position">Receives the position.</param>
    /// <param name="counter">Receives the QPC time in 100 ns units.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetPosition(ulong* position, ulong* counter);

    /// <summary>GetCharacteristics.</summary>
    /// <param name="characteristics">Receives zero.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetCharacteristics(uint* characteristics);
}
