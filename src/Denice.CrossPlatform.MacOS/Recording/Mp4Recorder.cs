// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.MacOS.Recording;

/// <summary>Records composed BGRA frames with macOS's H.264 MP4 encoder.</summary>
internal sealed class Mp4Recorder : IDisposable
{
    private const int MaximumHeldFramesPerSubmission = 8;

    private static readonly nint Framework = NativeLibrary.Load(RecordingNative.AvFoundation);
    private readonly Lock _gate = new();
    private readonly string _destination;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly TimeProvider _clock;
    private MediaHandle? _writerHandle;
    private MediaHandle? _videoHandle;
    private MediaHandle? _adaptorHandle;
    private RecordedAudio? _audio;
    private long? _audioOrigin;
    private long _audioStartSample;
    private long _audioNextSample;
    private long _audioSamples;
    private long _audioGapSamples;
    private long _audioPackets;
    private long _audioLastPts;
    private bool _finished;
    private bool _audioStreamActive;
    private long _lastFrame = -1;
    private long _lastRequestedFrame = -1;
    private nint _previousPixelBuffer;

    private Mp4Recorder(string destination, int width, int height, TimeProvider clock, int fps)
    {
        ArgumentException.ThrowIfNullOrEmpty(destination);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fps);
        _destination = destination;
        _width = width;
        _height = height;
        _fps = fps;
        _clock = clock;

