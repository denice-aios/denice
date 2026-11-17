// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;

namespace Denice.Tests;

/// <summary>
/// A build-info fake that reports one chosen version, so an update test can say which version is running. Its
/// constructor is private (DN0003) and it is handed out only as its interface.
/// </summary>
internal sealed class FixedBuildInfo : IBuildInfo
{
    private FixedBuildInfo(string semVer) => SemVer = semVer;

    /// <inheritdoc />
    public string SemVer { get; }

    /// <inheritdoc />
    public string AssemblyVersion => "0.1.0.0";

    /// <inheritdoc />
    public string FileVersion => "0.1.0.0";

    /// <summary>Creates the fake for one running version.</summary>
    /// <param name="semVer">The running version.</param>
    /// <returns>The fake, as its interface.</returns>
    internal static IBuildInfo Create(string semVer) => new FixedBuildInfo(semVer);
}
