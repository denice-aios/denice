// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace Denice.CrossPlatform.Windows.Recording;

/// <summary>Encodes GPU video through a hardware H.264 MFT. Product audio integration remains separate.</summary>
internal sealed class WindowsVideoRecorder : IDisposable
{
    private const int Capacity = 8;
    [SuppressMessage("Usage", "CA2213", Justification = "SampleRelease disposes the semaphore after native callback ownership ends.")]
    private readonly SemaphoreSlim _slots = new(Capacity, Capacity);
    private readonly Thread _thread = Thread.CurrentThread;
    private readonly TimeProvider _clock;
    private readonly uint _width;
    private readonly uint _height;
    private readonly SampleRelease _release;
    private WindowsVideoConverter? _converter;
    private IMFDXGIDeviceManager? _manager;
    private IMFByteStream? _stream;
    private IMFSinkWriter? _writer;
    private ID3D11Texture2D? _previous;
    private int _videoStream;
    private MfNative.Session? _session;
    private bool _disposed;
    private bool _finished;
    private long _nextFrame;

    private WindowsVideoRecorder(uint width, uint height, TimeProvider clock)
    {
        _clock = clock;
        _width = width;
        _height = height;
        _release = new SampleRelease(_slots, clock);
    }

    /// <summary>Gets the number of encoded samples, including repeated frames.</summary>
    internal long FramesWritten => _nextFrame;

    /// <summary>Gets the hardware encoder's device link from its MFT attributes.</summary>
    internal string HardwareEncoder { get; private set; } = string.Empty;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        RequireThread();
        _disposed = true;
        _writer?.Dispose();
        _stream?.Dispose();
        _manager?.Dispose();
        _previous?.Dispose();
        _converter?.Dispose();

