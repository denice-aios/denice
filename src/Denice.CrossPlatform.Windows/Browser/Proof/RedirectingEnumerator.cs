// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>
/// The DENICE IMMDeviceEnumerator Chromium receives from libcef.dll's redirected CoCreateInstance. Enumeration and
/// notification registration go to the real enumerator; render devices from GetDefaultAudioEndpoint and GetDevice are
/// wrapped so their IAudioClient is DENICE's. Capture devices stay real. With no render endpoint (hosted runners), the
/// enumerator supplies one virtual default endpoint (proof build only).
/// </summary>
[GeneratedComClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: COM out pointers are unmanaged.")]
internal sealed unsafe partial class RedirectingEnumerator : IMMDeviceEnumerator
{
    /// <summary>The virtual endpoint's id.</summary>
    internal const string VirtualId = "{0.0.0.00000000}.{denice-virtual-render-endpoint}";

    private const int Render = 0;
    private const int All = 2;
    private readonly IMMDeviceEnumerator _real;
    private readonly TimeProvider _clock;

    /// <summary>Initializes a new instance of the <see cref="RedirectingEnumerator"/> class.</summary>
    /// <param name="real">The real enumerator.</param>
    /// <param name="clock">The injected clock.</param>
    internal RedirectingEnumerator(IMMDeviceEnumerator real, TimeProvider clock)
    {
        _real = real;
        _clock = clock;
    }

    /// <inheritdoc />
    public int EnumAudioEndpoints(int flow, uint stateMask, nint* devices)
    {
        int status = _real.EnumAudioEndpoints(flow, stateMask, devices);
        uint* counted = stackalloc uint[1];
        if (status == ComNative.Ok && *devices != 0)
        {
            _ = ComNative.Wrap<IMMDeviceCollection>(*devices).GetCount(counted);
        }

        uint count = counted[0];

        AudioProofLog.Note($"IMMDeviceEnumerator.EnumAudioEndpoints flow {flow} mask 0x{stateMask:x} -> 0x{status:x8}, {count} real endpoints");
        if (flow is Render or All && count == 0 && (stateMask & 1) != 0)
        {
            _ = ComNative.Release(*devices);
            *devices = ComNative.Expose(new VirtualCollection(this), typeof(IMMDeviceCollection).GUID);
            AudioProofLog.Count("virtual endpoint supplied in EnumAudioEndpoints");
            return ComNative.Ok;
        }

        return status;
    }

    /// <inheritdoc />
    public int GetDefaultAudioEndpoint(int flow, int role, nint* endpoint)
    {
        int status = _real.GetDefaultAudioEndpoint(flow, role, endpoint);
        AudioProofLog.Note($"IMMDeviceEnumerator.GetDefaultAudioEndpoint flow {flow} role {role} -> 0x{status:x8}");
        if (flow != Render)
        {
            return status;
        }

        if (status == ComNative.NotFound)
        {
            AudioProofLog.Count("virtual endpoint supplied as default");
            *endpoint = Virtual();
            return ComNative.Ok;
        }

        return status == ComNative.Ok ? WrapInPlace(endpoint) : status;
    }

    /// <inheritdoc />
    public int GetDevice(nint id, nint* device)
    {
        string name = id == 0 ? string.Empty : new string((char*)id);
        AudioProofLog.Note("IMMDeviceEnumerator.GetDevice " + name);
        if (string.Equals(name, VirtualId, StringComparison.Ordinal))
        {
            *device = Virtual();
            return ComNative.Ok;
        }

        int status = _real.GetDevice(id, device);
        if (status != ComNative.Ok)
        {
            return status;
        }

        int* flow = stackalloc int[1];
        flow[0] = -1;
        nint endpoint = ComNative.QueryInterface(*device, typeof(IMMEndpoint).GUID);
        if (endpoint != 0)
        {
            _ = ComNative.Wrap<IMMEndpoint>(endpoint).GetDataFlow(flow);
            _ = ComNative.Release(endpoint);
        }

        return flow[0] == Render ? WrapInPlace(device) : status;
    }

    /// <inheritdoc />
    public int RegisterEndpointNotificationCallback(nint client) => _real.RegisterEndpointNotificationCallback(client);

    /// <inheritdoc />
    public int UnregisterEndpointNotificationCallback(nint client) => _real.UnregisterEndpointNotificationCallback(client);

    /// <summary>Creates the virtual endpoint's COM pointer.</summary>
    /// <returns>The IMMDevice.</returns>
    internal nint Virtual() => ComNative.Expose(new RedirectingDevice(null, VirtualId, _clock), typeof(IMMDevice).GUID);

    private int WrapInPlace(nint* device)
    {
        nint real = *device;
        nint* id = stackalloc nint[1];
        IMMDevice wrapped = ComNative.Wrap<IMMDevice>(real);
        _ = wrapped.GetId(id);
        string name = id[0] == 0 ? string.Empty : new string((char*)id[0]);
        *device = ComNative.Expose(new RedirectingDevice(wrapped, name, _clock), typeof(IMMDevice).GUID);
        _ = ComNative.Release(real);
        AudioProofLog.Count("render endpoints wrapped");
        return ComNative.Ok;
    }

    /// <summary>The one-device collection returned when the system has no render endpoint.</summary>
    [GeneratedComClass]
    private sealed partial class VirtualCollection(RedirectingEnumerator owner) : IMMDeviceCollection
    {
        public int GetCount(uint* count)
        {
            *count = 1;
            return ComNative.Ok;
        }

        public int Item(uint index, nint* device)
        {
            *device = index == 0 ? owner.Virtual() : 0;
            return index == 0 ? ComNative.Ok : unchecked((int)0x80070057);
        }
    }
}
