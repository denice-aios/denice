// Copyright (c) Denice.ai. All rights reserved.

using System.Globalization;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Browser;
using Denice.CrossPlatform.Desktop;
using SkiaSharp;
using Xilium.CefGlue;

namespace Denice.CrossPlatform.Windows.Desktop;

/// <summary>Owns the Win32 window, hardware compositor and shared CEF lifetime.</summary>
internal sealed class WindowsDesktop : IPlatformDesktop, IPlatformBrowserFrameSink, IPlatformBrowserAudioSink, IDisposable
{
    private readonly Lock _graphics = new();
    private readonly IPlatformBrowser _browser;
    private string? _audioError;
    private WindowsWindow? _window;
    private WindowsGpuContext? _context;
    private WindowsSharedFrame? _view;
    private WindowsSharedFrame? _popup;
    private OrbScene? _scene;
    private SKRect _viewSource;
    private SKRect _popupSource;
    private SKRect _popupDestination;
    private bool _hasView;
    private bool _hasPopup;
    private bool _popupVisible;
    private long _frames;
    private bool _cleanupFailed;

    private WindowsDesktop(IPlatformBrowser browser)
    {
        _browser = browser;
    }

    /// <inheritdoc />
    public string? LastRecordingError { get; private set; }

    /// <inheritdoc />
    public string? LastAudioError => Volatile.Read(ref _audioError);

    /// <inheritdoc />
    public string LastFrameStatistics => string.Create(CultureInfo.InvariantCulture, $"Rendered {_frames} hardware GPU frames.");

    /// <inheritdoc />
    public int Run(IApplicationSession application, string? recordingPath, double? durationSeconds, TimeProvider clock, string browserCachePath, int browserDebugPort, TimeSpan browserGpuCopyTimeout)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(clock);
        if (recordingPath is not null)
        {
            throw new PlatformNotSupportedException("Windows MP4 recording is not yet connected to the hardware compositor.");
        }

        WindowsSharedFrame.EnsureCallbackSafety();
        LastRecordingError = null;
        Volatile.Write(ref _audioError, null);
        _frames = 0;
        _hasView = false;
        _hasPopup = false;
        _popupVisible = false;
        if (_context is not null || _window is not null)
        {
            throw new InvalidOperationException("A prior Windows desktop lifetime has not finished shutdown.");
        }

        bool startAttempted = false;
        try
        {
            _window = WindowsWindow.Create(OrbScene.Width, OrbScene.Height);
            _context = WindowsGpuContext.Create();
            _context.AttachWindow(_window.Handle);
            _view = WindowsSharedFrame.Create(_context.Device);
            _popup = WindowsSharedFrame.Create(_context.Device);
            _scene = OrbScene.Create(_context.Skia);
            _window.AttachBrowser(_browser);
            long origin = clock.GetTimestamp();
            Render(_scene, 0);
            startAttempted = true;
            _browser.Start(application.StartupPage, OrbScene.Width, OrbScene.Height, this, this, browserCachePath, browserDebugPort);
            RunFrames(_window, _scene, new ApplicationPageFollower(application, _browser, clock), origin, durationSeconds, clock);
        }
        finally
        {
            // Stop the session before the browser closes, so no retry navigates after close.
            application.RequestStop();
            if (startAttempted)
            {
                CloseBrowser(clock);
                _browser.Shutdown();
            }

            Dispose();
        }

