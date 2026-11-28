// Copyright (c) Denice.ai. All rights reserved.

using System.Globalization;
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
    private readonly TimeSpan _copyTimeout;
    private readonly BrowserGpuCopyTermination _termination;
    private readonly TimeProvider _clock;
    private ID3D11Texture2D? _texture;

    /// <summary>Initializes a new instance of the <see cref="WindowsSharedFrame"/> class on the compositor's D3D11 device.</summary>
    /// <param name="device">The compositor device.</param>
    /// <param name="copyTimeout">How long one copy may stay pending (browser.gpuCopyTimeoutSeconds).</param>
    /// <param name="termination">Ends the process when a copy cannot be completed safely.</param>
    /// <param name="clock">The clock the deadline is measured on.</param>
    internal WindowsSharedFrame(ID3D11Device device, TimeSpan copyTimeout, BrowserGpuCopyTermination termination, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(copyTimeout, TimeSpan.Zero);
        _termination = termination ?? throw new ArgumentNullException(nameof(termination));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _copyTimeout = copyTimeout;
        _device = device.QueryInterface<ID3D11Device1>();
        try
        {
            _commands = device.ImmediateContext.QueryInterface<ID3D11DeviceContext>();
            try
            {
                _completion = device.CreateQuery(new QueryDescription(QueryType.Event));
            }
            catch
            {
                _commands.Dispose();
                throw;
            }
        }
        catch
        {
            _device.Dispose();
            throw;
        }
    }

    /// <summary>What one completion query result means for the borrowed source texture (approved GPU completion policy).</summary>
    internal enum CompletionStep
    {
        /// <summary>The copy finished; the borrowed texture can be returned to CEF.</summary>
        Done,

        /// <summary>The copy is still pending on a live device within the deadline.</summary>
        Wait,

        /// <summary>The device was removed or reset; the frame is invalid and the error is ordinary.</summary>
        DeviceLost,

        /// <summary>Still pending past the deadline on a live device: the process must end at once.</summary>
        DeadlinePassed,

        /// <summary>The query failed on a live device for no explained reason: the process must end at once.</summary>
        QueryFailed,
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

    /// <summary>Classifies one completion query result (approved GPU completion policy).</summary>
    /// <param name="queryStatus">The HRESULT from ID3D11DeviceContext::GetData (S_OK done, S_FALSE pending).</param>
    /// <param name="removedReason">The device's GetDeviceRemovedReason HRESULT (S_OK while the device is live).</param>
    /// <param name="elapsed">Time since the copy was submitted.</param>
    /// <param name="deadline">The browser.gpuCopyTimeoutSeconds deadline.</param>
    /// <returns>What to do next.</returns>
    internal static CompletionStep Classify(int queryStatus, int removedReason, TimeSpan elapsed, TimeSpan deadline)
    {
        if (queryStatus == 0)
        {
            return CompletionStep.Done;
        }

        // DXGI_ERROR_DEVICE_REMOVED and _RESET from the query, or a removed-device reason: REMOVED, HUNG, RESET,
        // DRIVER_INTERNAL_ERROR or INVALID_CALL.
        if (queryStatus is unchecked((int)0x887A0005) or unchecked((int)0x887A0007)
            || removedReason is unchecked((int)0x887A0005) or unchecked((int)0x887A0007)
                or unchecked((int)0x887A0006) or unchecked((int)0x887A0020) or unchecked((int)0x887A0001))
        {
            return CompletionStep.DeviceLost;
        }

        if (queryStatus == 1)
        {
            return elapsed > deadline ? CompletionStep.DeadlinePassed : CompletionStep.Wait;
        }

        return CompletionStep.QueryFailed;
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
        long submitted = _clock.GetTimestamp();
        SpinWait spin = default;
        while (true)
        {
            int status = _commands.GetData(_completion, 0, 0, AsyncGetDataFlags.DoNotFlush).Code;
            int removed = status == 0 ? 0 : _device.DeviceRemovedReason.Code;
            switch (Classify(status, removed, _clock.GetElapsedTime(submitted), _copyTimeout))
            {
                case CompletionStep.Done:
                    return;
                case CompletionStep.DeviceLost:
                    throw new IOException("The D3D11 device was removed or reset; the browser GPU frame is invalid.");
                case CompletionStep.DeadlinePassed:
                    // A live device still owns a read of CEF's texture: returning it, or unwinding, could let CEF reuse
                    // memory the GPU is reading. The process ends instead.
                    _termination.Terminate(string.Create(CultureInfo.InvariantCulture, $"The browser GPU frame copy was still pending after {_copyTimeout.TotalSeconds:0.###} s (browser.gpuCopyTimeoutSeconds) on a live D3D11 device."));
                    break;
                case CompletionStep.QueryFailed:
                    _termination.Terminate(string.Create(CultureInfo.InvariantCulture, $"The browser GPU frame copy's completion query failed with 0x{status:X8} on a live D3D11 device."));
                    break;
                default:
                    spin.SpinOnce();
                    break;
            }
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
