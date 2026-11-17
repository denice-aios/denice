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

namespace Denice.Updates;

/// <summary>
/// Installs a downloaded Denice release into the install root, the way pkg-a0206b25f619 decided: the version's files are
/// unpacked into <c>versions/&lt;version&gt;.partial</c>, every file is checked against the release's
/// <c>SHA256SUMS</c>, and only then is the folder renamed to <c>versions/&lt;version&gt;</c> in one step, so the loader
/// never sees half a version. Tim, 14 November 2026: "The .NET DLL performs the downloading".
/// </summary>
public sealed class VersionInstaller
{
    private const string ChecksumsFile = "SHA256SUMS";
    private const string ChecksumsSignatureFile = "SHA256SUMS.cosign.bundle";
    private const string VersionsPrefix = LoaderProtocol.VersionsDirectory + "/";

    private readonly ISystemServices _services;
    private readonly string _installRoot;
    private readonly string _rid;
    private readonly ReleaseTrust _trust;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionInstaller"/> class for one install root.
    /// </summary>
    /// <param name="services">The owned services every file operation goes through.</param>
    /// <param name="installRoot">The install root (the folder holding the loader and <c>versions</c>).</param>
    /// <param name="rid">The runtime identifier of the platform the install is for (e.g. <c>win-x64</c>); it names the
    /// versioned entry (<see cref="LoaderProtocol.EntryFileName"/>), the program whose code signature is checked.</param>
    public VersionInstaller(ISystemServices services, string installRoot, string rid)
        : this(services, installRoot, rid, ReleaseTrust.Denice)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionInstaller"/> class that trusts the given signers (tests pin
    /// their fixture signers; production always trusts <see cref="ReleaseTrust.Denice"/>).
    /// </summary>
    /// <param name="services">The owned services every file operation goes through.</param>
    /// <param name="installRoot">The install root.</param>
    /// <param name="rid">The runtime identifier of the platform the install is for.</param>
    /// <param name="trust">Who a release must come from.</param>
    internal VersionInstaller(ISystemServices services, string installRoot, string rid, ReleaseTrust trust)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(installRoot);
        ArgumentException.ThrowIfNullOrEmpty(rid);
        ArgumentNullException.ThrowIfNull(trust);
        _services = services;
        _installRoot = installRoot;
        _rid = rid;
        _trust = trust;
    }

    /// <summary>
    /// Installs the version a release file carries.
    /// </summary>
    /// <param name="releaseZip">The release file's bytes (<c>denice-v&lt;version&gt;-&lt;rid&gt;.zip</c>).</param>
    /// <param name="ct">Cancels the install; a cancelled install leaves only a <c>.partial</c> folder, never a version.</param>
    /// <returns>The outcome and the version it concerns.</returns>
    /// <exception cref="InvalidDataException">The release file is not a valid Denice release; nothing was installed.</exception>
    public async Task<InstallOutcome> InstallAsync(ReadOnlyMemory<byte> releaseZip, CancellationToken ct)
    {
        using var archive = new ZipArchive(new MemoryStream(releaseZip.ToArray(), writable: false), ZipArchiveMode.Read);
        string version = SingleVersionIn(archive);
        string versions = Path.Combine(_installRoot, LoaderProtocol.VersionsDirectory);
        string final = Path.Combine(versions, version);
        string partial = final + LoaderProtocol.PartialSuffix;
        IDirectoryEnumerator directories = _services.FileSystem.GetDirectoryReader();
        IDirectoryWriter directoryWriter = _services.FileSystem.GetDirectoryWriter();

        if (directories.DirectoryExists(final))
        {
            return new InstallOutcome(InstallResult.AlreadyInstalled, version);
        }

        if (directories.DirectoryExists(partial))
        {
            directoryWriter.DeleteDirectory(partial, recursive: true);
        }

        directoryWriter.CreateDirectory(partial);
        await UnpackAsync(archive, version, partial, ct).ConfigureAwait(false);
        string? problem = ChecksumProblem(partial)
            ?? await CosignProblemAsync(partial, ct).ConfigureAwait(false)
            ?? CodeSignatureProblem(partial, version);
        if (problem is not null)
        {
            directoryWriter.DeleteDirectory(partial, recursive: true);
            throw new InvalidDataException($"Denice {version} failed its download checks and was not installed: {problem}");
        }

        directoryWriter.MoveDirectory(partial, final);
        return new InstallOutcome(InstallResult.Installed, version);
    }

    private static string SingleVersionIn(ZipArchive archive)
    {
        string[] found = archive.Entries
            .Select(entry => entry.FullName)
            .Where(name => name.StartsWith(VersionsPrefix, StringComparison.Ordinal))
            .Select(name => name[VersionsPrefix.Length..].Split('/')[0])
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (found.Length != 1 || !IsSafeSegment(found[0]))
        {
            throw new InvalidDataException(
                $"A Denice release holds exactly one versions/<version>/ folder; this one holds {found.Length}.");
        }

        return found[0];
    }

    private static bool IsSafeSegment(string segment) =>
        segment.Length > 0 && !string.Equals(segment, ".", StringComparison.Ordinal)
        && !string.Equals(segment, "..", StringComparison.Ordinal) && segment.IndexOfAny(['\\', ':']) < 0;

    private async Task UnpackAsync(ZipArchive archive, string version, string partial, CancellationToken ct)
    {
        string prefix = VersionsPrefix + version + "/";
        IFileWriter files = _services.FileSystem.GetFileWriter();
        IDirectoryWriter directoryWriter = _services.FileSystem.GetDirectoryWriter();
        IPlatformFileSystem platform = _services.Platform.FileSystem;
        foreach (ZipArchiveEntry entry in archive.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)))
        {
            string relative = entry.FullName[prefix.Length..];
            if (relative.Length == 0 || relative.EndsWith('/'))
            {
                continue;
            }

            string[] segments = relative.Split('/');
            if (!segments.All(IsSafeSegment))
            {
                throw new InvalidDataException($"The release entry '{entry.FullName}' leaves its version folder.");
            }

            string target = Path.Combine([partial, .. segments]);
            directoryWriter.CreateDirectory(Path.GetDirectoryName(target)!);
            using var content = new MemoryStream();
            Stream source = await entry.OpenAsync(ct).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                await source.CopyToAsync(content, ct).ConfigureAwait(false);
            }

            await files.WriteAllBytesAsync(target, content.ToArray(), ct).ConfigureAwait(false);
            const int unixExecuteBits = 0b001_001_001;
            if (platform.NeedsExecutableFlag() && ((entry.ExternalAttributes >> 16) & unixExecuteBits) != 0)
            {
                platform.MakeExecutable(target);
            }
        }
    }

    /// <summary>The cosign signature over <c>SHA256SUMS</c> must be Denice's release workflow's.</summary>
    private async Task<string?> CosignProblemAsync(string partial, CancellationToken ct)
    {
        IFileReader reader = _services.FileSystem.GetFileReader();
        string bundlePath = Path.Combine(partial, ChecksumsSignatureFile);
        if (!reader.Exists(bundlePath))
        {
            return "it has no SHA256SUMS.cosign.bundle";
        }

        byte[] checksums = reader.ReadAllBytes(Path.Combine(partial, ChecksumsFile));
        return await _trust.CosignProblem(checksums, reader.ReadAllText(bundlePath), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Denice's own executables must carry an OS code signature by a pinned Denice certificate: on macOS the loader app
    /// and the versioned entry; on Windows the loader, the entry and the Denice.*.dll assemblies (the files CI signs).
    /// The entry is exactly <see cref="LoaderProtocol.EntryFileName"/>: other files sharing its prefix (such as a debug
    /// file) are not programs, carry no code signature, and are covered by the checksum and cosign checks like every
    /// other file. Platforms without OS code signatures (Linux) rely on the checksum and cosign checks.
    /// </summary>
    private string? CodeSignatureProblem(string partial, string version)
    {
        IPlatformFileSystem platform = _services.Platform.FileSystem;
        string entry = Path.Combine(partial, LoaderProtocol.EntryFileName(version, _rid));
        IReadOnlyList<string> signed = platform.CodeSignatureFormat switch
        {
            CodeSignatureFormat.AppleCodeSignature => [Path.Combine(partial, "Denice.app"), entry],
            CodeSignatureFormat.Authenticode => [Path.Combine(partial, "denice.exe"), entry, .. AssembliesIn(partial)],
            _ => [],
        };
        foreach (string path in signed)
        {
            CodeSignatureCheck check = platform.CheckCodeSignature(path, _trust.PinnedSigners);
            if (check.Status != CodeSignatureStatus.Valid)
            {
                return $"'{Path.GetFileName(path)}' is not signed by Denice ({check.Status}: {check.Detail})";
            }
        }

        return null;
    }

    private IEnumerable<string> AssembliesIn(string partial) => TopLevelFiles(partial)
        .Where(path => Path.GetFileName(path).StartsWith("Denice.", StringComparison.Ordinal)
            && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

    private IEnumerable<string> TopLevelFiles(string partial) => _services.FileSystem.GetDirectoryReader()
        .EnumerateChildren(partial).Where(child => !child.IsDirectory).Select(child => child.FullPath);

    private string? ChecksumProblem(string partial)
    {
        IFileReader reader = _services.FileSystem.GetFileReader();
        string sumsPath = Path.Combine(partial, ChecksumsFile);
        if (!reader.Exists(sumsPath))
        {
            return "it has no SHA256SUMS";
        }

        var listed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in reader.ReadAllText(sumsPath).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int gap = line.IndexOf("  ", StringComparison.Ordinal);
            if (gap != 64)
            {
                return $"SHA256SUMS has a malformed line: '{line}'";
            }

            listed[line[(gap + 2)..].TrimEnd('\r')] = line[..gap];
        }

        List<string> present = FilesUnder(partial, string.Empty)
            .Where(name => !string.Equals(name, ChecksumsFile, StringComparison.Ordinal)
                && !string.Equals(name, ChecksumsSignatureFile, StringComparison.Ordinal))
            .ToList();
        foreach (string name in present)
        {
            if (!listed.TryGetValue(name, out string? expected))
            {
                return $"'{name}' is not listed in SHA256SUMS";
            }

            string actual = Convert.ToHexStringLower(SHA256.HashData(reader.ReadAllBytes(Path.Combine(partial, name))));
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                return $"'{name}' does not match its checksum";
            }
        }

        string? missing = listed.Keys.FirstOrDefault(name => !present.Contains(name, StringComparer.Ordinal));
        return missing is null ? null : $"'{missing}' is listed in SHA256SUMS but missing";
    }

    /// <summary>Every file under <paramref name="directory"/>, as a <c>/</c>-separated path relative to it.</summary>
    private IEnumerable<string> FilesUnder(string directory, string relativePrefix)
    {
        foreach (DirectoryChild child in _services.FileSystem.GetDirectoryReader().EnumerateChildren(directory))
        {
            string relative = relativePrefix + Path.GetFileName(child.FullPath);
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
}
