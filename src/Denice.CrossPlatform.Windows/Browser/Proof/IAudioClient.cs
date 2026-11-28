// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>IAudioClient (audioclient.h), in vtable order.</summary>
[GeneratedComInterface]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: the COM signature takes unmanaged pointers.")]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
internal unsafe partial interface IAudioClient
{
    /// <summary>Initialize.</summary>
    /// <param name="shareMode">The AUDCLNT_SHAREMODE.</param>
    /// <param name="flags">The AUDCLNT_STREAMFLAGS.</param>
    /// <param name="bufferDuration">The buffer duration in 100 ns.</param>
    /// <param name="periodicity">The period in 100 ns.</param>
    /// <param name="format">The WAVEFORMATEX.</param>
    /// <param name="session">The session GUID.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int Initialize(int shareMode, uint flags, long bufferDuration, long periodicity, nint format, Guid* session);

    /// <summary>GetBufferSize.</summary>
    /// <param name="frames">Receives the frames.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetBufferSize(uint* frames);

    /// <summary>GetStreamLatency.</summary>
    /// <param name="latency">Receives 100 ns units.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetStreamLatency(long* latency);

    /// <summary>GetCurrentPadding.</summary>
    /// <param name="frames">Receives the frames queued.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetCurrentPadding(uint* frames);

    /// <summary>IsFormatSupported.</summary>
    /// <param name="shareMode">The AUDCLNT_SHAREMODE.</param>
    /// <param name="format">The WAVEFORMATEX.</param>
    /// <param name="closest">Receives the closest match.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int IsFormatSupported(int shareMode, nint format, nint* closest);

    /// <summary>GetMixFormat.</summary>
    /// <param name="format">Receives a CoTaskMem WAVEFORMATEX.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetMixFormat(nint* format);

    /// <summary>GetDevicePeriod.</summary>
    /// <param name="defaultPeriod">Receives the default period.</param>
    /// <param name="minimumPeriod">Receives the minimum period.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetDevicePeriod(long* defaultPeriod, long* minimumPeriod);

    /// <summary>Start.</summary>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int Start();

    /// <summary>Stop.</summary>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int Stop();

    /// <summary>Reset.</summary>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int Reset();

    /// <summary>SetEventHandle.</summary>
    /// <param name="handle">The event.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int SetEventHandle(nint handle);

    /// <summary>GetService.</summary>
    /// <param name="iid">The service asked for.</param>
    /// <param name="service">Receives it.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetService(Guid* iid, nint* service);
}
