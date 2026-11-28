// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics;
using System.Text;

namespace Denice.CrossPlatform.MacOS.Browser;

/// <summary>
/// The browser-audio event log for diagnostic builds (DeniceAudioDiagnostics). Every call is removed, arguments included,
/// from normal builds; it is never called from the audio render thread.
/// </summary>
internal static class AudioDiagnostics
{
    private static readonly Lock Gate = new();
    private static readonly StringBuilder Log = new();

    /// <summary>Gets the collected events.</summary>
    internal static string Events
    {
        get
        {
            lock (Gate)
            {
                return Log.ToString();
            }
        }
    }

    /// <summary>Appends one event.</summary>
    /// <param name="text">The event.</param>
    [Conditional("DENICE_AUDIO_DIAGNOSTICS")]
    internal static void Note(string text)
    {
        lock (Gate)
        {
            if (Log.Length < 64000)
            {
                Log.Append(text).Append("; ");
            }
        }
    }
}
