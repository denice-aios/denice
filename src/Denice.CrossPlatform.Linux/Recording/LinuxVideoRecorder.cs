// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Denice.CrossPlatform.Linux.Desktop;
using Denice.CrossPlatform.Posix;
using Microsoft.Win32.SafeHandles;

namespace Denice.CrossPlatform.Linux.Recording;

/// <summary>Owns the hardware-video path used by native recording validation; product audio integration is separate.</summary>
[SuppressMessage("Design", "CA2216", Justification = "A GCHandle retains the owner until native processing stops and all resources are explicitly released.")]
internal sealed class LinuxVideoRecorder : IDisposable
{
    private const int NullState = 1;
    private const int ReadyState = 2;
    private const int PlayingState = 4;
    private const ulong StateTimeout = 5_000_000_000;
    private const uint EndMessage = 1;
    private const uint ErrorMessage = 2;
    private readonly int _width;
    private readonly int _height;
    private LinuxCudaContext? _cuda;
    private GstHardwareEncoder? _encoder;
    private SafeFileHandle? _file;
    private GCHandle _lifetime;
    private nint _pipeline;
    private nint _bus;
    private nint _source;
    private ulong _lastTimestamp;
    private bool _streamEnded;
    private bool _endReceived;
    private bool _disposed;
    private Exception? _failure;

    private LinuxVideoRecorder(int width, int height)
    {
        _width = width;
        _height = height;
    }

    /// <summary>Gets the actual selected hardware factory.</summary>
    internal string HardwareEncoder { get; private set; } = string.Empty;

    /// <summary>Gets the number of input buffers accepted by appsrc, not a measured distinct-frame rate.</summary>
    internal ulong SubmittedFrames { get; private set; }

    /// <summary>Gets a value indicating whether muxer EOS and native pipeline shutdown completed.</summary>
    internal bool Finished { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopPipeline();
        _cuda?.Dispose();
        _cuda = null;
        _disposed = true;
        if (_lifetime.IsAllocated)
        {
            _lifetime.Free();
        }
    }

    /// <summary>Creates a capability-selected hardware video pipeline and an exclusive MP4 destination.</summary>
    /// <param name="graphics">The compositor owner, which outlives this recorder.</param>
    /// <param name="baseDirectory">The packaged application directory.</param>
    /// <param name="path">The new destination path.</param>
    /// <param name="width">The fixed recording width.</param>
    /// <param name="height">The fixed recording height.</param>
    /// <returns>The native video recorder.</returns>
    internal static LinuxVideoRecorder Create(LinuxGpuContext graphics, string baseDirectory, string path, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        GstRuntime.Initialize(baseDirectory);
        var owner = new LinuxVideoRecorder(width, height);
        owner._lifetime = GCHandle.Alloc(owner);
        try
        {
            owner.Initialize(graphics, path);
            return owner;
        }
        catch (Exception startup)
        {
            try
            {
                owner.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Linux encoder startup and native cleanup failed; resources remain retained.", startup, cleanup);
            }

            throw;
        }
    }

