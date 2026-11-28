// Copyright (c) Denice.ai. All rights reserved.

using Denice.CrossPlatform.MacOS.Browser;

namespace Denice.CrossPlatform.Tests;

/// <summary>The macOS half of the mixer harness: Core Audio render structures for the macOS mixer's render entry point.</summary>
internal static partial class MixerHarness
{
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
}
