// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>IMMDeviceEnumerator (mmdeviceapi.h), in vtable order.</summary>
[GeneratedComInterface]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: the COM signature takes unmanaged pointers.")]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal unsafe partial interface IMMDeviceEnumerator
{
    /// <summary>EnumAudioEndpoints.</summary>
    /// <param name="flow">The EDataFlow.</param>
    /// <param name="stateMask">The device states.</param>
    /// <param name="devices">Receives an IMMDeviceCollection.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int EnumAudioEndpoints(int flow, uint stateMask, nint* devices);

    /// <summary>GetDefaultAudioEndpoint.</summary>
    /// <param name="flow">The EDataFlow.</param>
    /// <param name="role">The ERole.</param>
    /// <param name="endpoint">Receives an IMMDevice.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetDefaultAudioEndpoint(int flow, int role, nint* endpoint);

    /// <summary>GetDevice.</summary>
    /// <param name="id">The endpoint id string.</param>
    /// <param name="device">Receives an IMMDevice.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetDevice(nint id, nint* device);

    /// <summary>RegisterEndpointNotificationCallback.</summary>
    /// <param name="client">The IMMNotificationClient.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int RegisterEndpointNotificationCallback(nint client);

    /// <summary>UnregisterEndpointNotificationCallback.</summary>
    /// <param name="client">The IMMNotificationClient.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int UnregisterEndpointNotificationCallback(nint client);
}
