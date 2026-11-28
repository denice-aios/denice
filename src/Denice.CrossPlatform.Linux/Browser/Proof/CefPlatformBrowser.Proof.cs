// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Linux.Browser;

namespace Denice.CrossPlatform.Browser;

/// <summary>
/// Linux, compile-selected browser-audio redirection proof only (DeniceAudioRedirectionProof): the approved optional
/// audio hooks install the libcef.so redirection before CEF initializes, hand the mixer's output to the audio sink after
/// each pump and complete it after shutdown. Normal builds do not compile this file.
/// </summary>
internal sealed partial class CefPlatformBrowser
{
    private MixerCore? proofMixer;
    private IPlatformBrowserAudioSink? proofSink;

    /// <summary>Creates the browser whose audio goes through the given started mixer core.</summary>
    /// <param name="mixer">The mixer core; its output is started before Start and stopped after Shutdown.</param>
    /// <returns>The browser capability.</returns>
    internal static IPlatformBrowser CreateWithAudioProof(MixerCore mixer)
    {
        ArgumentNullException.ThrowIfNull(mixer);
        return new CefPlatformBrowser { proofMixer = mixer };
    }

    partial void StartAudio(IPlatformBrowserAudioSink audio)
    {
        if (proofMixer is null)
        {
            return;
        }

        LinuxAudioRedirection.Install(proofMixer);
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

        if (PulseRedirection.OpenStreams != 0 || AlsaRedirection.OpenStreams != 0)
        {
            proofMixer!.Fail($"{PulseRedirection.OpenStreams} PulseAudio and {AlsaRedirection.OpenStreams} ALSA redirected browser streams were still open after CEF shutdown.");
        }

        proofMixer!.DrainTo(proofSink);
        proofSink.OnAudioStopped();
    }
}