    /// <summary>Submits the original timestamp, returning false before copying when bounded capacity is unavailable.</summary>
    /// <param name="texture">The completed fixed-size compositor image.</param>
    /// <param name="timestamp">Its original presentation time in nanoseconds.</param>
    /// <param name="duration">Its duration in nanoseconds.</param>
    /// <returns>Whether the frame was accepted; the caller retains an unaccepted recording slot.</returns>
    /// <remarks>Call from the creating graphics thread. This method never renumbers timestamps after backpressure.</remarks>
    internal bool TryAppend(LinuxOwnedTexture texture, ulong timestamp, ulong duration)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_streamEnded || Finished)
        {
            throw new InvalidOperationException("The Linux recording stream has ended.");
        }

        if (texture.Width != _width || texture.Height != _height || duration == 0
            || timestamp >= ulong.MaxValue - duration || (SubmittedFrames != 0 && timestamp <= _lastTimestamp))
        {
            throw new ArgumentException("The recording frame must match its dimensions and preserve increasing finite timestamps.", nameof(texture));
        }

        CheckErrors();
        if (GstPipelineNative.QueuedBuffers(_source) >= 3 || _cuda!.OutstandingFrames >= 6)
        {
            return false;
        }

        try
        {
            PushTexture(texture, timestamp, duration);
            _lastTimestamp = timestamp;
            SubmittedFrames++;
            CheckErrors();
            return true;
        }
        catch (Exception error)
        {
            _failure ??= error;
            throw;
        }
    }

    /// <summary>Completes MP4 muxing and stops native processing before reporting success.</summary>
    internal void Finish()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Finished)
        {
            return;
        }

        CheckErrors();
        if (SubmittedFrames == 0)
        {
            throw new InvalidOperationException("Cannot finalize a Linux recording without video frames.");
        }

        if (!_streamEnded)
        {
            RequireFlow(GstPipelineNative.EndStream(_source), "end the input stream");
            _streamEnded = true;
        }

        if (!_endReceived)
        {
            nint message = GstPipelineNative.PopBus(_bus, 30_000_000_000, EndMessage | ErrorMessage);
            if (message == 0)
            {
                throw new TimeoutException("The Linux MP4 muxer did not finish within 30 seconds.");
            }

            ConsumeMessage(message);
        }

        StopPipeline();
        CheckErrors();
        if (_cuda!.OutstandingFrames != 0)
        {
            throw new InvalidOperationException("The Linux encoder has not released all device frames.");
        }

        Finished = true;
    }

    private static void RequireFlow(int flow, string operation)
    {
        if (flow != 0)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"GStreamer could not {operation}; flow status {flow}."));
        }
    }

    private static InvalidOperationException ReadError(nint message)
    {
        GstPipelineNative.ParseError(message, out nint error, out nint debug);
        try
        {
            return GstRuntime.ReadError(error, "The native hardware encoding pipeline failed.");
        }
        finally
        {
            GstNative.Free(debug);
        }
    }

    [SuppressMessage("Reliability", "S3869", Justification = "fdsink accepts an integer descriptor; this owner retains its SafeFileHandle until the native pipeline is NULL and released.")]
    private void Initialize(LinuxGpuContext graphics, string path)
    {
        _cuda = LinuxCudaContext.Create(graphics);
        _encoder = GstHardwareEncoder.Create(_cuda, _width, _height);
        HardwareEncoder = _encoder.FactoryName;
        _pipeline = GstPipelineNative.NewPipeline("denice-video-recording");
        if (_pipeline == 0)
        {
            throw new InvalidOperationException("GStreamer could not create the recording pipeline.");
        }

        _bus = GstPipelineNative.GetBus(_pipeline);
        if (_bus == 0 || GstPipelineNative.AddElement(_pipeline, _encoder.Element) == 0)
        {
            throw new InvalidOperationException("GStreamer could not retain the hardware encoder in its pipeline.");
        }

        _source = Add("appsrc", "libgstapp.so", "compositor-input");
        nint parser = Add("h264parse", "libgstvideoparsersbad.so", "video-parser");
        nint muxer = Add("mp4mux", "libgstisomp4.so", "recording-muxer");
        nint sink = Add("fdsink", "libgstcoreelements.so", "recording-output");
        ConfigureInput();
        GstProperties.SetUInt(_encoder.Element, "bframes", 0);
        GstProperties.SetUInt(_encoder.Element, "rc-lookahead", 0);
        GstProperties.SetBoolean(sink, "sync", false);
        GstProperties.SetBoolean(sink, "async", false);
        _file = PosixFileSystem.CreateRecordingFile(path);
        GstProperties.SetInt(sink, "fd", checked((int)_file.DangerousGetHandle()));
        if (GstPipelineNative.Link(_source, _encoder.Element) == 0
            || GstPipelineNative.Link(_encoder.Element, parser) == 0
            || GstPipelineNative.Link(parser, muxer) == 0
            || GstPipelineNative.Link(muxer, sink) == 0)
        {
            throw new NotSupportedException("The selected hardware elements cannot negotiate the requested recording pipeline.");
        }

        SetStateAndWait(ReadyState);
        _encoder.RequireContext(_cuda);
        if (GstPipelineNative.SetState(_pipeline, PlayingState) == 0)
        {
            CheckErrors();
            throw new InvalidOperationException("The Linux recording pipeline could not start.");
        }
    }

    private nint Add(string factory, string plugin, string name)
    {
        nint element = GstRuntime.CreateElement(factory, plugin, name);
        if (GstPipelineNative.AddElement(_pipeline, element) == 0)
        {
            GstNative.Unref(element);
            throw new InvalidOperationException("GStreamer could not add " + factory + " to the recording pipeline.");
        }

        return element;
    }

    private void ConfigureInput()
    {
        nint caps = GstHardwareEncoder.CreateInputCaps(_width, _height);
        try
        {
            GstPipelineNative.SetSourceCaps(_source, caps);
        }
        finally
        {
            GstNative.UnrefMiniObject(caps);
        }

        GstProperties.SetEnum(_source, "format", 3);
        GstProperties.SetBoolean(_source, "is-live", true);
        GstProperties.SetBoolean(_source, "block", false);
        GstProperties.SetBoolean(_source, "do-timestamp", false);
        GstProperties.SetBoolean(_source, "emit-signals", false);
        GstProperties.SetUInt64(_source, "max-buffers", 3);
        GstProperties.SetUInt64(_source, "max-bytes", 0);
        GstProperties.SetUInt64(_source, "max-time", 0);
    }

    private void PushTexture(LinuxOwnedTexture texture, ulong timestamp, ulong duration)
    {
        nint buffer = GstPipelineNative.NewBuffer();
        if (buffer == 0)
        {
            throw new InvalidOperationException("GStreamer could not allocate a recording buffer.");
        }

        nint memory = 0;
        LinuxCudaContext.DeviceFrame? frame = null;
        bool transferred = false;
        try
        {
            frame = _cuda!.CopyTexture(texture);
            memory = GstCudaMemory.Create(frame);
            transferred = true;
            GstPipelineNative.AppendMemory(buffer, memory);
            memory = 0;
            GstPipelineNative.SetTiming(buffer, timestamp, duration);
            nint submitted = buffer;
            buffer = 0;
            RequireFlow(GstPipelineNative.PushBuffer(_source, submitted), "queue a CUDA frame");
        }
        finally
        {
            if (buffer != 0)
            {
                GstNative.UnrefMiniObject(buffer);
            }

            if (memory != 0)
            {
                GstNative.UnrefMiniObject(memory);
            }

            if (!transferred)
            {
                frame?.Dispose();
            }
        }
    }

    private void CheckErrors()
    {
        _failure ??= _cuda?.ReleaseFailure;
        if (_failure is not null)
        {
            throw new InvalidOperationException("Linux recording failed.", _failure);
        }

        if (_bus == 0)
        {
            return;
        }

        nint message;
        while ((message = GstPipelineNative.PopBus(_bus, 0, EndMessage | ErrorMessage)) != 0)
        {
            ConsumeMessage(message);
        }
    }

    private void ConsumeMessage(nint message)
    {
        try
        {
            if (GstPipelineNative.MessageType(message) == EndMessage && _streamEnded)
            {
                _endReceived = true;
                return;
            }

            _failure = GstPipelineNative.MessageType(message) == ErrorMessage
                ? ReadError(message) : new InvalidOperationException("The native recording pipeline ended before the requested finalization.");
            throw _failure;
        }
        finally
        {
            GstNative.UnrefMiniObject(message);
        }
    }

    private void SetStateAndWait(int state)
    {
        if (GstPipelineNative.SetState(_pipeline, state) == 0)
        {
            throw new InvalidOperationException("The Linux media pipeline rejected its requested state.");
        }

        // SetState itself can block synchronously; this timeout only bounds the subsequent query.
        int result = GstPipelineNative.GetState(_pipeline, out int current, out _, StateTimeout);
        if (result is 0 or 2 || current != state)
        {
            throw new InvalidOperationException("The Linux media pipeline did not complete its requested state transition.");
        }
    }

    private void StopPipeline()
    {
        if (_pipeline != 0)
        {
            // Failure leaves the pipeline, file handle and self-root retained.
            // A native worker must never write through a recycled descriptor.
            SetStateAndWait(NullState);
            if (_bus != 0)
            {
                GstNative.Unref(_bus);
                _bus = 0;
            }

            GstNative.Unref(_pipeline);
            _pipeline = 0;
            _source = 0;
        }

        _encoder?.Dispose();
        _encoder = null;
        _file?.Dispose();
        _file = null;
    }
}
