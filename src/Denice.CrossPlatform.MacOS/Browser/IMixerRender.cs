// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.CrossPlatform.Browser;

/// <summary>Fills one output cycle on the device's real-time thread: no locks, no allocation.</summary>
internal interface IMixerRender
{
    /// <summary>Fills one cycle.</summary>
    /// <param name="left">The left channel to fill.</param>
    /// <param name="right">The right channel to fill.</param>
    /// <param name="hostNanoseconds">The cycle's output time on the host clock.</param>
    void RenderCycle(Span<float> left, Span<float> right, long hostNanoseconds);
}
