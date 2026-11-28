// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>Owns the native desktop window, graphics composition, and recording lifetime.</summary>
public interface IPlatformDesktop
{
    /// <summary>Gets the most recent recording failure while allowing the desktop to continue.</summary>
    string? LastRecordingError { get; }

    /// <summary>
    /// Gets the first browser audio failure of the latest run, whether or not it recorded; null when audio had no failure.
    /// A failure does not stop the run; it is reported after the desktop closes.
    /// </summary>
    string? LastAudioError { get; }

    /// <summary>Gets the measured rendering and recording timings for the latest run.</summary>
    string LastFrameStatistics { get; }

    /// <summary>Runs the desktop on the calling application main thread until its window closes.</summary>
    /// <param name="application">The supervised application server; the desktop shows its startup page, then follows its state.</param>
    /// <param name="recordingPath">The optional MP4 destination, which must not already exist.</param>
    /// <param name="durationSeconds">An optional duration, counted from the first application page, after which to close and finalize recording.</param>
    /// <param name="clock">The host clock used for frame scheduling and bounded waits.</param>
    /// <param name="browserCachePath">The selected browser profile directory.</param>
    /// <param name="browserDebugPort">The optional localhost debugging port, or zero.</param>
    /// <param name="browserGpuCopyTimeout">How long a borrowed browser GPU copy may stay pending (setting browser.gpuCopyTimeoutSeconds).</param>
    /// <returns>Zero on normal exit; nonzero if startup fails.</returns>
    int Run(IApplicationSession application, string? recordingPath, double? durationSeconds, TimeProvider clock, string browserCachePath, int browserDebugPort, TimeSpan browserGpuCopyTimeout);
}