        return _cleanupFailed ? 1 : 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_browser.IsClosed)
        {
            // Native CEF callbacks retain this owner and its graphics until closure.
            return;
        }

        _scene?.Dispose();
        _view?.Dispose();
        _popup?.Dispose();
        _context?.Dispose();
        _window?.Dispose();
        _cleanupFailed |= _context?.CleanupFailed == true || _window?.CleanupFailed == true;
        _scene = null;
        _view = null;
        _popup = null;
        _context = null;
        _window = null;
    }

    /// <inheritdoc />
    public void OnAcceleratedFrame(scoped in BrowserGpuBuffer buffer, int format, int width, int height, int visibleX, int visibleY, int visibleWidth, int visibleHeight, bool popup)
    {
        if ((CefColorType)format is not (CefColorType.Bgra8888 or CefColorType.Rgba8888))
        {
            throw new InvalidOperationException("CEF supplied an unsupported Windows GPU color format.");
        }

        int cropWidth = visibleWidth > 0 ? visibleWidth : width;
        int cropHeight = visibleHeight > 0 ? visibleHeight : height;
        if (visibleX < 0 || visibleY < 0 || cropWidth <= 0 || cropHeight <= 0
            || (long)visibleX + cropWidth > width || (long)visibleY + cropHeight > height)
        {
            throw new InvalidOperationException("CEF supplied a visible rectangle outside its GPU texture.");
        }

        lock (_graphics)
        {
            WindowsSharedFrame target = (popup ? _popup : _view) ?? throw new InvalidOperationException("The desktop GPU importer is unavailable.");
            target.Copy(in buffer, width, height);
            var source = SKRect.Create(visibleX, visibleY, cropWidth, cropHeight);
            if (popup)
            {
                _popupSource = source;
                _hasPopup = true;
            }
            else
            {
                _viewSource = source;
                _hasView = true;
            }
        }
    }

    /// <inheritdoc />
    public void OnPopup(bool visible, int x, int y, int width, int height)
    {
        lock (_graphics)
        {
            _popupVisible = visible;
            _popupDestination = SKRect.Create(x, y, width, height);
        }
    }

    /// <inheritdoc />
    public void OnAudioStarted(int channels, int sampleRate)
    {
        // Browser playback remains CEF-owned until the capture architecture is approved.
    }

    /// <inheritdoc />
    public void OnAudioPacket(nint planarFloatPointers, int frames, long pts)
    {
        // Browser playback remains CEF-owned until the capture architecture is approved.
    }

    /// <inheritdoc />
    public void OnAudioStopped()
    {
        // Browser playback remains CEF-owned until the capture architecture is approved.
    }

    /// <inheritdoc />
    public void OnAudioError(string message)
    {
        // The first browser audio failure of the run is kept whether or not it recorded; the existing report is unchanged.
        _ = Interlocked.CompareExchange(ref _audioError, message, null);
        LastRecordingError = message;
    }

    /// <summary>Creates the platform desktop with the shared browser lifecycle.</summary>
    /// <returns>The native desktop capability.</returns>
    internal static IPlatformDesktop Create() => new WindowsDesktop(CrossPlatform.Browser.CefPlatformBrowser.Create());

    private void RunFrames(WindowsWindow window, OrbScene scene, ApplicationPageFollower follower, long origin, double? durationSeconds, TimeProvider clock)
    {
        long lastFrame = -1;
        while (window.IsOpen && !_browser.IsClosed)
        {
            window.Pump();
            _browser.Pump();
            if (!window.IsOpen)
            {
                break;
            }

            double elapsed = clock.GetElapsedTime(origin).TotalSeconds;
            bool hasView;
            lock (_graphics)
            {
                hasView = _hasView;
            }

            follower.Tick(hasView);
            if (follower.FirstApplicationReady is { } ready && durationSeconds.HasValue && clock.GetElapsedTime(ready).TotalSeconds >= durationSeconds.Value)
            {
                break;
            }

            long index = (long)(elapsed * 60);
            if (index > lastFrame)
            {
                Render(scene, elapsed);
                lastFrame = index;
            }

            Task.Delay(TimeSpan.FromMilliseconds(1), clock).GetAwaiter().GetResult();
        }
    }

    private void Render(OrbScene scene, double seconds)
    {
        lock (_graphics)
        {
            SKSizeI size = _window!.GetClientSize();
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            WindowsGpuContext context = _context!;
            context.MakeCurrent();
            using SKSurface target = context.CreatePresentationTarget(size.Width, size.Height);
            SKCanvas canvas = target.Canvas;
            canvas.Clear(SKColors.Black);
            float scale = Math.Min(size.Width / (float)OrbScene.Width, size.Height / (float)OrbScene.Height);
            canvas.Translate((size.Width - (OrbScene.Width * scale)) / 2, (size.Height - (OrbScene.Height * scale)) / 2);
            canvas.Scale(scale);
            scene.Draw(canvas, seconds);
            if (_hasView)
            {
                context.DrawTexture(_view!.Texture, canvas, _viewSource, SKRect.Create(OrbScene.Width, OrbScene.Height));
            }

            if (_popupVisible && _hasPopup)
            {
                context.DrawTexture(_popup!.Texture, canvas, _popupSource, _popupDestination);
            }

            target.Flush();
            context.Skia.Flush(true, true);
            context.MakeCurrent();
            context.Skia.ResetContext();
            using SKSurface screen = context.CreateWindowTarget(size.Width, size.Height);
            context.DrawTexture(context.Presentation, screen.Canvas, SKRect.Create(size.Width, size.Height), SKRect.Create(size.Width, size.Height));
            screen.Flush();
            context.Present();
            _frames++;
        }
    }

    private void CloseBrowser(TimeProvider clock)
    {
        if (_browser.IsClosed)
        {
            return;
        }

        _browser.RequestClose();
        long started = clock.GetTimestamp();
        while (!_browser.IsClosed && clock.GetElapsedTime(started).TotalSeconds < 10)
        {
            _window!.Pump();
            _browser.Pump();
            Task.Delay(TimeSpan.FromMilliseconds(1), clock).GetAwaiter().GetResult();
        }

        if (!_browser.IsClosed)
        {
            throw new IOException("Chromium did not close within ten seconds.");
        }
    }
}
