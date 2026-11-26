// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Denice.CrossPlatform.Windows.Desktop;

/// <summary>Copies each borrowed CEF NT handle into GPU storage owned by DENICE.</summary>
internal sealed class WindowsSharedFrame : IDisposable
{
    private readonly ID3D11Device1 _device;
    private readonly ID3D11DeviceContext _commands;
    private readonly ID3D11Query _completion;
    private ID3D11Texture2D? _texture;

    private WindowsSharedFrame(ID3D11Device1 device, ID3D11DeviceContext commands, ID3D11Query completion)
    {
        _device = device;
        _commands = commands;
        _completion = completion;
    }

    /// <summary>Gets the most recently copied owned texture.</summary>
    internal ID3D11Texture2D Texture => _texture ?? throw new InvalidOperationException("No browser GPU frame has arrived.");

    /// <inheritdoc />
    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        _completion.Dispose();
        _commands.Dispose();
        _device.Dispose();
    }

    /// <summary>Prevents callback integration until unrecoverable synchronization failure has an approved policy.</summary>
    internal static void EnsureCallbackSafety() => throw new PlatformNotSupportedException("Windows browser composition is gated pending the policy for an unexpected GPU completion failure on a live D3D11 device.");

    /// <summary>Creates an importer on the compositor's D3D11 device.</summary>
    /// <param name="device">The compositor device.</param>
    /// <returns>The importer owning its copy texture and query.</returns>
    internal static WindowsSharedFrame Create(ID3D11Device device)
    {
        var ownedDevice = device.QueryInterface<ID3D11Device1>();
        ID3D11DeviceContext? commands = null;
        ID3D11Query? completion = null;
        try
        {
            commands = device.ImmediateContext.QueryInterface<ID3D11DeviceContext>();
            completion = device.CreateQuery(new QueryDescription(QueryType.Event));
            return new WindowsSharedFrame(ownedDevice, commands, completion);
        }
        catch
        {
            completion?.Dispose();
            commands?.Dispose();
            ownedDevice.Dispose();
            throw;
        }
    }

    /// <summary>Copies a callback-scoped browser texture and waits for the source read to finish.</summary>
    /// <param name="buffer">The borrowed CEF NT shared texture descriptor.</param>
    /// <param name="width">The reported coded width.</param>
    /// <param name="height">The reported coded height.</param>
    internal void Copy(scoped in BrowserGpuBuffer buffer, int width, int height)
    {
        if (buffer.IsNativePixmap || buffer.SharedTexture == 0 || width <= 0 || height <= 0)
        {
            throw new ArgumentException("CEF supplied an invalid shared texture or dimensions.", nameof(buffer));
        }

        // OpenSharedResource1 accepts the NT handle used by current CEF. The opened
        // COM reference is ours; the original HANDLE remains CEF-owned and is not closed.
        using ID3D11Texture2D source = _device.OpenSharedResource1<ID3D11Texture2D>(buffer.SharedTexture);
        Texture2DDescription description = source.Description;
        if (description.Width != (uint)width || description.Height != (uint)height
            || description.ArraySize != 1 || description.MipLevels != 1 || description.SampleDescription.Count != 1
            || description.Format is not (Format.B8G8R8A8_UNorm or Format.R8G8B8A8_UNorm))
        {
            throw new InvalidOperationException("CEF supplied an unsupported D3D11 browser texture layout.");
        }

        EnsureTexture(description);
        _commands.CopyResource(Texture, source);
        _commands.End(_completion);
        _commands.Flush();
        SpinWait spin = default;
        while (true)
        {
            var status = _commands.GetData(_completion, 0, 0, AsyncGetDataFlags.DoNotFlush);
            if (status.Code == 0)
            {
                return;
            }

            var removed = _device.DeviceRemovedReason;
            if (status.Code is unchecked((int)0x887A0005) or unchecked((int)0x887A0007)
                || removed.Code is unchecked((int)0x887A0005) or unchecked((int)0x887A0007)
                    or unchecked((int)0x887A0006) or unchecked((int)0x887A0020) or unchecked((int)0x887A0001))
            {
                throw new IOException("The D3D11 device was removed or reset; the browser GPU frame is invalid.");
            }

            // A live device with an unresolved EVENT still owns a source read. Do not
            // unwind or return its borrowed texture merely because a deadline elapsed.
            // Live-device query failure handling must be settled before CEF wiring.
            spin.SpinOnce();
        }
    }

    private void EnsureTexture(Texture2DDescription source)
    {
        if (_texture is not null)
        {
            Texture2DDescription current = _texture.Description;
            if (current.Width == source.Width && current.Height == source.Height && current.Format == source.Format)
            {
                return;
            }
        }

        source.Usage = ResourceUsage.Default;
        source.BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget;
        source.CPUAccessFlags = CpuAccessFlags.None;
        source.MiscFlags = ResourceOptionFlags.None;
        ID3D11Texture2D replacement = _device.CreateTexture2D(source);
        _texture?.Dispose();
        _texture = replacement;
    }
}
