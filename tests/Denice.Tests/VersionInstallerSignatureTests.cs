// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using Denice.Updates;
using FluentAssertions;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// The installer's OS code-signature check with genuinely signed files on the real platform: a release whose Denice
/// executables are signed by a pinned certificate installs; the same release with a different certificate pinned does
/// not. The files come from the shared signed fixtures (tests/Denice.CrossPlatform.Tests/Fixtures/CodeSignature), chosen
/// by the platform's own code-signature format, never by checking the OS; on Linux there is no OS code signature, so
/// both releases install on the checksum and cosign checks alone.
/// </summary>
public sealed class VersionInstallerSignatureTests
{
    private const string Version = "0.1.300-pre-release+ccccccc";
    private const string MacFixtureSigner =
        "2B:EA:8E:85:D4:18:37:7F:D9:46:45:ED:16:34:2C:3F:AD:AC:49:AC:AD:61:DC:7D:D1:2E:3E:BD:3E:8D:A6:4A";

    private const string WindowsFixtureSigner =
        "56:51:C6:CB:D1:6D:5E:08:DE:E0:4E:D6:42:DD:3B:88:2D:7B:4F:D9:FF:F0:0D:2F:1C:F7:DE:81:E5:E9:F7:18";

    private static readonly string NotPinned = string.Join(':', Enumerable.Repeat("00", 32));

    private readonly ISystemServices services = SystemServicesBuilder.Real().Build();
    private readonly string fixtures;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionInstallerSignatureTests"/> class.
    /// </summary>
    public VersionInstallerSignatureTests() =>
        fixtures = Path.Combine(services.Environment.GetBaseDirectory(), "Fixtures", "CodeSignature");

    private bool Authenticode => services.Platform.FileSystem.CodeSignatureFormat == CodeSignatureFormat.Authenticode;

    /// <summary>The rid whose entry the signed release carries.</summary>
    private string Rid => Authenticode ? "win-x64" : "osx-arm64";

    private string OtherRid => Authenticode ? "win-arm64" : "osx-x64";

    [Fact]
    public async Task AReleaseSignedByAPinnedCertificate_Installs()
    {
        string root = services.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-signed-");
        var installer = new VersionInstaller(services, root, Rid, Trust(MacFixtureSigner, WindowsFixtureSigner));

        InstallOutcome outcome = await installer.InstallAsync(SignedRelease(), CancellationToken.None);

        outcome.Result.Should().Be(InstallResult.Installed);
    }

    [Fact]
    public async Task AnUnsignedFileSharingTheEntrysPrefix_IsNotCodeSignatureChecked()
    {
        // The Rust entry's debug file (denice-v<version>-win-x64.pdb) was shipped once and refused as "not signed by
        // Denice": only the entry program itself carries a code signature; the checksums and cosign cover the rest.
        string root = services.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-debugfile-");
        var installer = new VersionInstaller(services, root, Rid, Trust(MacFixtureSigner, WindowsFixtureSigner));

        InstallOutcome outcome = await installer.InstallAsync(
            SignedRelease($"denice-v{Version}-{Rid}.pdb", "debug symbols"u8.ToArray()), CancellationToken.None);

        outcome.Result.Should().Be(InstallResult.Installed);
    }

    [Fact]
    public async Task AReleaseWithoutTheEntryOfItsRid_InstallsNothingWhereTheOsSigns()
    {
        // The signed entry is the one named for this install's rid; an entry named for another rid does not stand in.
        string root = services.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-otherrid-");
        var installer = new VersionInstaller(services, root, OtherRid, Trust(MacFixtureSigner, WindowsFixtureSigner));

        Func<Task> act = () => installer.InstallAsync(SignedRelease(), CancellationToken.None);

        if (services.Platform.FileSystem.CodeSignatureFormat == CodeSignatureFormat.None)
        {
            await act.Should().NotThrowAsync();
        }
        else
        {
            (await act.Should().ThrowAsync<InvalidDataException>())
                .WithMessage($"*'{LoaderProtocol.EntryFileName(Version, OtherRid)}' is not signed by Denice*");
        }
    }

