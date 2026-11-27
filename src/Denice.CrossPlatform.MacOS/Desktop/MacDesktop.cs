// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.MacOS.Recording;

namespace Denice.CrossPlatform.MacOS.Desktop;

/// <summary>Coordinates Cocoa, Chromium, native Metal composition, and MP4 lifetime.</summary>
internal sealed class MacDesktop : IPlatformDesktop, IPlatformBrowserFrameSink, IPlatformBrowserAudioSink
{
    private readonly Lock _recordingLock = new();
    private readonly IPlatformBrowser _browser;
    private readonly Browser.BrowserAudioMixer _audio;
    private string? _audioError;
    private MacGpuWindow? _window;
    private Mp4Recorder? _recorder;
    private int _audioChannels;
    private int _audioSampleRate;
    private string? _recordingError;
    private bool _hasBrowserFrame;
    private bool _recordingActive;
    private Exception? _graphicsError;
    private int _frameCount;
    private double _renderMilliseconds;
    private double _readbackMilliseconds;
    private double _encodeMilliseconds;
    private long _captureOrigin;
    private long _lastFrameIndex = -1;
    private long _capturedVideoFrames;
    private long _encodedVideoFrames;

    private MacDesktop(IPlatformBrowser browser, Browser.BrowserAudioMixer audio)
    {
        _browser = browser;
        _audio = audio;
    }

    /// <inheritdoc />
    public string? LastRecordingError => _recordingError;

    /// <inheritdoc />
    public string? LastAudioError => Volatile.Read(ref _audioError);

    /// <inheritdoc />
    public string LastFrameStatistics => string.Create(CultureInfo.InvariantCulture, $"Rendered {_frameCount} frames. Recorder accepted {_capturedVideoFrames} newly captured frames and encoded {_encodedVideoFrames} frames including holds. Mean GPU render: {_renderMilliseconds / Math.Max(1, _frameCount):F2} ms; recording readback: {_readbackMilliseconds / Math.Max(1, _frameCount):F2} ms; encoder submission: {_encodeMilliseconds / Math.Max(1, _frameCount):F2} ms. Browser audio: {(LastAudioError is { } audioError ? "FAILED: " + audioError : "no failures.")}");

