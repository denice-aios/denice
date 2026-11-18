// Copyright (c) Denice.ai. All rights reserved.

using System;

namespace Denice.Updates;

/// <summary>
/// The contract between the Rust loader, the Rust entry and Denice Runtime. The single definition is
/// <c>eng/loader-protocol.json</c>; a test proves these values equal it, as the Rust side's test does.
/// </summary>
public static class LoaderProtocol
{
    /// <summary>Exit code asking the loader to restart into the newer version that is now installed.</summary>
    public const int ExitRestartIntoNewerVersion = 75;

    /// <summary>Exit code the entry returns after swapping in a newer loader.</summary>
    public const int ExitLoaderReplaced = 76;

    /// <summary>Environment variable holding the install root.</summary>
    public const string EnvInstallRoot = "DENICE_INSTALL_ROOT";

    /// <summary>Environment variable holding the running loader's path.</summary>
    public const string EnvLoaderPath = "DENICE_LOADER_PATH";

    /// <summary>Environment variable holding the running loader's version.</summary>
    public const string EnvLoaderVersion = "DENICE_LOADER_VERSION";

    /// <summary>Environment variable holding this version's folder.</summary>
    public const string EnvVersionDirectory = "DENICE_VERSION_DIR";

    /// <summary>The folder under the install root holding one folder per version.</summary>
    public const string VersionsDirectory = "versions";

    /// <summary>Suffix of a version folder still being downloaded and checked.</summary>
    public const string PartialSuffix = ".partial";

    /// <summary>Marker written in the version folder once Denice Runtime has started.</summary>
    public const string StartedMarker = ".started";

    /// <summary>Marker the loader writes in a version folder that failed to start.</summary>
    public const string BadMarker = ".bad";

    /// <summary>
    /// Lock file in the versions folder, held exclusively by whichever Denice is renaming a version into place, swapping
    /// the loader, or removing the replaced loader (pkg-db4b63e03ad4).
    /// </summary>
    public const string InstallLock = ".lock";

    /// <summary>Suffix of the new loader while it is unpacked.</summary>
    public const string LoaderStagingSuffix = ".ver";

    /// <summary>Suffix the replaced loader is renamed to.</summary>
    public const string LoaderBackupSuffix = ".old";

    /// <summary>The Denice Runtime assembly file the entry runs.</summary>
    public const string RuntimeAssembly = "Denice.Runtime.dll";

    /// <summary>The start of the versioned entry's file name; <see cref="EntryFileName"/> builds the whole name.</summary>
    public const string EntryPrefix = "denice-v";

    private const string WindowsRidPrefix = "win-";
    private const string WindowsExecutableSuffix = ".exe";

    /// <summary>
    /// The versioned entry's file name, <c>denice-v&lt;version&gt;-&lt;rid&gt;</c> with <c>.exe</c> for a Windows rid: the
    /// name the Rust build gives the entry (<c>platform::entry_file_name</c>) and the one file of a version the loader runs.
    /// </summary>
    /// <param name="version">The version the entry belongs to.</param>
    /// <param name="rid">The .NET runtime identifier of its platform, e.g. <c>win-x64</c>.</param>
    /// <returns>The entry's file name.</returns>
    public static string EntryFileName(string version, string rid)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);
        ArgumentException.ThrowIfNullOrEmpty(rid);
        string suffix = rid.StartsWith(WindowsRidPrefix, StringComparison.Ordinal) ? WindowsExecutableSuffix : string.Empty;
        return EntryPrefix + version + "-" + rid + suffix;
    }

    /// <summary>
    /// The runtime identifier a versioned entry's file name carries, or null when <paramref name="fileName"/> is not
    /// exactly <see cref="EntryFileName"/> of <paramref name="version"/> for some rid (a rid has no dot, so a debug file
    /// such as <c>denice-v&lt;version&gt;-win-x64.pdb</c> is not an entry).
    /// </summary>
    /// <param name="fileName">A file name in the version's folder.</param>
    /// <param name="version">The version whose entry is sought.</param>
    /// <returns>The rid, or null.</returns>
    public static string? EntryRid(string fileName, string version)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentException.ThrowIfNullOrEmpty(version);
        string prefix = EntryPrefix + version + "-";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal) || fileName.Length == prefix.Length)
        {
            return null;
        }

        string rid = fileName[prefix.Length..].Split('.')[0];
        return rid.Length > 0 && string.Equals(EntryFileName(version, rid), fileName, StringComparison.Ordinal) ? rid : null;
    }
}
