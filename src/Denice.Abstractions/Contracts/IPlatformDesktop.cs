// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>Owns the native desktop window, graphics composition, and recording lifetime.</summary>
public interface IPlatformDesktop
{
    /// <summary>Gets the most recent recording failure while allowing the desktop to continue.</summary>
    string? LastRecordingError { get; }

    /// <summary>Gets the measured rendering and recording timings for the latest run.</summary>
    string LastFrameStatistics { get; }

    /// <summary>Runs the desktop on the calling application main thread until its window closes.</summary>
    /// <param name="initialUrl">The local application page.</param>
    /// <param name="recordingPath">The optional MP4 destination, which must not already exist.</param>
    /// <param name="durationSeconds">An optional duration after which to close and finalize recording.</param>
    /// <param name="clock">The host clock used for frame scheduling.</param>
    /// <param name="browserCachePath">The selected browser profile directory.</param>
    /// <param name="browserDebugPort">The optional localhost debugging port, or zero.</param>
    /// <returns>Zero on normal exit; nonzero if startup fails.</returns>
    int Run(Uri initialUrl, string? recordingPath, double? durationSeconds, TimeProvider clock, string browserCachePath, int browserDebugPort);
}
