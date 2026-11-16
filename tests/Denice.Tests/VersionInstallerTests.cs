// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using Denice.Updates;
using FluentAssertions;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// Installing a downloaded release (pkg-a0206b25f619): the version is unpacked to <c>.partial</c>, every file is checked
/// against <c>SHA256SUMS</c>, and only a fully checked version is renamed into <c>versions/&lt;version&gt;</c>. A release
/// that fails any check leaves neither a version nor a partial folder behind.
/// </summary>
public sealed class VersionInstallerTests
{
    private const string Version = "0.1.200-pre-release+bbbbbbb";
    private const int UnixExecutable = 0b111_101_101 << 16;

    private readonly ISystemServices services;
    private readonly string root;
    private readonly VersionInstaller installer;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionInstallerTests"/> class over a fresh temporary install root.
    /// </summary>
    public VersionInstallerTests()
    {
        services = SystemServicesBuilder.Real().Build();
        root = services.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-install-");
        installer = new VersionInstaller(services, root);
    }

    private string Final => Path.Combine(root, LoaderProtocol.VersionsDirectory, Version);

    private string Partial => Final + LoaderProtocol.PartialSuffix;

    [Fact]
    public async Task Install_ChecksAndRenamesTheVersionIntoPlace()
    {
        InstallOutcome outcome = await installer.InstallAsync(Release(Files()), CancellationToken.None);

        outcome.Should().Be(new InstallOutcome(InstallResult.Installed, Version));
        Read(Path.Combine(Final, "Denice.Runtime.dll")).Should().Be("runtime");
        Read(Path.Combine(Final, "de", "System.CommandLine.resources.dll")).Should().Be("german");
        Directories().DirectoryExists(Partial).Should().BeFalse();
    }

    [Fact]
    public async Task Install_KeepsTheExecutableFlagWhereThePlatformHasOne()
    {
        await installer.InstallAsync(Release(Files()), CancellationToken.None);

        IPlatformFileSystem platform = services.Platform.FileSystem;
        string entry = Path.Combine(Final, $"denice-v{Version}-osx-arm64");
        platform.IsExecutable(entry).Should().Be(platform.NeedsExecutableFlag());
    }

    [Fact]
    public async Task Install_OfAnInstalledVersion_ChangesNothing()
    {
        await installer.InstallAsync(Release(Files()), CancellationToken.None);

        InstallOutcome again = await installer.InstallAsync(Release(Files()), CancellationToken.None);

        again.Result.Should().Be(InstallResult.AlreadyInstalled);
    }

    [Fact]
    public async Task Install_ReplacesAStalePartialFolderFromAnInterruptedDownload()
    {
        IDirectoryWriter writer = services.FileSystem.GetDirectoryWriter();
        writer.CreateDirectory(Partial);
        services.FileSystem.GetFileWriter().WriteAllText(Path.Combine(Partial, "leftover"), "x");

        InstallOutcome outcome = await installer.InstallAsync(Release(Files()), CancellationToken.None);

        outcome.Result.Should().Be(InstallResult.Installed);
        services.FileSystem.GetFileReader().Exists(Path.Combine(Final, "leftover")).Should().BeFalse();
    }

