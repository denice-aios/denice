// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Browser;
using Denice.CrossPlatform.Desktop;
using Denice.CrossPlatform.Linux.Browser;
using SkiaSharp;

namespace Denice.CrossPlatform.Linux.Desktop;

/// <summary>Coordinates X11, EGL composition and the stock CEF browser lifetime.</summary>
internal sealed class LinuxDesktop : IPlatformDesktop, IPlatformBrowserFrameSink, IPlatformBrowserAudioSink, IDisposable
{
    private readonly IPlatformBrowser _browser;
    private string? _audioError;
    private LinuxGpuContext? _graphics;
    private LinuxSharedFrame? _main;
    private LinuxSharedFrame? _popup;
    private OrbScene? _scene;
    private SKRect _mainVisible;
    private SKRect _popupVisible;
    private SKRect _popupDestination;
    private bool _popupShown;
    private bool _hasMain;
    private bool _hasPopup;
    private Exception? _callbackError;
    private long _frames;
    private long _imports;
    private long _lastFrame = -1;
    private string _renderer = string.Empty;

    private LinuxDesktop(IPlatformBrowser browser)
    {
        _browser = browser;
    }

    /// <inheritdoc />
    public string? LastRecordingError { get; private set; }

    /// <inheritdoc />
    public string? LastAudioError => Volatile.Read(ref _audioError);

    /// <inheritdoc />
    public string LastFrameStatistics => string.Create(CultureInfo.InvariantCulture, $"Renderer: {_renderer}. Presented {_frames} frames; copied {_imports} CEF DMA-BUF frames. Recording is unavailable.");

    /// <inheritdoc />
    public int Run(Uri initialUrl, string? recordingPath, double? durationSeconds, TimeProvider clock, string browserCachePath, int browserDebugPort)
    {
        ArgumentNullException.ThrowIfNull(initialUrl);
        ArgumentNullException.ThrowIfNull(clock);
        Volatile.Write(ref _audioError, null);
        if (recordingPath is not null)
        {
            LastRecordingError = "Linux recording requires the pending GPU encoder and approved audio capture integration.";
            return 1;
        }

        if (_graphics is not null)
        {
            throw new InvalidOperationException("A prior Linux desktop lifetime has not finished shutdown.");
        }

        ResetRun();
        bool startAttempted = false;
        try
        {
            // X11 startup trapping is finished and restored before CefInitialize.
            _graphics = LinuxGpuContext.Create(OrbScene.Width, OrbScene.Height);
            _renderer = _graphics.Renderer;
            _main = new LinuxSharedFrame(_graphics);
            _popup = new LinuxSharedFrame(_graphics);
            _scene = OrbScene.Create(_graphics.Skia);
            _graphics.Window.AttachBrowser(_browser);
            startAttempted = true;
            _browser.Start(initialUrl, OrbScene.Width, OrbScene.Height, this, this, browserCachePath, browserDebugPort);
            RunFrames(durationSeconds, clock);
        }
        finally
        {
            if (startAttempted)
            {
                CloseBrowser(clock);
                _browser.Shutdown();
            }

            Dispose();
        }

        return 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_browser.IsClosed)
        {
            // CloseBrowser reports the shutdown failure. Retain resources while
            // native callbacks can still reference them rather than freeing early.
            return;
        }

        _scene?.Dispose();
        _main?.Dispose();
        _popup?.Dispose();
        _graphics?.Dispose();
        _scene = null;
        _main = null;
        _popup = null;
        _graphics = null;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031", Justification = "The managed application loop rethrows errors captured at the unmanaged browser callback boundary.")]
    public void OnAcceleratedFrame(scoped in BrowserGpuBuffer buffer, int format, int width, int height, int visibleX, int visibleY, int visibleWidth, int visibleHeight, bool popup)
    {
        try
        {
            if (visibleX < 0 || visibleY < 0 || visibleWidth <= 0 || visibleHeight <= 0
                || (long)visibleX + visibleWidth > width || (long)visibleY + visibleHeight > height)
            {
                throw new ArgumentException("CEF supplied a visible rectangle outside its coded GPU frame.", nameof(visibleWidth));
            }

            LinuxSharedFrame importer = (popup ? _popup : _main) ?? throw new InvalidOperationException("The Linux GPU importer is unavailable.");
            importer.Copy(in buffer, format, width, height);
            var visible = new SKRect(visibleX, visibleY, visibleX + visibleWidth, visibleY + visibleHeight);
            if (popup)
            {
                _popupVisible = visible;
                _hasPopup = true;
            }
            else
            {
                _mainVisible = visible;
                _hasMain = true;
            }

            _imports++;
        }
        catch (Exception error)
        {
            _callbackError = error;
        }
    }

