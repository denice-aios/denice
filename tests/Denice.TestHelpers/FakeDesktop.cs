// Copyright (c) Denice.ai. All rights reserved.

using System;
using Denice.Abstractions.Contracts;

namespace Denice.TestHelpers;

/// <summary>Returns a selected desktop outcome without opening a native window.</summary>
public sealed class FakeDesktop : IPlatformDesktop
{
    private readonly int exit;

    private FakeDesktop(int exit, string? recordingError)
    {
        this.exit = exit;
        LastRecordingError = recordingError;
    }

    /// <inheritdoc />
    public string? LastRecordingError { get; }

    /// <inheritdoc />
    public string LastFrameStatistics => "desktop test completed";

    /// <summary>Creates a desktop result for command-level tests.</summary>
    /// <param name="exit">The result returned by the desktop loop.</param>
    /// <param name="recordingError">The optional recording failure after startup.</param>
    /// <returns>The fake desktop capability.</returns>
    public static IPlatformDesktop Create(int exit, string? recordingError) => new FakeDesktop(exit, recordingError);

    /// <inheritdoc />
    public int Run(Uri initialUrl, string? recordingPath, double? durationSeconds, TimeProvider clock, string browserCachePath, int browserDebugPort) => exit;
}
