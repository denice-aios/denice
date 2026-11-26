// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>Receives borrowed GPU frames. All native reads must finish during the call.</summary>
public interface IPlatformBrowserFrameSink
{
    /// <summary>Consumes a frame with CEF color format and its visible rectangle.</summary>
    /// <param name="buffer">The borrowed native GPU surface.</param>
    /// <param name="format">The CEF color format value.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="visibleX">The visible rectangle horizontal origin.</param>
    /// <param name="visibleY">The visible rectangle vertical origin.</param>
    /// <param name="visibleWidth">The visible rectangle width.</param>
    /// <param name="visibleHeight">The visible rectangle height.</param>
    /// <param name="popup">Whether this belongs to the popup layer.</param>
    void OnAcceleratedFrame(scoped in BrowserGpuBuffer buffer, int format, int width, int height, int visibleX, int visibleY, int visibleWidth, int visibleHeight, bool popup);

    /// <summary>Shows or hides the browser popup layer.</summary>
    /// <param name="visible">Whether the popup is visible.</param>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    void OnPopup(bool visible, int x, int y, int width, int height);
}
