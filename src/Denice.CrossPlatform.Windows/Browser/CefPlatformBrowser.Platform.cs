// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Xilium.CefGlue;

namespace Denice.CrossPlatform.Browser;

/// <summary>Supplies platform startup and borrowed frame metadata to the shared CEF lifecycle.</summary>
internal sealed partial class CefPlatformBrowser
{
    private static partial CefAudioHandler? CreateAudioHandler(State owner, IPlatformBrowserAudioSink sink) => null;

    private static partial void ConfigurePlatform(CefSettings settings)
    {
        // The shared CEF settings and switches apply unchanged on this platform.
    }

    private static partial void ConfigureCommandLine(CefCommandLine commandLine)
    {
        // The shared CEF settings and switches apply unchanged on this platform.
    }

    private static partial void ForwardFrame(IPlatformBrowserFrameSink sink, CefPaintElementType type, CefAcceleratedPaintInfo info)
    {
        var buffer = new BrowserGpuBuffer(info.SharedTexture);
        ForwardBuffer(sink, type, info, in buffer);
    }
}
