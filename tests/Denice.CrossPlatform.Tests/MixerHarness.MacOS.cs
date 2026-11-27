// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.MacOS.Browser;

namespace Denice.CrossPlatform.Tests;

/// <summary>Shared harness for driving the browser audio mixer without an output device.</summary>
internal static class MixerHarness
{
    /// <summary>A stream format the fake sink received.</summary>
    /// <param name="Channels">The channel count.</param>
    /// <param name="Rate">The sample rate.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct AudioFormat(int Channels, int Rate);

    /// <summary>Pinned Core Audio structures for one render cycle.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "The mixer's render entry point and Chromium's callbacks take unmanaged Core Audio structures.")]
    internal sealed unsafe class RenderBuffers
    {
        private readonly byte[] _timestamp = GC.AllocateArray<byte>(64, pinned: true);
        private readonly byte[] _bufferList = GC.AllocateArray<byte>(8 + (16 * 2), pinned: true);

        internal RenderBuffers(int frames)
        {
            Left = GC.AllocateArray<float>(frames, pinned: true);
            Right = GC.AllocateArray<float>(frames, pinned: true);
            fixed (byte* list = _bufferList)
            {
                *(int*)list = 2;
                for (int channel = 0; channel < 2; channel++)
                {
                    byte* buffer = list + 8 + (16 * channel);
                    *(int*)buffer = 1;
                    *(int*)(buffer + 4) = frames * sizeof(float);
                    fixed (float* samples = channel == 0 ? Left : Right)
                    {
                        *(nint*)(buffer + 8) = (nint)samples;
                    }
                }
            }
        }

        internal float[] Left { get; }

        internal float[] Right { get; }

        internal void Render(BrowserAudioMixer mixer)
        {
            fixed (byte* timestamp = _timestamp)
            {
                fixed (byte* list = _bufferList)
                {
                    mixer.RenderCycle((nint)timestamp, Left.Length, (nint)list);
                }
            }
        }
    }

    /// <summary>What the fake sink received.</summary>
    internal sealed class SinkLog
    {
        internal AudioFormat Format { get; set; }

        internal List<float[]> Packets { get; } = [];

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
            _log.PacketSpeech.Add(_log.SpeechProgress());
        }

        public void OnAudioStopped() => _log.Stopped = true;

        public void OnAudioError(string message) => _log.Errors.Add(message);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Contract fakes are created behind their interface (DN0003, DN0006).")]
        internal static IPlatformBrowserAudioSink Create(SinkLog log) => new LoggingSink(log);
    }
}