    [Fact]
    public async Task AReleaseSignedByAnUnpinnedCertificate_InstallsNothingWhereTheOsSigns()
    {
        string root = services.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-unpinned-");
        var installer = new VersionInstaller(services, root, Rid, Trust(NotPinned));

        Func<Task> act = () => installer.InstallAsync(SignedRelease(), CancellationToken.None);

        if (services.Platform.FileSystem.CodeSignatureFormat == CodeSignatureFormat.None)
        {
            // No OS code signature on this platform: the checksum and cosign checks decide alone.
            await act.Should().NotThrowAsync();
        }
        else
        {
            (await act.Should().ThrowAsync<InvalidDataException>()).WithMessage("*is not signed by Denice*");
            services.FileSystem.GetDirectoryReader()
                .DirectoryExists(Path.Combine(root, LoaderProtocol.VersionsDirectory, Version)).Should().BeFalse();
        }
    }

    private static ReleaseTrust Trust(params string[] pins) =>
        new((_, _, _) => Task.FromResult<string?>(null), pins);

    private static string Sums(Dictionary<string, byte[]> files) => string.Concat(
        files.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{Convert.ToHexStringLower(SHA256.HashData(pair.Value))}  {pair.Key}\n"));

    /// <summary>The files CI signs, made of the fixture signed by the fixture certificate for this platform's format.</summary>
    private Dictionary<string, byte[]> SignedFiles()
    {
        IFileReader reader = services.FileSystem.GetFileReader();
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (Authenticode)
        {
            byte[] signed = reader.ReadAllBytes(Path.Combine(fixtures, "windows", "signed.dll"));
            files["denice.exe"] = signed;
            files[LoaderProtocol.EntryFileName(Version, Rid)] = signed;
            files["Denice.Runtime.dll"] = signed;
        }
        else
        {
            files[LoaderProtocol.EntryFileName(Version, Rid)] = reader.ReadAllBytes(Path.Combine(fixtures, "macos", "signed-macho"));
            string app = Path.Combine(fixtures, "macos", "Signed.app");
            foreach (string relative in FilesUnder(app, string.Empty))
            {
                files["Denice.app/" + relative] = reader.ReadAllBytes(Path.Combine(app, relative));
            }
        }

        return files;
    }

    private IEnumerable<string> FilesUnder(string directory, string prefix)
    {
        foreach (DirectoryChild child in services.FileSystem.GetDirectoryReader().EnumerateChildren(directory))
        {
            string relative = prefix + Path.GetFileName(child.FullPath);
            if (child.IsDirectory)
            {
                foreach (string nested in FilesUnder(child.FullPath, relative + "/"))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return relative;
            }
        }
    }

    private byte[] SignedRelease() => SignedRelease(null, []);

    /// <summary>The signed release, with one more (unsigned) file in the version folder when a name is given.</summary>
    private byte[] SignedRelease(string? extraName, byte[] extraContent)
    {
        Dictionary<string, byte[]> files = SignedFiles();
        if (extraName is not null)
        {
            files[extraName] = extraContent;
        }

        var entries = files.ToDictionary(pair => $"versions/{Version}/{pair.Key}", pair => pair.Value, StringComparer.Ordinal);
        entries[$"versions/{Version}/SHA256SUMS"] = System.Text.Encoding.UTF8.GetBytes(Sums(files));
        entries[$"versions/{Version}/SHA256SUMS.cosign.bundle"] = "{}"u8.ToArray();
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (KeyValuePair<string, byte[]> pair in entries)
            {
                using Stream content = archive.CreateEntry(pair.Key).Open();
                content.Write(pair.Value);
            }
        }

        return stream.ToArray();
    }
}
