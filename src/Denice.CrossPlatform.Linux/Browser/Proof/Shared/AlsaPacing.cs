// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.CrossPlatform.Browser;

/// <summary>How far an output writer is ahead of real time; used to pace a device that never blocks.</summary>
internal static class AlsaPacing
{
    /// <summary>The frames written beyond those the clock says have played.</summary>
    /// <param name="framesWritten">The frames written since the start.</param>
    /// <param name="elapsed">The clock time since the start.</param>
    /// <param name="sampleRate">The output rate.</param>
    /// <returns>The frames ahead of real time; negative when behind.</returns>
    internal static long FramesAhead(long framesWritten, TimeSpan elapsed, int sampleRate) =>
        framesWritten - (long)(elapsed.TotalSeconds * sampleRate);
}