    [Fact]
    public async Task Install_OfATamperedFile_InstallsNothing()
    {
        Dictionary<string, string> files = Files();
        string sums = Sums(files);
        files["Denice.Runtime.dll"] = "tampered";

        Func<Task> act = () => installer.InstallAsync(Release(files, sums), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage("*does not match its checksum*");
        NothingInstalled();
    }

    [Fact]
    public async Task Install_WithAFileNotInTheChecksums_InstallsNothing()
    {
        Dictionary<string, string> files = Files();
        string sums = Sums(files);
        files["extra.dll"] = "smuggled";

        Func<Task> act = () => installer.InstallAsync(Release(files, sums), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage("*not listed in SHA256SUMS*");
        NothingInstalled();
    }

    [Fact]
    public async Task Install_WithAListedFileMissing_InstallsNothing()
    {
        Dictionary<string, string> files = Files();
        string sums = Sums(files);
        files.Remove("Denice.Updates.dll");

        Func<Task> act = () => installer.InstallAsync(Release(files, sums), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage("*listed in SHA256SUMS but missing*");
        NothingInstalled();
    }

    [Fact]
    public async Task Install_WithoutChecksums_InstallsNothing()
    {
        Func<Task> act = () => installer.InstallAsync(Release(Files(), sums: null), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage("*no SHA256SUMS*");
        NothingInstalled();
    }

    [Fact]
    public async Task Install_WithAMalformedChecksumLine_InstallsNothing()
    {
        Func<Task> act = () => installer.InstallAsync(Release(Files(), "not a checksum line\n"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage("*malformed line*");
        NothingInstalled();
    }

    [Fact]
    public async Task Install_OfAReleaseWithTwoVersions_IsRefused()
    {
        byte[] zip = Zip(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"versions/{Version}/a"] = "a",
            ["versions/0.1.300-pre-release+ccccccc/b"] = "b",
        });

        Func<Task> act = () => installer.InstallAsync(zip, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage("*exactly one versions/<version>/ folder*");
    }

    [Fact]
    public async Task Install_OfAnEntryThatLeavesItsVersionFolder_IsRefused()
    {
        byte[] zip = Zip(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"versions/{Version}/../../escaped"] = "x",
        });

        Func<Task> act = () => installer.InstallAsync(zip, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage("*leaves its version folder*");
        services.FileSystem.GetFileReader().Exists(Path.Combine(root, "escaped")).Should().BeFalse();
    }

    [Fact]
    public async Task Install_OnTheSimulatedFileSystem_MovesTheWholeTreeAndLeavesTheRealDiskUntouched()
    {
        ISystemServices simulated = SystemServicesBuilder.Real().SimulateFileSystem().Build();
        var simulatedInstaller = new VersionInstaller(simulated, root);

        await simulatedInstaller.InstallAsync(Release(Files()), CancellationToken.None);

        simulated.FileSystem.GetFileReader().ReadAllText(Path.Combine(Final, "de", "System.CommandLine.resources.dll"))
            .Should().Be("german");
        simulated.FileSystem.GetDirectoryReader().DirectoryExists(Partial).Should().BeFalse();
        Directories().DirectoryExists(Final).Should().BeFalse("the overlay never writes to the real disk");
    }

    [Fact]
    public void Constructor_RejectsMissingArguments()
    {
        Action noServices = () => _ = new VersionInstaller(null!, root);
        Action noRoot = () => _ = new VersionInstaller(services, string.Empty);

        noServices.Should().Throw<ArgumentNullException>();
        noRoot.Should().Throw<ArgumentException>();
    }

    private static Dictionary<string, string> Files() => new(StringComparer.Ordinal)
    {
        ["Denice.Runtime.dll"] = "runtime",
        ["Denice.Updates.dll"] = "updates",
        [$"denice-v{Version}-osx-arm64"] = "entry",
        ["de/System.CommandLine.resources.dll"] = "german",
    };

    private static string Sums(Dictionary<string, string> files) => string.Concat(
        files.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            $"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Value)))}  {pair.Key}\n"));

    private static byte[] Release(Dictionary<string, string> files) => Release(files, Sums(files));

    private static byte[] Release(Dictionary<string, string> files, string? sums)
    {
        var entries = files.ToDictionary(pair => $"versions/{Version}/{pair.Key}", pair => pair.Value, StringComparer.Ordinal);
        if (sums is not null)
        {
            entries[$"versions/{Version}/SHA256SUMS"] = sums;
            entries[$"versions/{Version}/SHA256SUMS.cosign.bundle"] = "{}";
        }

        entries["denice"] = "loader";
        return Zip(entries);
    }

    private static byte[] Zip(Dictionary<string, string> entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (KeyValuePair<string, string> pair in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(pair.Key);
                if (pair.Key.Contains("denice-v", StringComparison.Ordinal))
                {
                    entry.ExternalAttributes = UnixExecutable;
                }

                using Stream content = entry.Open();
                content.Write(Encoding.UTF8.GetBytes(pair.Value));
            }
        }

        return stream.ToArray();
    }

    private IDirectoryEnumerator Directories() => services.FileSystem.GetDirectoryReader();

    private string Read(string path) => services.FileSystem.GetFileReader().ReadAllText(path);

    private void NothingInstalled()
    {
        Directories().DirectoryExists(Final).Should().BeFalse();
        Directories().DirectoryExists(Partial).Should().BeFalse();
    }
}
