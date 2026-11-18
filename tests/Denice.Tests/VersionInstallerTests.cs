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
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// Installing a downloaded release (pkg-a0206b25f619): the version is unpacked to <c>.partial</c>, every file is checked
/// against <c>SHA256SUMS</c>, and only a fully checked version is renamed into <c>versions/&lt;version&gt;</c>. A release
/// that fails any check leaves neither a version nor a partial folder behind. These run on the simulated file system,
/// whose platform has no OS code signature (as on Linux), with a stand-in cosign check; the real signature checks are
/// proven in <see cref="VersionInstallerSignatureTests"/> and <see cref="ReleaseSignatureTests"/>.
/// </summary>
public sealed class VersionInstallerTests
{
    private const string Version = "0.1.200-pre-release+bbbbbbb";
    private const string Rid = "osx-arm64";

    private readonly ISystemServices services;
    private readonly string root;
    private readonly VersionInstaller installer;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionInstallerTests"/> class over a fresh temporary install root.
    /// </summary>
    public VersionInstallerTests()
    {
        services = SystemServicesBuilder.Real().SimulateFileSystem().Build();
        root = services.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-install-");
        installer = new VersionInstaller(services, root, Rid, AcceptingCosign);
    }

    private static ReleaseTrust AcceptingCosign { get; } =
        new((_, _, _) => Task.FromResult<string?>(null), DeniceSigningCertificates.Sha256Fingerprints.ToArray());

    /// <summary>A file in the version folder that shares the entry's prefix but is not the entry (a debug file).</summary>
    private static string DebugFile => $"denice-v{Version}-{Rid}.pdb";

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
        string library = Path.Combine(Final, "Denice.Runtime.dll");
        if (platform.NeedsExecutableFlag())
        {
            platform.IsExecutable(entry).Should().BeTrue("the release marked the entry executable");
            platform.IsExecutable(library).Should().BeFalse("the release did not mark the library executable");
        }
        else
        {
            // Windows has no executable flag: whether a file runs depends on its type, not on a flag the install sets.
            services.FileSystem.GetFileReader().Exists(entry).Should().BeTrue();
        }
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
    public async Task Install_WhoseCosignSignatureIsNotDenices_InstallsNothing()
    {
        var refusing = new VersionInstaller(
            services,
            root,
            Rid,
            new ReleaseTrust((_, _, _) => Task.FromResult<string?>("not Denice's release workflow"), []));

        Func<Task> act = () => refusing.InstallAsync(Release(Files()), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage("*not Denice's release workflow*");
        NothingInstalled();
    }

    [Fact]
    public async Task Install_WithoutACosignBundle_InstallsNothing()
    {
        Dictionary<string, string> files = Files();
        byte[] zip = Zip(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"versions/{Version}/SHA256SUMS"] = Sums(files),
            [$"versions/{Version}/Denice.Runtime.dll"] = files["Denice.Runtime.dll"],
            [$"versions/{Version}/Denice.Updates.dll"] = files["Denice.Updates.dll"],
            [$"versions/{Version}/denice-v{Version}-osx-arm64"] = files[$"denice-v{Version}-osx-arm64"],
            [$"versions/{Version}/de/System.CommandLine.resources.dll"] = files["de/System.CommandLine.resources.dll"],
        });

        Func<Task> act = () => installer.InstallAsync(zip, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage("*no SHA256SUMS.cosign.bundle*");
        NothingInstalled();
    }

    [Fact]
    public async Task TheCosignCheck_IsGivenExactlyTheChecksumsFileAndBundle()
    {
        byte[]? seenChecksums = null;
        string? seenBundle = null;
        var recording = new VersionInstaller(services, root, Rid, new ReleaseTrust(
            (checksums, bundle, _) =>
            {
                seenChecksums = checksums.ToArray();
                seenBundle = bundle;
                return Task.FromResult<string?>(null);
            },
            []));

        await recording.InstallAsync(Release(Files()), CancellationToken.None);

        Encoding.UTF8.GetString(seenChecksums!).Should().Be(Sums(Files()));
        seenBundle.Should().Be("{}");
    }

