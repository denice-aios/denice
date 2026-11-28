// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>IMMEndpoint, in vtable order.</summary>
[GeneratedComInterface]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: the COM signature takes unmanaged pointers.")]
[Guid("1BE09788-6894-4089-8586-9A2A6C265AC5")]
internal unsafe partial interface IMMEndpoint
{
    /// <summary>GetDataFlow.</summary>
    /// <param name="flow">Receives the EDataFlow.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetDataFlow(int* flow);
}
