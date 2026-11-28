// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.MacOS.Browser;

/// <summary>
/// The Core Audio render cycle in progress: its AudioTimeStamp, which Chromium's render callbacks and AudioConverter
/// receive. Written by the render thread before each cycle, read by the redirected streams during it.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio interop: render callbacks exchange stable unmanaged buffers with AudioToolbox and Chromium.")]
internal sealed unsafe class CoreAudioCycle
{
    /// <summary>Gets the AudioTimeStamp of the cycle in progress.</summary>
    internal nint Timestamp { get; private set; }

    /// <summary>Runs one Core Audio render cycle into a two-buffer non-interleaved list.</summary>
    /// <param name="render">The mixer render.</param>
    /// <param name="timestamp">The cycle's AudioTimeStamp.</param>
    /// <param name="frames">The frames requested.</param>
    /// <param name="bufferList">The output AudioBufferList with two float buffers.</param>
    internal void Render(IMixerRender render, nint timestamp, int frames, nint bufferList)
    {
        Timestamp = timestamp;
        float* left = (float*)Marshal.ReadIntPtr(bufferList + 8 + 8);
        float* right = (float*)Marshal.ReadIntPtr(bufferList + 8 + 16 + 8);
        long host = AudioOutputRedirection.HostTimeToNanoseconds(*(ulong*)(timestamp + 8));
        render.RenderCycle(new Span<float>(left, frames), new Span<float>(right, frames), host);
    }
}
