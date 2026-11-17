// Copyright (c) Denice.ai. All rights reserved.

using System.IO;
using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform.Posix;

/// <summary>
/// The Linux-only fragment of the POSIX file system (os-specific-file-naming-convention): the per-user data folder
/// (<c>~/.config/denice/</c>) and the native case-sensitivity query the shared source declares as a partial method. Linux has no <c>pathconf</c>
/// case-sensitivity name, so there is no native query to run — this body reports failure and defers to the shared
/// read-only probe, which is the OS-uniform fallback the shared source already calls. This is genuinely
/// Linux-specific behavior (no native query), not a copy of the macOS <c>pathconf</c> code, so it is not a DRY
/// violation of it; it is compiled only into the Linux project, never link-shared into the macOS build.
/// </summary>
internal sealed partial class PosixFileSystem
{
    /// <inheritdoc />
    // The owner's choice for Linux is exactly ~/.config/denice/ (the base is the home directory); XDG_CONFIG_HOME is
    // deliberately not consulted.
    public UserDataFolder UserDataFolder { get; } =
        new(EnvironmentVariable: null, RelativePath: Path.Combine(".config", "denice"));

    // Linux has no native case-sensitivity query (the macOS _PC_CASE_SENSITIVE pathconf name has no Linux meaning), so
    // this reports failure and the shared PosixFileSystem falls back to the OS-uniform read-only probe.
    private static partial bool TryQueryNativeCaseSensitivity(string path, out bool caseSensitive)
    {
        caseSensitive = false;
        return false;
    }
}