        // SharpGen's native wrapper holds only a weak managed reference. The task
        // roots the callback until native COM ownership has actually ended.
        _ = _release.DisposeWhenReleasedAsync();
        _session?.Dispose();
    }

    /// <summary>Creates a video encoder for an unused output path without a software fallback.</summary>
    /// <param name="path">The new MP4 destination.</param>
    /// <param name="device">The compositor's multithread-protected hardware device.</param>
    /// <param name="width">The even encoded width.</param>
    /// <param name="height">The even encoded height.</param>
    /// <param name="clock">The injected scheduling clock.</param>
    /// <returns>The encoder lifetime owner.</returns>
    internal static WindowsVideoRecorder Create(string path, ID3D11Device device, uint width, uint height, TimeProvider clock)
    {
        var recorder = new WindowsVideoRecorder(width, height, clock);
        try
        {
            recorder.Initialize(path, device);
            return recorder;
        }
        catch
        {
            recorder.Dispose();
            throw;
        }
    }

    /// <summary>Converts the latest composition and fills earlier missed slots with the previous GPU image.</summary>
    /// <param name="presentation">The completed composition texture.</param>
    /// <param name="frameIndex">The elapsed 60 Hz recording slot.</param>
    internal void AppendVideo(ID3D11Texture2D presentation, long frameIndex)
    {
        RequireThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished || frameIndex < _nextFrame)
        {
            throw new InvalidOperationException("Video recording received a non-monotonic frame or was already finalized.");
        }

        if (_previous is not null)
        {
            while (_nextFrame < frameIndex)
            {
                WriteSample(_previous);
            }
        }

        // Reserve capacity before allocating a GPU texture, so delayed native
        // consumers cannot create an unbounded collection of conversion surfaces.
        AcquireSlot();
        ID3D11Texture2D? converted = null;
        try
        {
            converted = _converter!.Convert(presentation);
        }
        catch
        {
            _slots.Release();
            throw;
        }

        _previous?.Dispose();
        _previous = converted;
        WriteSample(converted, slotReserved: true);
    }

    /// <summary>Drains the hardware encoder and finalizes the MP4 container.</summary>
    internal void Finish()
    {
        RequireThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished)
        {
            return;
        }

        if (_nextFrame == 0)
        {
            throw new IOException("The hardware encoder accepted no video frames.");
        }

        _writer!.NotifyEndOfSegment(_videoStream);
        _writer.Finalize();
        _finished = true;
    }

    private static IMFMediaType CreateVideoType(Guid subtype, uint width, uint height)
    {
        IMFMediaType type = MediaFactory.MFCreateMediaType();
        try
        {
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            type.Set(MediaTypeAttributeKeys.Subtype, subtype).CheckError();
            type.Set(MediaTypeAttributeKeys.FrameSize, MediaFactory.PackSize(width, height)).CheckError();
            type.Set(MediaTypeAttributeKeys.FrameRate, MediaFactory.PackRatio(60, 1)).CheckError();
            type.Set(MediaTypeAttributeKeys.PixelAspectRatio, MediaFactory.PackRatio(1, 1)).CheckError();
            type.Set(MediaTypeAttributeKeys.InterlaceMode, 2u).CheckError();
            type.Set(MediaTypeAttributeKeys.VideoPrimaries, 2u).CheckError();
            type.Set(MediaTypeAttributeKeys.TransferFunction, 5u).CheckError();
            type.Set(MediaTypeAttributeKeys.YuvMatrix, 1u).CheckError();
            type.Set(MediaTypeAttributeKeys.VideoNominalRange, 1u).CheckError();
            return type;
        }
        catch
        {
            type.Dispose();
            throw;
        }
    }

    private void Initialize(string path, ID3D11Device device)
    {
        _session = MfNative.OpenSession();
        using var protection = device.ImmediateContext.QueryInterface<ID3D11Multithread>();
        if (!protection.GetMultithreadProtected())
        {
            throw new InvalidOperationException("Media Foundation requires the shared D3D11 device to be multithread protected.");
        }

        _converter = WindowsVideoConverter.Create(device, _width, _height, _clock);
        _manager = MediaFactory.MFCreateDXGIDeviceManager();
        _manager.ResetDevice(device).CheckError();
        _stream = MediaFactory.MFCreateFile(FileAccessMode.MfAccessModeWrite, FileOpenMode.MfOpenModeFailIfExist, FileFlags.FlagsNone, path);
        using IMFAttributes attributes = MediaFactory.MFCreateAttributes(4);
        attributes.Set(SinkWriterAttributeKeys.D3DManager, _manager).CheckError();
        attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, true).CheckError();
        attributes.Set(SinkWriterAttributeKeys.ReadwriteDisableConverters, true).CheckError();
        attributes.Set(TranscodeAttributeKeys.TranscodeContainertype, TranscodeContainerTypeGuids.Mpeg4).CheckError();
        _writer = MediaFactory.MFCreateSinkWriterFromURL(null!, _stream, attributes);
        using IMFMediaType output = CreateVideoType(VideoFormatGuids.H264, _width, _height);
        output.Set(MediaTypeAttributeKeys.AvgBitrate, 12_000_000u).CheckError();
        _videoStream = _writer.AddStream(output);
        using IMFMediaType input = CreateVideoType(VideoFormatGuids.NV12, _width, _height);
        _writer.SetInputMediaType(_videoStream, input, null!);
        _writer.BeginWriting();
        RequireHardwareEncoder();
    }

    private void RequireHardwareEncoder()
    {
        using var writer = _writer!.QueryInterface<IMFSinkWriterEx>();
        writer.GetTransformForStream(_videoStream, 0, out Guid category, out IMFTransform transform);
        using (transform)
        using (IMFAttributes attributes = transform.Attributes)
        {
            if (category != TransformCategoryGuids.VideoEncoder)
            {
                throw new PlatformNotSupportedException("Media Foundation did not select the requested video encoder.");
            }

            HardwareEncoder = attributes.GetString(TransformAttributeKeys.MftEnumHardwareUrlAttribute);
            if (string.IsNullOrEmpty(HardwareEncoder))
            {
                throw new PlatformNotSupportedException("Media Foundation selected a software H.264 encoder.");
            }
        }
    }

    private void WriteSample(ID3D11Texture2D texture, bool slotReserved = false)
    {
        if (!slotReserved)
        {
            AcquireSlot();
        }

        bool tracked = false;
        try
        {
            MediaFactory.MFCreateTrackedSample(out IMFTrackedSample allocation).CheckError();
            using (allocation)
            using (IMFSample sample = allocation.QueryInterface<IMFSample>())
            using (IMFMediaBuffer buffer = MediaFactory.MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, texture, 0, false))
            using (var layout = buffer.QueryInterface<IMF2DBuffer>())
            {
                buffer.CurrentLength = layout.ContiguousLength;
                sample.AddBuffer(buffer);
                sample.SampleTime = checked(_nextFrame * 10_000_000 / 60);
                sample.SampleDuration = checked(((_nextFrame + 1) * 10_000_000 / 60) - sample.SampleTime);
                allocation.SetAllocator(_release, null!);
                tracked = true;
                _writer!.WriteSample(_videoStream, sample);
                _nextFrame++;
            }
        }
        finally
        {
            if (!tracked)
            {
                _slots.Release();
            }
        }
    }

    private void AcquireSlot()
    {
        if (!_slots.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new IOException("The hardware encoder retained all eight GPU video samples for ten seconds.");
        }
    }

    private void RequireThread()
    {
        if (_thread != Thread.CurrentThread)
        {
            throw new InvalidOperationException("Media Foundation recording must remain on its owning thread.");
        }
    }

    /// <summary>Owns the allocator callback and releases capacity after each native sample lifetime.</summary>
    /// <param name="slots">The capacity semaphore transferred to this callback for disposal.</param>
    /// <param name="clock">The injected cleanup scheduling clock.</param>
    internal sealed class SampleRelease(SemaphoreSlim slots, TimeProvider clock) : CallbackBase, IMFAsyncCallback
    {
        /// <inheritdoc />
        public Result GetParameters(out AsyncCallbackFlags pdwFlags, out int pdwQueue)
        {
            pdwFlags = AsyncCallbackFlags.None;
            pdwQueue = WorkQueueId.Standard.Id;
            return Result.Ok;
        }

        /// <inheritdoc />
        [SuppressMessage("Design", "CA1031", Justification = "An allocator callback must return an HRESULT rather than unwind across the native ABI.")]
        public Result Invoke(IMFAsyncResult asyncResult)
        {
            try
            {
                slots.Release();
                return Result.Ok;
            }
            catch (Exception)
            {
                return Result.Fail;
            }
        }

        /// <summary>Retains the callback until only its initial managed-owner COM reference remains.</summary>
        /// <returns>The asynchronous completion of callback and semaphore disposal.</returns>
        internal async Task DisposeWhenReleasedAsync()
        {
            // AddRef/Release uses SharpGen's interlocked native reference count.
            // The owner's initial reference prevents a zero-count race while probing.
            // An outstanding Invoke still has the native caller's reference even
            // after it has returned the sample's semaphore slot.
            while (true)
            {
                _ = AddRef();
                if (Release() == 1)
                {
                    Dispose();
                    slots.Dispose();
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), clock).ConfigureAwait(false);
            }
        }
    }
}