    /// <inheritdoc />
    public void OnPopup(bool visible, int x, int y, int width, int height)
    {
        _popupShown = visible;
        _popupDestination = new SKRect(x, y, x + width, y + height);
    }

    /// <inheritdoc />
    public void OnAudioStarted(int channels, int sampleRate) => UnexpectedAudioCapture();

    /// <inheritdoc />
    public void OnAudioPacket(nint planarFloatPointers, int frames, long pts) => UnexpectedAudioCapture();

    /// <inheritdoc />
    public void OnAudioStopped() => UnexpectedAudioCapture();

    /// <inheritdoc />
    public void OnAudioError(string message)
    {
        // The first browser audio failure of the run is kept whether or not it recorded; the existing report is unchanged.
        _ = Interlocked.CompareExchange(ref _audioError, message, null);
        _callbackError = new IOException(message);
    }

    /// <summary>Creates the Linux desktop service.</summary>
    /// <returns>The desktop with its browser collaborator.</returns>
    internal static IPlatformDesktop Create() => new LinuxDesktop(CefPlatformBrowser.Create());

    private void ResetRun()
    {
        LastRecordingError = null;
        _callbackError = null;
        _frames = 0;
        _imports = 0;
        _lastFrame = -1;
        _hasMain = false;
        _hasPopup = false;
        _popupShown = false;
    }

    private void RunFrames(double? durationSeconds, TimeProvider clock)
    {
        LinuxGpuContext graphics = _graphics!;
        long origin = clock.GetTimestamp();
        long? ready = null;
        while (!graphics.Window.CloseRequested && !_browser.IsClosed)
        {
            graphics.Window.Pump();
            _browser.Pump();
            ThrowCallbackError();
            if (_hasMain && _browser.IsReady)
            {
                ready ??= clock.GetTimestamp();
            }
            else if (clock.GetElapsedTime(origin).TotalSeconds >= 15)
            {
                throw new IOException("CEF did not deliver a loaded Linux GPU frame within fifteen seconds.");
            }

            if (ready.HasValue && durationSeconds.HasValue && clock.GetElapsedTime(ready.Value).TotalSeconds >= durationSeconds.Value)
            {
                break;
            }

            Render(clock.GetElapsedTime(origin).TotalSeconds);
            Task.Delay(TimeSpan.FromMilliseconds(1), clock).GetAwaiter().GetResult();
        }
    }

    private void Render(double seconds)
    {
        long index = (long)(seconds * 60);
        LinuxGpuContext graphics = _graphics!;
        SKSizeI size = graphics.Window.Size;
        if (index <= _lastFrame || size.Width <= 0 || size.Height <= 0 || graphics.Window.CloseRequested)
        {
            return;
        }

        using SKSurface target = graphics.CreateWindowTarget();
        SKCanvas canvas = target.Canvas;
        canvas.Clear(SKColors.Black);
        float scale = Math.Min(size.Width / (float)OrbScene.Width, size.Height / (float)OrbScene.Height);
        canvas.Translate((size.Width - (OrbScene.Width * scale)) / 2, (size.Height - (OrbScene.Height * scale)) / 2);
        canvas.Scale(scale);
        _scene!.Draw(canvas, seconds);
        if (_hasMain)
        {
            using SKImage image = _main!.Surface.Snapshot();
            canvas.DrawImage(image, _mainVisible, new SKRect(0, 0, OrbScene.Width, OrbScene.Height), new SKSamplingOptions(SKFilterMode.Linear));
        }

        if (_popupShown && _hasPopup)
        {
            using SKImage image = _popup!.Surface.Snapshot();
            canvas.DrawImage(image, _popupVisible, _popupDestination, new SKSamplingOptions(SKFilterMode.Linear));
        }

        target.Flush();
        graphics.Present();
        _lastFrame = index;
        _frames++;
    }

    private void CloseBrowser(TimeProvider clock)
    {
        _browser.RequestClose();
        long origin = clock.GetTimestamp();
        while (!_browser.IsClosed && clock.GetElapsedTime(origin).TotalSeconds < 10)
        {
            _browser.Pump();
            Task.Delay(TimeSpan.FromMilliseconds(1), clock).GetAwaiter().GetResult();
        }

        if (!_browser.IsClosed)
        {
            throw new IOException("CEF did not complete Linux browser shutdown within ten seconds; graphics resources remain borrowed.");
        }
    }

    private void ThrowCallbackError()
    {
        if (_callbackError is { } error)
        {
            _callbackError = null;
            throw new InvalidOperationException("Linux browser GPU or audio processing failed.", error);
        }
    }

    private void UnexpectedAudioCapture() => _callbackError = new NotSupportedException("Linux CEF must retain normal playback until native audio capture is integrated.");
}
