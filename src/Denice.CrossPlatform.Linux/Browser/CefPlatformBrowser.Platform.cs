// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Xilium.CefGlue;

namespace Denice.CrossPlatform.Browser;

/// <summary>Supplies platform startup and borrowed frame metadata to the shared CEF lifecycle.</summary>
/// <summary>Configures the stock Linux CEF runtime and forwards borrowed DMA-BUF planes.</summary>
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
#if DENICE_AUDIO_REDIRECTION_PROOF
        // Proof build only: Chromium's Linux page speech runs only with this switch, and the fixture's explicit-device
        // case needs media permission granted without a prompt.
        commandLine.AppendSwitch("enable-speech-dispatcher");
        commandLine.AppendSwitch("use-fake-ui-for-media-stream");
#endif
    }

    private static partial void ForwardFrame(IPlatformBrowserFrameSink sink, CefPaintElementType type, CefAcceleratedPaintInfo info)
    {
        if (info.PlaneCount is < 1 or > 4 || info.Planes.Length < info.PlaneCount)
        {
            throw new InvalidOperationException("CEF supplied an invalid DMA-BUF plane count.");
        }

        Span<BrowserGpuPlane> planes = stackalloc BrowserGpuPlane[info.PlaneCount];
        for (int index = 0; index < planes.Length; index++)
        {
            CefAcceleratedPaintNativePixmapPlane plane = info.Planes[index];
            planes[index] = new BrowserGpuPlane(plane.FileDescriptor, plane.Stride, plane.Offset, plane.Size);
        }

        var buffer = new BrowserGpuBuffer(planes, info.Modifier);
        ForwardBuffer(sink, type, info, in buffer);
    }
}
