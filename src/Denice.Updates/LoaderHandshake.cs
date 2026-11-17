// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.IO;
using Denice.Abstractions.Contracts;

namespace Denice.Updates;

/// <summary>
/// What the loader told this version when it started it, read from the environment (eng/loader-protocol.json).
/// When Denice Runtime runs without a loader (a developer run or a test), there is no handshake.
/// </summary>
public sealed class LoaderHandshake
{
    private readonly IFileWriter _writer;

    private LoaderHandshake(IFileWriter writer, string installRoot, string loaderPath, string loaderVersion, string versionDirectory)
    {
        _writer = writer;
        InstallRoot = installRoot;
        LoaderPath = loaderPath;
        LoaderVersion = loaderVersion;
        VersionDirectory = versionDirectory;
    }

    /// <summary>Gets the install root (the folder holding the loader and <c>versions</c>).</summary>
    public string InstallRoot { get; }

    /// <summary>Gets the running loader's path.</summary>
    public string LoaderPath { get; }

    /// <summary>Gets the running loader's version.</summary>
    public string LoaderVersion { get; }

    /// <summary>Gets this version's folder.</summary>
    public string VersionDirectory { get; }

    /// <summary>
    /// Reads the handshake the loader passed, or returns <see langword="null"/> when this version was not started by
    /// a loader (any of the four values missing).
    /// </summary>
    /// <param name="services">The owned services the environment and file writer come from.</param>
    /// <returns>The handshake, or <see langword="null"/>.</returns>
    public static LoaderHandshake? Read(ISystemServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        IEnvironment environment = services.Environment;
        string? installRoot = environment.GetEnvironmentVariable(LoaderProtocol.EnvInstallRoot);
        string? loaderPath = environment.GetEnvironmentVariable(LoaderProtocol.EnvLoaderPath);
        string? loaderVersion = environment.GetEnvironmentVariable(LoaderProtocol.EnvLoaderVersion);
        string? versionDirectory = environment.GetEnvironmentVariable(LoaderProtocol.EnvVersionDirectory);
        if (string.IsNullOrEmpty(installRoot) || string.IsNullOrEmpty(loaderPath)
            || string.IsNullOrEmpty(loaderVersion) || string.IsNullOrEmpty(versionDirectory))
        {
            return null;
        }

        return new LoaderHandshake(services.FileSystem.GetFileWriter(), installRoot, loaderPath, loaderVersion, versionDirectory);
    }

    /// <summary>
    /// Records that this version has started, so the loader keeps it and removes older versions
    /// (pkg-a0206b25f619: "the previous version stays until the new one has started successfully once").
    /// </summary>
    public void RecordStarted() => _writer.WriteAllText(Path.Combine(VersionDirectory, LoaderProtocol.StartedMarker), string.Empty);
}