    /// <inheritdoc />
    public int Run(Uri initialUrl, string? recordingPath, double? durationSeconds, TimeProvider clock, string browserCachePath, int browserDebugPort)
    {
        ArgumentNullException.ThrowIfNull(initialUrl);
        ArgumentNullException.ThrowIfNull(clock);
        _recordingError = null;
        Volatile.Write(ref _audioError, null);
        nint app = MacApplication.Create();
        nint lifetimePool = MacNative.Call(MacNative.Class("NSAutoreleasePool"), "new");
        nint frame = 0;
        bool browserStarted = false;
        try
        {
            _window = MacGpuWindow.Create();
            if (recordingPath is not null)
            {
                _recorder = Mp4Recorder.Create(recordingPath, _window.RecordingWidth, _window.RecordingHeight, clock);
                _recordingActive = true;
                frame = Marshal.AllocHGlobal(checked(_window.RecordingWidth * _window.RecordingHeight * 4));
            }

            _captureOrigin = _recorder?.OriginTimestamp ?? clock.GetTimestamp();
            RenderFrame(frame, clock);

            // The browser's audio output plays before CEF loads, so Chromium's first stream is pulled from its first sample.
            _audio.Start(clock);
            _browser.Start(initialUrl, MacGpuWindow.Width, MacGpuWindow.Height, this, this, browserCachePath, browserDebugPort);
            browserStarted = true;
            WaitForBrowser(app, frame, clock);
            RunFrames(app, frame, durationSeconds, clock);
            lock (_recordingLock)
            {
                _recordingActive = false;
            }

            if (_recorder is not null && _recorder.FramesWritten == 0)
            {
                throw new IOException("Recording could not start: the encoder accepted no video frames.");
            }

            CloseBrowser(app, clock);
            return 0;
        }
        finally
        {
            FinishRecording();
            if (frame != 0)
            {
                Marshal.FreeHGlobal(frame);
            }

            if (browserStarted || !_browser.IsClosed)
            {
                ShutdownBrowser(app, clock);
            }

            // Only after CEF shutdown: no Chromium audio unit can still call into the mixer.
            _audio.Dispose();
            _window?.Dispose();
            _window = null;
            MacNative.Release(lifetimePool);
        }
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031", Justification = "All managed failures must be captured at this unmanaged Chromium callback and rethrown by the outer application loop.")]
    public void OnAcceleratedFrame(scoped in BrowserGpuBuffer buffer, int format, int width, int height, int visibleX, int visibleY, int visibleWidth, int visibleHeight, bool popup)
    {
        try
        {
            if (buffer.IsNativePixmap || buffer.SharedTexture == 0)
            {
                throw new ArgumentException("The macOS compositor requires a borrowed IOSurface.", nameof(buffer));
            }

            nint surface = buffer.SharedTexture;
            _window?.CopyBrowserSurface(surface, format, width, height, visibleX, visibleY, visibleWidth, visibleHeight, popup);
            if (!popup && surface != 0)
            {
                _hasBrowserFrame = true;
            }
        }
        catch (Exception exception)
        {
            // A native callback cannot propagate a managed exception through Chromium.
            _graphicsError = exception;
        }
    }

    /// <inheritdoc />
    public void OnPopup(bool visible, int x, int y, int width, int height) => _window?.Popup(visible, x, y, width, height);

    /// <inheritdoc />
    public void OnAudioStarted(int channels, int sampleRate)
    {
        lock (_recordingLock)
        {
            _audioChannels = channels;
            _audioSampleRate = sampleRate;
            _recorder?.BeginAudioStream();
        }
    }

    /// <inheritdoc />
    public void OnAudioPacket(nint planarFloatPointers, int frames, long pts)
    {
        lock (_recordingLock)
        {
            if (_recorder is null || _recordingError is not null || !_recordingActive)
            {
                return;
            }

            try
            {
                _recorder.AppendAudio(planarFloatPointers, _audioChannels, frames, _audioSampleRate, pts);
            }
            catch (IOException exception)
            {
                _recordingError = exception.Message;
            }
        }
    }

    /// <inheritdoc />
    public void OnAudioStopped()
    {
        lock (_recordingLock)
        {
            _recorder?.EndAudioStream();
        }
    }

    /// <inheritdoc />
    public void OnAudioError(string message)
    {
        // The first browser audio failure of the run is kept whether or not it recorded; the recording report is unchanged.
        _ = Interlocked.CompareExchange(ref _audioError, message, null);
        lock (_recordingLock)
        {
            if (_recorder is not null)
            {
                _recordingError ??= message;
            }
        }
    }

    /// <summary>Creates the desktop with its browser collaborator.</summary>
    /// <returns>The created object or native operation result.</returns>
    internal static IPlatformDesktop Create()
    {
        var audio = Browser.BrowserAudioMixer.Create();
        return new MacDesktop(CrossPlatform.Browser.CefPlatformBrowser.Create(audio), audio);
    }

    private void ShutdownBrowser(nint app, TimeProvider clock)
    {
        CloseBrowser(app, clock);
        LogAudioDiagnostics("before CefRuntime.Shutdown");
        _browser.Shutdown();
        LogAudioDiagnostics("after CefRuntime.Shutdown");
    }

    [Conditional("DENICE_AUDIO_DIAGNOSTICS")]
    [SuppressMessage("Performance", "CA1822", Justification = "Uses instance data in diagnostic builds (DeniceAudioDiagnostics); empty otherwise.")]
    private void LogAudioDiagnostics(string stage)
    {
#if DENICE_AUDIO_DIAGNOSTICS
        string mixer = string.Create(CultureInfo.InvariantCulture, $"cycles={_audio.Cycles}, last cycle frames={_audio.LastCycleFrames}, registered={_audio.RegisteredStreams}, speech queued/mixed={_audio.SpeechFramesQueued}/{_audio.SpeechFramesMixed}, failure={_audio.Failure ?? "none"}");
        MacNative.LogProof(MacNative.Text($"DENICE audio diagnostics {stage}: {Browser.AudioOutputRedirection.Report}; speech: {Browser.SpeechRedirection.Report}; mixer: {mixer}; events: {Browser.AudioDiagnostics.Events}"));
        if (stage.StartsWith("after", StringComparison.Ordinal))
        {
            foreach (string line in _audio.DescribeCycles(0, long.MaxValue))
            {
                MacNative.LogProof(MacNative.Text(line));
            }
        }
#endif
    }

    private void WaitForBrowser(nint app, nint frame, TimeProvider clock)
    {
        long startup = clock.GetTimestamp();
        while ((!_hasBrowserFrame || !_browser.IsReady) && clock.GetElapsedTime(startup).TotalSeconds < 15 && _window!.IsOpen)
        {
            PumpCocoa(app);
            _browser.Pump();
            ThrowGraphicsError();
            RenderFrame(frame, clock);
            Task.Delay(TimeSpan.FromMilliseconds(1), clock).GetAwaiter().GetResult();
        }

        if (!_hasBrowserFrame || !_browser.IsReady)
        {
            throw new IOException("Chromium did not deliver a loaded application GPU frame within fifteen seconds.");
        }
    }

    private void RunFrames(nint app, nint frame, double? durationSeconds, TimeProvider clock)
    {
        long elapsed = clock.GetTimestamp();
        while (_window!.IsOpen && !_browser.IsClosed && (!durationSeconds.HasValue || clock.GetElapsedTime(elapsed).TotalSeconds < durationSeconds.Value))
        {
            nint pool = MacNative.Call(MacNative.Class("NSAutoreleasePool"), "new");
            try
            {
                PumpCocoa(app);
                _browser.Pump();
                ThrowGraphicsError();
                RenderFrame(frame, clock);
            }
            finally
            {
                MacNative.Release(pool);
            }

            Task.Delay(TimeSpan.FromMilliseconds(1), clock).GetAwaiter().GetResult();
        }
    }

    private void RenderFrame(nint frame, TimeProvider clock)
    {
        double seconds = clock.GetElapsedTime(_captureOrigin).TotalSeconds;
        long frameIndex = (long)(seconds * 60);
        if (frameIndex <= _lastFrameIndex)
        {
            return;
        }

        long renderStart = clock.GetTimestamp();
        _window!.Render(seconds);
        _renderMilliseconds += clock.GetElapsedTime(renderStart).TotalMilliseconds;
        RecordFrame(frame, frameIndex, clock);
        _frameCount++;
        _lastFrameIndex = frameIndex;
    }

    private void RecordFrame(nint frame, long frameIndex, TimeProvider clock)
    {
        lock (_recordingLock)
        {
            if (_recorder is not null && _recordingError is null)
            {
                try
                {
                    long readbackStart = clock.GetTimestamp();
                    _window!.ReadFrame(frame, _window.RecordingWidth * 4);
                    _readbackMilliseconds += clock.GetElapsedTime(readbackStart).TotalMilliseconds;
                    long encodeStart = clock.GetTimestamp();
                    _recorder.AppendVideo(frame, _window.RecordingWidth * 4, frameIndex);
                    _encodeMilliseconds += clock.GetElapsedTime(encodeStart).TotalMilliseconds;
                }
                catch (IOException exception) when (_recorder.FramesWritten > 0)
                {
                    _recordingError = exception.Message;
                }
            }

            if (_recordingError is not null)
            {
                _window!.ReportRecordingError(_recordingError);
            }
        }
    }

    private void CloseBrowser(nint app, TimeProvider clock)
    {
        if (_browser.IsClosed)
        {
            return;
        }

        _browser.RequestClose();
        long closing = clock.GetTimestamp();
        while (!_browser.IsClosed && clock.GetElapsedTime(closing).TotalSeconds < 10)
        {
            PumpCocoa(app);
            _browser.Pump();
            Task.Delay(TimeSpan.FromMilliseconds(1), clock).GetAwaiter().GetResult();
        }

        if (!_browser.IsClosed)
        {
            throw new IOException("Chromium did not close within ten seconds.");
        }
    }

    private void FinishRecording()
    {
        lock (_recordingLock)
        {
            _recordingActive = false;
            try
            {
                _recorder?.Finish();
            }
            catch (IOException exception)
            {
                _recordingError ??= exception.Message;
            }

            _capturedVideoFrames = _recorder?.FramesWritten ?? 0;
            _encodedVideoFrames = _recorder?.EncodedFrames ?? 0;
            _recorder = null;
        }
    }

    private void ThrowGraphicsError()
    {
        if (_graphicsError is not null)
        {
            throw new IOException("Chromium GPU frame import failed.", _graphicsError);
        }
    }

    private void PumpCocoa(nint app)
    {
        while (true)
        {
            nint evt = MacApplication.NextEvent(app);
            if (evt == 0)
            {
                break;
            }

            _window?.HandleEvent(evt, _browser);
            MacNative.Call(app, "sendEvent:", evt);
        }

        MacNative.Call(app, "updateWindows");
    }
}
