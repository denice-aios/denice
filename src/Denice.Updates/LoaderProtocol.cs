// Copyright (c) Denice.ai. All rights reserved.

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

    /// <summary>Suffix of the new loader while it is unpacked.</summary>
    public const string LoaderStagingSuffix = ".ver";

    /// <summary>Suffix the replaced loader is renamed to.</summary>
    public const string LoaderBackupSuffix = ".old";

    /// <summary>The Denice Runtime assembly file the entry runs.</summary>
    public const string RuntimeAssembly = "Denice.Runtime.dll";
}
