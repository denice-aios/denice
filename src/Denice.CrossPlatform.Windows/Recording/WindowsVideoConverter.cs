// Copyright (c) Denice.ai. All rights reserved.

using Vortice;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Denice.CrossPlatform.Windows.Recording;

/// <summary>Converts owned composition textures to encoder-owned NV12 storage on the same hardware device.</summary>
internal sealed class WindowsVideoConverter : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext _videoContext;
    private readonly TimeProvider _clock;
    private readonly uint _width;
    private readonly uint _height;
    private ID3D11VideoProcessorEnumerator? _enumerator;
    private ID3D11VideoProcessor? _processor;
    private uint _inputWidth;
    private uint _inputHeight;

    private WindowsVideoConverter(ID3D11Device device, ID3D11VideoDevice videoDevice, ID3D11VideoContext videoContext, uint width, uint height, TimeProvider clock)
    {
        _clock = clock;
        _device = device;
        _videoDevice = videoDevice;
        _videoContext = videoContext;
        _width = width;
        _height = height;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _processor?.Dispose();
        _enumerator?.Dispose();
        _videoContext.Dispose();
        _videoDevice.Dispose();
        _device.Dispose();
    }

    /// <summary>Creates conversion resources with independent references to the shared D3D device.</summary>
    /// <param name="device">The hardware compositor device.</param>
    /// <param name="width">The fixed even encoder width.</param>
    /// <param name="height">The fixed even encoder height.</param>
    /// <param name="clock">The injected scheduling clock.</param>
    /// <returns>The conversion owner.</returns>
    internal static WindowsVideoConverter Create(ID3D11Device device, uint width, uint height, TimeProvider clock)
    {
        if (width == 0 || height == 0 || (width & 1) != 0 || (height & 1) != 0)
        {
            throw new ArgumentException("NV12 encoding requires positive even dimensions.", nameof(width));
        }

        ID3D11Device ownedDevice = device.QueryInterface<ID3D11Device>();
        ID3D11VideoDevice? videoDevice = null;
        ID3D11VideoContext? videoContext = null;
        try
        {
            videoDevice = device.QueryInterface<ID3D11VideoDevice>();
            videoContext = device.ImmediateContext.QueryInterface<ID3D11VideoContext>();
            return new WindowsVideoConverter(ownedDevice, videoDevice, videoContext, width, height, clock);
        }
        catch
        {
            videoContext?.Dispose();
            videoDevice?.Dispose();
            ownedDevice.Dispose();
            throw;
        }
    }

    /// <summary>Fits the complete source into a new NV12 texture that is never overwritten after submission.</summary>
    /// <param name="source">The completed composition texture.</param>
    /// <returns>The caller-owned GPU conversion result.</returns>
    internal ID3D11Texture2D Convert(ID3D11Texture2D source)
    {
        Texture2DDescription input = source.Description;
        EnsureProcessor(input);
        var output = new Texture2DDescription
        {
            Width = _width,
            Height = _height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.NV12,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget,
        };
        ID3D11Texture2D texture = _device.CreateTexture2D(output);
        try
        {
            using ID3D11VideoProcessorInputView inputView = _videoDevice.CreateVideoProcessorInputView(source, _enumerator!, new VideoProcessorInputViewDescription { ViewDimension = VideoProcessorInputViewDimension.Texture2D });
            using ID3D11VideoProcessorOutputView outputView = _videoDevice.CreateVideoProcessorOutputView(texture, _enumerator!, new VideoProcessorOutputViewDescription { ViewDimension = VideoProcessorOutputViewDimension.Texture2D });
            _videoContext.VideoProcessorBlt(_processor!, outputView, 0, [new VideoProcessorStream { Enable = true, InputSurface = inputView }]).CheckError();
            CompleteConversion();
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private void CompleteConversion()
    {
        ID3D11DeviceContext commands = _device.ImmediateContext;
        using ID3D11Query completion = _device.CreateQuery(new QueryDescription(QueryType.Event));
        commands.End(completion);
        commands.Flush();
        long started = _clock.GetTimestamp();
        while (true)
        {
            var result = commands.GetData(completion, 0, 0, AsyncGetDataFlags.DoNotFlush);
            result.CheckError();
            if (result.Code == 0)
            {
                return;
            }

            if (_clock.GetElapsedTime(started).TotalSeconds >= 10)
            {
                throw new IOException("GPU video conversion did not complete within ten seconds.");
            }

            Thread.Yield();
        }
    }

    private void EnsureProcessor(Texture2DDescription input)
    {
        if (_processor is not null && _inputWidth == input.Width && _inputHeight == input.Height)
        {
            return;
        }

        _processor?.Dispose();
        _processor = null;
        _enumerator?.Dispose();
        _enumerator = _videoDevice.CreateVideoProcessorEnumerator(new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputFrameRate = new Rational(60, 1),
            OutputFrameRate = new Rational(60, 1),
            InputWidth = input.Width,
            InputHeight = input.Height,
            OutputWidth = _width,
            OutputHeight = _height,
            Usage = VideoUsage.OptimalQuality,
        });
        if (((uint)_enumerator.CheckVideoProcessorFormat(input.Format) & (uint)VideoProcessorFormatSupport.Input) == 0
            || ((uint)_enumerator.CheckVideoProcessorFormat(Format.NV12) & (uint)VideoProcessorFormatSupport.Output) == 0)
        {
            throw new PlatformNotSupportedException("The hardware video processor cannot convert the composition format to NV12.");
        }

        _processor = _videoDevice.CreateVideoProcessor(_enumerator, 0);
        ConfigureProcessor(input);
        _inputWidth = input.Width;
        _inputHeight = input.Height;
    }

    private void ConfigureProcessor(Texture2DDescription input)
    {
        double scale = Math.Min(_width / (double)input.Width, _height / (double)input.Height);
        int width = (int)(input.Width * scale);
        int height = (int)(input.Height * scale);
        int left = ((int)_width - width) / 2;
        int top = ((int)_height - height) / 2;
        ID3D11VideoProcessor processor = _processor!;
        _videoContext.VideoProcessorSetStreamFrameFormat(processor, 0, VideoFrameFormat.Progressive);
        _videoContext.VideoProcessorSetStreamAutoProcessingMode(processor, 0, false);
        _videoContext.VideoProcessorSetStreamSourceRect(processor, 0, true, new RawRect(0, 0, (int)input.Width, (int)input.Height));
        _videoContext.VideoProcessorSetStreamDestRect(processor, 0, true, new RawRect(left, top, left + width, top + height));
        _videoContext.VideoProcessorSetOutputTargetRect(processor, true, new RawRect(0, 0, (int)_width, (int)_height));
        _videoContext.VideoProcessorSetOutputBackgroundColor(processor, false, new VideoColor { Rgba = new VideoColorRgba { A = 1 } });
        _videoContext.VideoProcessorSetStreamColorSpace(processor, 0, new VideoProcessorColorSpace { RGB_Range = 0, YCbCr_Matrix = 1, Nominal_Range = 2 });
        _videoContext.VideoProcessorSetOutputColorSpace(processor, new VideoProcessorColorSpace { YCbCr_Matrix = 1, Nominal_Range = 1 });
    }
}