        try
        {
            Initialize();
            OriginTimestamp = clock.GetTimestamp();
        }
        catch
        {
            Release();
            throw;
        }
    }

    /// <summary>Gets the monotonic origin shared by video, startup capture, and audio.</summary>
    internal long OriginTimestamp { get; }

    /// <summary>Gets the number of video frames accepted by the encoder.</summary>
    internal long FramesWritten { get; private set; }

    /// <summary>Gets encoded samples including held copies of the last captured image.</summary>
    internal long EncodedFrames { get; private set; }

    private MediaHandle Writer => _writerHandle!;

    private MediaHandle VideoInput => _videoHandle!;

    private MediaHandle Adaptor => _adaptorHandle!;

    /// <inheritdoc />
    public void Dispose()
    {
        Finish();
        GC.SuppressFinalize(this);
    }

    /// <summary>Creates an H.264/AAC MP4 writer; an existing output causes an error.</summary>
    /// <param name="path">The destination file.</param>
    /// <param name="width">The fixed recording width.</param>
    /// <param name="height">The fixed recording height.</param>
    /// <param name="clock">The shared recording clock.</param>
    /// <param name="fps">The recording timestamp rate.</param>
    /// <returns>The active recorder.</returns>
    internal static Mp4Recorder Create(string path, int width, int height, TimeProvider clock, int fps = 60) => new(path, width, height, clock, fps);

    /// <summary>Copies one completed BGRA scene into the encoder.</summary>
    /// <param name="bgra">The complete composed frame, valid throughout the call.</param>
    /// <param name="stride">Bytes between source rows.</param>
    /// <param name="frameIndex">The monotonic zero-based frame timestamp.</param>
    internal void AppendVideo(nint bgra, int stride, long frameIndex)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_finished, this);
            if (frameIndex <= _lastRequestedFrame || stride < checked(_width * 4) || bgra == 0)
            {
                throw new ArgumentException("Recording frames must have increasing timestamps and complete BGRA rows.", nameof(frameIndex));
            }

            _lastRequestedFrame = frameIndex;
            EnsureWriting();
            if (!_audioStreamActive)
            {
                _audio?.AdvanceSilenceTo(frameIndex * 48000 / _fps);
            }

            _audio?.Drain();
            if (!FillHeldFrames(frameIndex))
            {
                return;
            }

            if (RecordingNative.BoolMessage(VideoInput, RecordingNative.Selector("isReadyForMoreMediaData")) == 0)
            {
                // Real-time encoder backpressure drops this frame while preserving its time gap.
                return;
            }

            int result = RecordingNative.CVPixelBufferCreate(0, (nuint)_width, (nuint)_height, 0x42475241, 0, out nint pixelBuffer);
            if (result != 0)
            {
                throw new IOException($"Cannot allocate recording pixel buffer: {result}.");
            }

            try
            {
                CopyPixels(pixelBuffer, bgra, stride);
                SubmitPixelBuffer(pixelBuffer, frameIndex);
                if (_previousPixelBuffer != 0)
                {
                    RecordingNative.CFRelease(_previousPixelBuffer);
                }

                _previousPixelBuffer = pixelBuffer;
                pixelBuffer = 0;
                FramesWritten++;
            }
            finally
            {
                if (pixelBuffer != 0)
                {
                    RecordingNative.CFRelease(pixelBuffer);
                }
            }
        }
    }

    /// <summary>Starts a new browser audio epoch on the current recording timeline.</summary>
    internal void BeginAudioStream()
    {
        lock (_gate)
        {
            _audioOrigin = null;
            _audioStreamActive = true;
        }
    }

    /// <summary>Marks the output inactive so subsequent video time contains silence.</summary>
    internal void EndAudioStream()
    {
        lock (_gate)
        {
            _audioStreamActive = false;
        }
    }

    /// <summary>Copies the captured browser output into the AAC track.</summary>
    /// <param name="planarFloatPointers">The array of channel pointers valid during this call.</param>
    /// <param name="channels">The configured channel count.</param>
    /// <param name="frames">The samples per channel.</param>
    /// <param name="sampleRate">The configured sample rate.</param>
    /// <param name="ptsMilliseconds">The browser audio timestamp in milliseconds.</param>
    internal void AppendAudio(nint planarFloatPointers, int channels, int frames, int sampleRate, long ptsMilliseconds)
    {
        lock (_gate)
        {
            if (_finished || _audio is null)
            {
                return;
            }

            EnsureWriting();
            _audioStreamActive = true;
            if (_audioOrigin is null)
            {
                _audioOrigin = ptsMilliseconds;

                // CEF audio_capturer.cc supplies TimeTicks milliseconds, despite the public
                // header describing Unix time. Anchor its epoch to this shared capture clock.
                _audioStartSample = Math.Max(_audioNextSample, _clock.GetElapsedTime(OriginTimestamp).Ticks * sampleRate / TimeSpan.TicksPerSecond);
                _audioNextSample = _audioStartSample;
            }

            // Packet samples define the audio clock; millisecond source timestamps cannot describe
            // individual samples. Preserve gaps larger than one packet instead of adding rounding gaps.
            long sourcePosition = _audioStartSample + ((ptsMilliseconds - _audioOrigin.Value) * sampleRate / 1000);
            if (_audioNextSample - sourcePosition > frames)
            {
                // A backwards source-clock jump starts a new epoch without rewinding encoded samples.
                _audioOrigin = ptsMilliseconds;
                _audioStartSample = Math.Max(_audioNextSample, _clock.GetElapsedTime(OriginTimestamp).Ticks * sampleRate / TimeSpan.TicksPerSecond);
                sourcePosition = _audioStartSample;
            }

            long position = sourcePosition - _audioNextSample > frames ? sourcePosition : _audioNextSample;
            if (_audioPackets > 0)
            {
                _audioGapSamples += position - _audioNextSample;
            }

            _audioSamples += frames;
            _audioPackets++;
            _audioLastPts = ptsMilliseconds;
            try
            {
                _audio.Append(planarFloatPointers, channels, frames, sampleRate, position);
                _audioNextSample = position + frames;
            }
            catch (IOException exception)
            {
                throw WriterError($"{exception.Message} Audio packet: channels={channels}, frames={frames}, rate={sampleRate}, pts={ptsMilliseconds}, position={position}");
            }
        }
    }

    /// <summary>Finishes the tracks and flushes the MP4 before releasing native resources.</summary>
    internal void Finish()
    {
        lock (_gate)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            Task<byte>? finalization = null;
            try
            {
                if (_lastFrame < 0)
                {
                    _ = RecordingNative.Send(Writer, "cancelWriting");
                    throw new IOException("Recording contains no composed video frames.");
                }

                DrainHeldFrames();
                _ = RecordingNative.Send(VideoInput, "markAsFinished");
                _audio?.AdvanceSilenceTo((_lastRequestedFrame + 1) * 48000 / _fps);
                _audio?.Finish();

                // The synchronous native API must run off the application main thread.
                finalization = Task.Run(FinishNative);
                byte success = finalization.WaitAsync(TimeSpan.FromSeconds(15), _clock, CancellationToken.None).GetAwaiter().GetResult();
                if (success == 0)
                {
                    throw WriterError("Cannot finalize recording; recoverable fragments remain at " + _destination);
                }
            }
            catch (TimeoutException exception)
            {
                throw new IOException("MP4 finalization exceeded fifteen seconds; recoverable fragments remain at " + _destination, exception);
            }
            finally
            {
                if (finalization is { IsCompleted: false })
                {
                    // The worker still owns the writer and its inputs. Dispose only after its native call returns.
                    _ = finalization.ContinueWith(ReleaseAfterFinalization, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                else
                {
                    Release();
                }
            }
        }
    }

    private static nint Constant(string symbol) => RecordingNative.Constant(Framework, symbol);

    private static void Put(nint dictionary, nint key, nint value) =>
        _ = RecordingNative.MessageTwo(dictionary, RecordingNative.Selector("setObject:forKey:"), value, key);

    private static IOException NativeError(string message, nint error)
    {
        nint description = RecordingNative.Send(error, "localizedDescription");
        string detail = Marshal.PtrToStringUTF8(RecordingNative.Send(description, "UTF8String")) ?? "Unknown native encoder error";
        nint domain = RecordingNative.Send(error, "domain");
        string? domainText = Marshal.PtrToStringUTF8(RecordingNative.Send(domain, "UTF8String"));
        nint metadata = RecordingNative.Send(RecordingNative.Send(error, "userInfo"), "description");
        string? metadataText = Marshal.PtrToStringUTF8(RecordingNative.Send(metadata, "UTF8String"));
        return new IOException($"{message}: {detail} ({domainText} {RecordingNative.Send(error, "code")}). {metadataText}");
    }

    private void CopyPixels(nint pixelBuffer, nint bgra, int stride)
    {
        int result = RecordingNative.CVPixelBufferLockBaseAddress(pixelBuffer, 0);
        if (result != 0)
        {
            throw new IOException($"Cannot lock recording pixel buffer: {result}.");
        }

        try
        {
            nint target = RecordingNative.CVPixelBufferGetBaseAddress(pixelBuffer);
            int targetStride = checked((int)RecordingNative.CVPixelBufferGetBytesPerRow(pixelBuffer));
            for (int row = 0; row < _height; row++)
            {
                _ = RecordingNative.CopyMemory(target + (row * targetStride), bgra + (row * stride), (nuint)(_width * 4));
            }
        }
        finally
        {
            _ = RecordingNative.CVPixelBufferUnlockBaseAddress(pixelBuffer, 0);
        }
    }

    private void DrainHeldFrames()
    {
        long started = _clock.GetTimestamp();
        while (!FillHeldFrames(_lastRequestedFrame + 1))
        {
            EnsureWriting();
            if (_clock.GetElapsedTime(started) > TimeSpan.FromSeconds(5))
            {
                throw new IOException("The video encoder did not drain held frames within five seconds; recoverable fragments remain at " + _destination);
            }

            Task.Delay(TimeSpan.FromMilliseconds(1), _clock).GetAwaiter().GetResult();
        }
    }

    private bool FillHeldFrames(long nextFrame)
    {
        int submitted = 0;
        while (_previousPixelBuffer != 0 && _lastFrame + 1 < nextFrame)
        {
            if (submitted == MaximumHeldFramesPerSubmission || RecordingNative.BoolMessage(VideoInput, RecordingNative.Selector("isReadyForMoreMediaData")) == 0)
            {
                return false;
            }

            SubmitPixelBuffer(_previousPixelBuffer, _lastFrame + 1);
            submitted++;
        }

        return true;
    }

    private void SubmitPixelBuffer(nint pixelBuffer, long frameIndex)
    {
        if (RecordingNative.AppendPixelBuffer(Adaptor, RecordingNative.Selector("appendPixelBuffer:withPresentationTime:"), pixelBuffer, new MediaTime(frameIndex, _fps)) == 0)
        {
            throw WriterError("Cannot append recording frame");
        }

        _lastFrame = frameIndex;
        EncodedFrames++;
    }

    private byte FinishNative()
    {
        nint pool = RecordingNative.Send(RecordingNative.Send(RecordingNative.GetClass("NSAutoreleasePool"), "alloc"), "init");
        try
        {
            return RecordingNative.BoolMessage(Writer, RecordingNative.Selector("finishWriting"));
        }
        finally
        {
            _ = RecordingNative.Send(pool, "drain");
        }
    }

    private void Initialize()
    {
        nint fileType = Constant("AVFileTypeMPEG4");
        nint pool = RecordingNative.Send(RecordingNative.Send(RecordingNative.GetClass("NSAutoreleasePool"), "alloc"), "init");
        try
        {
            nint url = RecordingNative.Send(RecordingNative.GetClass("NSURL"), "fileURLWithPath:", RecordingNative.String(_destination));
            nint writer = RecordingNative.InitializeWriter(RecordingNative.Send(RecordingNative.GetClass("AVAssetWriter"), "alloc"), RecordingNative.Selector("initWithURL:fileType:error:"), url, fileType, out nint error);
            _writerHandle = MediaHandle.Own(writer);
            if (Writer.IsInvalid)
            {
                throw NativeError("Cannot create MP4 recording", error);
            }

            nint settings = RecordingNative.Send(RecordingNative.GetClass("NSMutableDictionary"), "dictionary");
            Put(settings, Constant("AVVideoCodecKey"), Constant("AVVideoCodecTypeH264"));
            Put(settings, Constant("AVVideoWidthKey"), RecordingNative.Number(_width));
            Put(settings, Constant("AVVideoHeightKey"), RecordingNative.Number(_height));
            nint video = RecordingNative.MessageTwo(RecordingNative.GetClass("AVAssetWriterInput"), RecordingNative.Selector("assetWriterInputWithMediaType:outputSettings:"), Constant("AVMediaTypeVideo"), settings);
            _videoHandle = MediaHandle.Own(RecordingNative.Send(video, "retain"));
            _ = RecordingNative.Send(VideoInput, "setExpectsMediaDataInRealTime:", 1);
            if (RecordingNative.BoolArgument(Writer, RecordingNative.Selector("canAddInput:"), VideoInput) == 0)
            {
                throw new IOException("The MP4 writer cannot accept H.264 video.");
            }

            _ = RecordingNative.Send(Writer, "addInput:", VideoInput);
            nint adaptor = RecordingNative.MessageTwo(RecordingNative.GetClass("AVAssetWriterInputPixelBufferAdaptor"), RecordingNative.Selector("assetWriterInputPixelBufferAdaptorWithAssetWriterInput:sourcePixelBufferAttributes:"), VideoInput, 0);
            _adaptorHandle = MediaHandle.Own(RecordingNative.Send(adaptor, "retain"));
            _audio = RecordedAudio.Create(Writer, Framework, _clock);
            RecordingNative.TimeMessage(Writer, RecordingNative.Selector("setMovieFragmentInterval:"), new MediaTime(1, 1));
            if (RecordingNative.BoolMessage(Writer, RecordingNative.Selector("startWriting")) == 0)
            {
                throw WriterError("Cannot start MP4 recording");
            }

            RecordingNative.TimeMessage(Writer, RecordingNative.Selector("startSessionAtSourceTime:"), new MediaTime(0, _fps));
        }
        finally
        {
            _ = RecordingNative.Send(pool, "drain");
        }
    }

    private void EnsureWriting()
    {
        if (RecordingNative.Send(Writer, "status") != 1)
        {
            throw WriterError("Recording encoder stopped");
        }
    }

    private IOException WriterError(string message) =>
        NativeError($"{message}. Audio samples={_audioSamples}, gaps={_audioGapSamples}, packets={_audioPackets}, source duration={_audioLastPts - _audioOrigin}ms", RecordingNative.Send(Writer, "error"));

    private void ReleaseAfterFinalization(Task<byte> completed)
    {
        _ = completed.Exception;
        Release();
    }

    private void Release()
    {
        if (_previousPixelBuffer != 0)
        {
            RecordingNative.CFRelease(_previousPixelBuffer);
            _previousPixelBuffer = 0;
        }

        _audio?.Dispose();
        _audio = null;
        _adaptorHandle?.Dispose();
        _videoHandle?.Dispose();
        _writerHandle?.Dispose();
        _adaptorHandle = null;
        _videoHandle = null;
        _writerHandle = null;
    }
}
