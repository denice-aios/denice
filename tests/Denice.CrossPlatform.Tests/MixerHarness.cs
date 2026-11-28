// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform.Tests;

/// <summary>Shared harness for driving the browser audio mixer without an output device.</summary>
internal static partial class MixerHarness
{
    /// <summary>A stream format the fake sink received.</summary>
    /// <param name="Channels">The channel count.</param>
    /// <param name="Rate">The sample rate.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct AudioFormat(int Channels, int Rate);

    /// <summary>What the fake sink received.</summary>
    internal sealed class SinkLog
    {
        internal AudioFormat Format { get; set; }

        internal List<float[]> Packets { get; } = [];

        /// <summary>Gets each packet's presentation time in milliseconds on the host clock, in the same order as <see cref="Packets"/>.</summary>
        internal List<long> PacketTimes { get; } = [];

        /// <summary>Gets the right channel of each packet, in the same order as <see cref="Packets"/>.</summary>
        internal List<float[]> RightPackets { get; } = [];

        internal List<long> PacketSpeech { get; } = [];

        internal List<string> Errors { get; } = [];

        internal bool Stopped { get; set; }

        /// <summary>Gets or sets where the mixer's speech position is read from when a packet arrives.</summary>
        internal Func<long> SpeechProgress { get; set; } = () => 0;

        /// <summary>Finds runs of sounding packets during which no speech was mixed: the page's tones, with their pitch.</summary>
        /// <returns>The tones in order.</returns>
        internal IReadOnlyList<Tone> Tones()
        {
            var tones = new List<Tone>();
            int index = 0;
            while (index < Packets.Count)
            {
                if (Peak(Packets[index]) < 0.05f)
                {
                    index++;
                    continue;
                }

                int first = index;
                while (index < Packets.Count && Peak(Packets[index]) >= 0.01f && PacketSpeech[index] == PacketSpeech[first])
                {
                    index++;
                }

                float[] run = [.. Packets.Skip(first).Take(index - first).SelectMany(packet => packet)];
                if (run.Length >= 4800 && PacketSpeech[Math.Min(index, Packets.Count - 1)] == PacketSpeech[first])
                {
                    int crossings = run.Zip(run.Skip(1)).Count(pair => (pair.First < 0) != (pair.Second < 0));
                    tones.Add(new Tone(crossings / 2.0 * 48000 / run.Length, PacketSpeech[first]));
                }
            }

            return tones;
        }

        private static float Peak(float[] samples) => samples.Max(Math.Abs);

        /// <summary>A tone heard by the mixer.</summary>
        /// <param name="Hertz">Its pitch from zero crossings.</param>
        /// <param name="SpeechFramesBefore">Speech frames mixed before it started.</param>
        [StructLayout(LayoutKind.Auto)]
        internal readonly record struct Tone(double Hertz, long SpeechFramesBefore);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "The mixer's render entry point and Chromium's callbacks take unmanaged Core Audio structures.")]
    internal sealed unsafe class LoggingSink : IPlatformBrowserAudioSink
    {
        private readonly SinkLog _log;

        private LoggingSink(SinkLog log) => _log = log;

        public void OnAudioStarted(int channels, int sampleRate) => _log.Format = new AudioFormat(channels, sampleRate);

        public void OnAudioPacket(nint planarFloatPointers, int frames, long pts)
        {
            _log.Packets.Add(new Span<float>(*(float**)planarFloatPointers, frames).ToArray());
            _log.PacketTimes.Add(pts);
            _log.RightPackets.Add(new Span<float>(*((float**)planarFloatPointers + 1), frames).ToArray());
            _log.PacketSpeech.Add(_log.SpeechProgress());
        }

        public void OnAudioStopped() => _log.Stopped = true;

        public void OnAudioError(string message) => _log.Errors.Add(message);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Contract fakes are created behind their interface (DN0003, DN0006).")]
        internal static IPlatformBrowserAudioSink Create(SinkLog log) => new LoggingSink(log);
    }
}
