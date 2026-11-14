// Copyright (c) Denice.ai. All rights reserved.

using System.IO;
using System.Linq;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// Drives the shared helpers every per-OS file system reuses — the case-sensitivity fallback probe and the managed
/// symlink removal — directly on a real temporary directory, so they are proven on every OS and not only where a
/// per-OS implementation happens to call them.
/// </summary>
public sealed class PlatformFileSystemSharedTests : IDisposable
{
    private readonly ISystemServices services;
    private readonly string root;
    private readonly PlatformFileSystemShared shared;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformFileSystemSharedTests"/> class over a fresh temporary
    /// directory and the production composition of the shared helpers.
    /// </summary>
    public PlatformFileSystemSharedTests()
    {
        services = SystemServicesBuilder.Real().Build();
        root = services.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-shared-spec-");
        shared = PlatformFileSystemComposition.Create().Shared;
    }

    [Fact]
    public void ProbeCaseSensitive_AgreesWithThePlatformsOwnAnswer()
    {
        services.FileSystem.GetFileWriter().WriteAllText(Path.Combine(root, "Probe.txt"), "probe");

        bool probed = shared.ProbeCaseSensitive(root);

        probed.Should().Be(services.Platform.FileSystem.IsCaseSensitive(root));
    }

    [Fact]
    public void ProbeCaseSensitive_WithNothingProbeable_DefaultsToCaseSensitive()
    {
        services.FileSystem.GetFileWriter().WriteAllText(Path.Combine(root, "1234"), "no letters");

        shared.ProbeCaseSensitive(root).Should().BeTrue();
    }

    [Fact]
    public void DeleteLinkEntry_RemovesADirectoryLinkButLeavesItsTarget()
    {
        string target = Path.Combine(root, "target");
        services.FileSystem.GetDirectoryWriter().CreateDirectory(target);
        string link = Path.Combine(root, "current");
        services.Platform.FileSystem.MakeLinkTarget(link, "target");

        shared.DeleteLinkEntry(link, isLink: true);

        Entries().Should().BeEquivalentTo("target");
    }

    [Fact]
    public void DeleteLinkEntry_WhenThePathIsNotALink_LeavesItUntouched()
    {
        string file = Path.Combine(root, "real.txt");
        services.FileSystem.GetFileWriter().WriteAllText(file, "real");

        shared.DeleteLinkEntry(file, isLink: false);

        Entries().Should().BeEquivalentTo("real.txt");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            services.FileSystem.GetDirectoryWriter().DeleteDirectory(root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing to clean up.
        }
    }

    private string[] Entries() =>
        services.FileSystem.GetDirectoryReader().EnumerateChildren(root)
            .Select(child => Path.GetFileName(child.FullPath)).OfType<string>().ToArray();
}
