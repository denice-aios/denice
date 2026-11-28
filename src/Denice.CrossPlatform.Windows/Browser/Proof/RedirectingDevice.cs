// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>
/// A wrapped render endpoint: Activate(IAudioClient) returns a DENICE <see cref="WasapiRedirectedClient"/>; everything
/// else goes to the real device. The virtual endpoint has no real device and an empty property store.
/// </summary>
[GeneratedComClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: COM out pointers are unmanaged.")]
internal sealed unsafe partial class RedirectingDevice : IMMDevice, IMMEndpoint, IPropertyStore
{
    private readonly IMMDevice? _real;
    private readonly string _id;
    private readonly TimeProvider _clock;

    /// <summary>Initializes a new instance of the <see cref="RedirectingDevice"/> class.</summary>
    /// <param name="real">The real device, or null for the virtual endpoint.</param>
    /// <param name="id">The endpoint id.</param>
    /// <param name="clock">The injected clock.</param>
    internal RedirectingDevice(IMMDevice? real, string id, TimeProvider clock)
    {
        _real = real;
        _id = id;
        _clock = clock;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The client's lifetime belongs to Chromium's COM reference; it stays registered (silent once stopped) until the process exits.")]
    public int Activate(Guid* iid, uint context, nint parameters, nint* instance)
    {
        Guid asked = *iid;
        AudioProofLog.Count("IMMDevice.Activate " + asked);
        if (asked == typeof(IAudioClient).GUID)
        {
            AudioProofLog.Count("DENICE IAudioClient activations");
            AudioProofLog.Note("IMMDevice.Activate IAudioClient on " + _id + " -> DENICE client");
            *instance = ComNative.Expose(new WasapiRedirectedClient(_clock, _id), asked);
            return ComNative.Ok;
        }

        AudioProofLog.Note($"IMMDevice.Activate {asked} on {_id} -> {(_real is null ? "E_NOINTERFACE (virtual)" : "real device")}");
        if (_real is null)
        {
            *instance = 0;
            return ComNative.NoInterface;
        }

        return _real.Activate(iid, context, parameters, instance);
    }

    /// <inheritdoc />
    public int OpenPropertyStore(uint access, nint* store)
    {
        if (_real is not null)
        {
            return _real.OpenPropertyStore(access, store);
        }

        *store = ComNative.Expose(this, typeof(IPropertyStore).GUID);
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int GetId(nint* id)
    {
        *id = ComNative.TaskString(_id);
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int GetState(uint* state)
    {
        if (_real is not null)
        {
            return _real.GetState(state);
        }

        *state = 1;
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int GetDataFlow(int* flow)
    {
        *flow = 0;
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int GetCount(uint* count)
    {
        *count = 0;
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int GetAt(uint index, void* key) => unchecked((int)0x80070057);

    /// <inheritdoc />
    public int GetValue(void* key, void* value)
    {
        // An empty PROPVARIANT (VT_EMPTY): the virtual endpoint has no friendly name or form factor.
        new Span<byte>(value, 24).Clear();
        return ComNative.Ok;
    }

    /// <inheritdoc />
    public int SetValue(void* key, void* value) => ComNative.NotImplemented;

    /// <inheritdoc />
    public int Commit() => ComNative.Ok;
}
