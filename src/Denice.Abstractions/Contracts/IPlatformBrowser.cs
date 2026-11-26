// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>Owns the embedded browser on the application main thread.</summary>
public interface IPlatformBrowser
{
    /// <summary>Gets a value indicating whether all browsers have closed.</summary>
    bool IsClosed { get; }

    /// <summary>Gets a value indicating whether the main document completed loading.</summary>
    bool IsReady { get; }

    /// <summary>Starts transparent GPU rendering. The sink must finish reading each borrowed surface before returning.</summary>
    /// <param name="url">The document URL.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="frames">The frame consumer or sample count.</param>
    /// <param name="audio">The audio capture consumer.</param>
    /// <param name="cachePath">The selected browser profile directory.</param>
    /// <param name="debugPort">The optional localhost debugging port, or zero.</param>
    void Start(Uri url, int width, int height, IPlatformBrowserFrameSink frames, IPlatformBrowserAudioSink audio, string cachePath, int debugPort);

    /// <summary>Runs pending browser work on the main thread.</summary>
    void Pump();

    /// <summary>Changes the document without replacing the browser.</summary>
    /// <param name="url">The document URL.</param>
    void Navigate(Uri url);

    /// <summary>Changes the browser viewport.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    void Resize(int width, int height);

    /// <summary>Begins asynchronous browser shutdown.</summary>
    void RequestClose();

    /// <summary>Releases CEF after IsClosed becomes true.</summary>
    void Shutdown();

    /// <summary>Delivers a pointer movement.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <param name="leave">Whether the pointer left the viewport.</param>
    void MouseMove(int x, int y, bool leave);

    /// <summary>Delivers a mouse button. Buttons are left=0, middle=1, right=2.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <param name="button">The mouse button number.</param>
    /// <param name="mouseUp">Whether this releases the button.</param>
    /// <param name="clickCount">The number of clicks.</param>
    void MouseClick(int x, int y, int button, bool mouseUp, int clickCount);

    /// <summary>Delivers wheel deltas.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <param name="deltaX">The horizontal wheel delta.</param>
    /// <param name="deltaY">The vertical wheel delta.</param>
    void MouseWheel(int x, int y, int deltaX, int deltaY);
}
