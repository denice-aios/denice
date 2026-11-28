// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Windows.Browser;

namespace Denice.CrossPlatform.Browser;

/// <summary>
/// Windows, compile-selected browser-audio redirection proof only (DeniceAudioRedirectionProof): the approved optional
/// audio hooks patch libcef.dll's audio imports before CEF initializes, hand the mixer's output to the audio sink after
/// each pump and complete it after shutdown. Normal builds do not compile this file.
/// </summary>
internal sealed partial class CefPlatformBrowser
{
    private MixerCore? proofMixer;
    private TimeProvider? proofClock;
    private IPlatformBrowserAudioSink? proofSink;

    /// <summary>Creates the browser whose audio goes through the given started mixer core.</summary>
    /// <param name="mixer">The mixer core; its output is started before Start and stopped after Shutdown.</param>
    /// <param name="clock">The injected clock.</param>
    /// <returns>The browser capability.</returns>
    internal static IPlatformBrowser CreateWithAudioProof(MixerCore mixer, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(mixer);
        ArgumentNullException.ThrowIfNull(clock);
        return new CefPlatformBrowser { proofMixer = mixer, proofClock = clock };
    }

    partial void StartAudio(IPlatformBrowserAudioSink audio)
    {
        if (proofMixer is null || proofClock is null)
        {
            return;
        }

        WindowsAudioRedirection.Install(proofMixer, proofClock);
        proofSink = audio;
    }

    partial void PumpAudio()
    {
        if (proofSink is not null)
        {
            proofMixer!.DrainTo(proofSink);
        }
    }

    partial void FinishAudio()
    {
        if (proofSink is null)
        {
            return;
        }

        proofMixer!.DrainTo(proofSink);
        proofSink.OnAudioStopped();
    }
}