    [Fact]
    public async Task AnotherFileSharingTheEntrysPrefix_IsCoveredByTheChecksums()
    {
        Dictionary<string, string> files = WithDebugFile(Files());
        string sums = Sums(files);
        files[DebugFile] = "tampered";

        Func<Task> act = () => installer.InstallAsync(Release(files, sums), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage($"*'{DebugFile}' does not match its checksum*");
        NothingInstalled();
    }

    [Fact]
    public async Task AnotherFileSharingTheEntrysPrefix_MustBeListedInTheChecksums()
    {
        Dictionary<string, string> files = Files();
        string sums = Sums(files);

        Func<Task> act = () => installer.InstallAsync(Release(WithDebugFile(files), sums), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage($"*'{DebugFile}' is not listed in SHA256SUMS*");
        NothingInstalled();
    }

    [Fact]
    public async Task AnotherFileSharingTheEntrysPrefix_IsInTheChecksumsTheCosignCheckSigns()
    {
        byte[]? seenChecksums = null;
        var recording = new VersionInstaller(services, root, Rid, new ReleaseTrust(
            (checksums, _, _) =>
            {
                seenChecksums = checksums.ToArray();
                return Task.FromResult<string?>(null);
            },
            []));

        await recording.InstallAsync(Release(WithDebugFile(Files())), CancellationToken.None);

        Encoding.UTF8.GetString(seenChecksums!).Should().Contain($"  {DebugFile}\n");
    }

    [Fact]
    public async Task Install_WhileAnotherDeniceKeepsTheInstallLock_GivesUpAfterTwoMinutesWithAClearError()
    {
        var clock = new FakeTimeProvider();
        DateTimeOffset start = clock.GetUtcNow();
        ISystemServices waiting = SystemServicesBuilder.Real().SimulateFileSystem().With(clock).Build();
        string waitingRoot = waiting.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-install-");
        string lockPath = Path.Combine(waitingRoot, LoaderProtocol.VersionsDirectory, LoaderProtocol.InstallLock);
        using IDisposable? held = waiting.Platform.FileSystem.TryLockExclusive(lockPath);

        Task<InstallOutcome> install =
            new VersionInstaller(waiting, waitingRoot, Rid, AcceptingCosign).InstallAsync(Release(Files()), CancellationToken.None);
        for (int step = 0; step < 100_000 && !install.IsCompleted; step++)
        {
            install.IsCompleted.Should().BeFalse();
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        (await install.Awaiting(task => task).Should().ThrowAsync<IOException>())
            .WithMessage($"Another Denice held '*{LoaderProtocol.InstallLock}' for 120 seconds*nothing was installed.");
        clock.GetUtcNow().Should().BeOnOrAfter(start + TimeSpan.FromMinutes(2));
        waiting.FileSystem.GetDirectoryReader()
            .DirectoryExists(Path.Combine(waitingRoot, LoaderProtocol.VersionsDirectory, Version)).Should().BeFalse();
    }

    [Fact]
    public void Constructor_RejectsMissingArguments()
    {
        Action noServices = () => _ = new VersionInstaller(null!, root, Rid);
        Action noRoot = () => _ = new VersionInstaller(services, string.Empty, Rid);
        Action noRid = () => _ = new VersionInstaller(services, root, string.Empty);
        Action noTrust = () => _ = new VersionInstaller(services, root, Rid, null!);

        noServices.Should().Throw<ArgumentNullException>();
        noRoot.Should().Throw<ArgumentException>();
        noRid.Should().Throw<ArgumentException>();
        noTrust.Should().Throw<ArgumentNullException>();
    }

    private static Dictionary<string, string> Files() => TestReleases.Files(Version);

    private static Dictionary<string, string> WithDebugFile(Dictionary<string, string> files)
    {
        files[DebugFile] = "debug symbols";
        return files;
    }

    private static string Sums(Dictionary<string, string> files) => TestReleases.Sums(files);

    private static byte[] Release(Dictionary<string, string> files) => TestReleases.Release(Version, files);

    private static byte[] Release(Dictionary<string, string> files, string? sums) => TestReleases.Release(Version, files, sums);

    private static byte[] Zip(Dictionary<string, string> entries) => TestReleases.Zip(entries);

    private IDirectoryEnumerator Directories() => services.FileSystem.GetDirectoryReader();

    private string Read(string path) => services.FileSystem.GetFileReader().ReadAllText(path);

    private void NothingInstalled()
    {
        Directories().DirectoryExists(Final).Should().BeFalse();
        Directories().DirectoryExists(Partial).Should().BeFalse();
    }
}
