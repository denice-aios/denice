// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Xilium.CefGlue;

namespace Denice.CrossPlatform.Browser;

/// <summary>Supplies platform startup and borrowed frame metadata to the shared CEF lifecycle.</summary>
internal sealed partial class CefPlatformBrowser
{
    private static partial CefAudioHandler? CreateAudioHandler(State owner, IPlatformBrowserAudioSink sink) => new Audio(owner, sink);

    private static partial void ConfigurePlatform(CefSettings settings)
    {
        MacOS.Desktop.MacApplication.AttachCefProtocol();
        settings.FrameworkDirPath = CefRuntimeLocator.GetFrameworkDirPath();
    }

    private static partial void ConfigureCommandLine(CefCommandLine commandLine) => commandLine.AppendSwitch("use-mock-keychain");

    private static partial void ForwardFrame(IPlatformBrowserFrameSink sink, CefPaintElementType type, CefAcceleratedPaintInfo info)
    {
        var buffer = new BrowserGpuBuffer(info.SharedTexture);
        ForwardBuffer(sink, type, info, in buffer);
    }

    private sealed class Audio(State owner, IPlatformBrowserAudioSink sink) : CefAudioHandler
    {
        protected override bool GetAudioParameters(CefBrowser browser, CefAudioParameters parameters)
        {
            parameters.ChannelLayout = CefChannelLayout.Stereo;
            parameters.SampleRate = 48000;
            return true;
        }

        protected override void OnAudioStreamStarted(CefBrowser browser, in CefAudioParameters parameters, int channels)
        {
            int rate = parameters.SampleRate;
            owner.Invoke(() => sink.OnAudioStarted(channels, rate));
        }

        protected override void OnAudioStreamPacket(CefBrowser browser, nint data, int frames, long pts) => owner.Invoke(() => sink.OnAudioPacket(data, frames, pts));

        protected override void OnAudioStreamStopped(CefBrowser browser) => owner.Invoke(sink.OnAudioStopped);

        protected override void OnAudioStreamError(CefBrowser browser, string message) => owner.Invoke(() => sink.OnAudioError(message));
    }
}
