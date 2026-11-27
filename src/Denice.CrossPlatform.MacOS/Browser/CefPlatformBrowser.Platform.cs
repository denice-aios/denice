// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.MacOS.Browser;
using Xilium.CefGlue;

namespace Denice.CrossPlatform.Browser;

/// <summary>
/// macOS: every Chromium output stream and page speech is redirected into the browser audio mixer, whose mixed output is
/// the browser's sole audio channel, played by the mixer and handed to the audio sink given to Start on the main thread.
/// The desktop owns the mixer's lifetime: it starts it before Start and disposes it after Shutdown. CEF's own audio capture
/// is not used.
/// </summary>
internal sealed partial class CefPlatformBrowser
{
    private BrowserAudioMixer? mixer;
    private IPlatformBrowserAudioSink? audioSink;

    /// <summary>Creates the platform browser service whose audio goes through the given mixer.</summary>
    /// <param name="mixer">The browser audio mixer; the desktop starts it before Start and disposes it after Shutdown.</param>
    /// <returns>The browser capability.</returns>
    internal static IPlatformBrowser Create(BrowserAudioMixer mixer)
    {
        ArgumentNullException.ThrowIfNull(mixer);
        return new CefPlatformBrowser { mixer = mixer };
    }

    private static partial CefAudioHandler? CreateAudioHandler(State owner, IPlatformBrowserAudioSink sink) => null;

    private static partial void ConfigurePlatform(CefSettings settings)
    {
        MacOS.Desktop.MacApplication.AttachCefProtocol();
        settings.FrameworkDirPath = CefRuntimeLocator.GetFrameworkDirPath();
    }

    private static partial void ConfigureCommandLine(CefCommandLine commandLine)
    {
        commandLine.AppendSwitch("use-mock-keychain");

        // Single-process runs Chromium's audio service in process, but MediaDevicesManager binds the service's DeviceNotifier
        // whenever this feature is enabled; that notifier's destructor runs after the browser SystemMonitor is gone and
        // crashes CEF shutdown once a page has selected an output device. Disabling the feature keeps device monitoring
        // on the browser SystemMonitor (approved for macOS: document-2053ad5f…, revision 9248c6e9…).
        commandLine.AppendSwitch("disable-features", "AudioServiceOutOfProcess");
    }

    private static partial void ForwardFrame(IPlatformBrowserFrameSink sink, CefPaintElementType type, CefAcceleratedPaintInfo info)
    {
        var buffer = new BrowserGpuBuffer(info.SharedTexture);
        ForwardBuffer(sink, type, info, in buffer);
    }

    partial void StartAudio(IPlatformBrowserAudioSink audio)
    {
        // After CefRuntime.Load and before Initialize: the redirection must be in place before CEF creates any audio unit.
        BrowserAudioMixer owned = mixer ?? throw new InvalidOperationException("The macOS browser requires its audio mixer; create it with CefPlatformBrowser.Create(BrowserAudioMixer).");
        AudioOutputRedirection.Install(owned);
        SpeechRedirection.Install(owned);
        audioSink = audio;
    }

    partial void PumpAudio()
    {
        if (audioSink is not null)
        {
            mixer!.DrainTo(audioSink);
        }
    }

    partial void FinishAudio()
    {
        // CEF has disposed its audio units by now; a unit still tracked would mean Chromium audio outlived CEF.
        if (AudioOutputRedirection.TrackedUnits != 0)
        {
            mixer!.Fail($"{AudioOutputRedirection.TrackedUnits} browser audio streams were still open after CEF shutdown.");
        }

        if (audioSink is not null)
        {
            mixer!.DrainTo(audioSink);
            audioSink.OnAudioStopped();
        }
    }
}
