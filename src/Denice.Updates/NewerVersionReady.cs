// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Updates;

/// <summary>
/// A newer version is installed while a long-running session runs (Tim: "A ready newer version triggers the decided
/// restart"), so the session ends with <see cref="ExitCode"/> and the loader starts the newer version.
/// </summary>
/// <param name="Version">The installed version.</param>
public sealed record NewerVersionReady(string Version)
{
    /// <summary>
    /// Gets the exit code the session ends with: <see cref="LoaderProtocol.ExitRestartIntoNewerVersion"/>, which asks the
    /// loader to restart into the newer version.
    /// </summary>
    public int ExitCode { get; } = LoaderProtocol.ExitRestartIntoNewerVersion;
}
