// Copyright (c) Denice.ai. All rights reserved.

using System.IO;
using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform.Posix;

/// <summary>
/// The macOS-only fragment of the POSIX file system (os-specific-file-naming-convention): the per-user data folder
/// (<c>~/Library/Application Support/Denice/</c>) and the native case-sensitivity query the shared source declares as a
/// partial method. It is compiled ONLY into the macOS project
/// — it is never link-shared into the Linux build (which would compile the macOS <c>pathconf</c> query in as
/// permanently-dead, uncoverable code and drop the leg below the coverage floor). Because this file runs only on
/// macOS, the query needs no OS branch; the macOS project selecting it is what makes it macOS-specific. The
/// <c>pathconf</c> P/Invoke it calls is the macOS-only fragment of <c>PosixNativeMethods</c> in the sibling
/// <c>PosixNativeMethods.MacOS.cs</c> (one type per file, per the repository's SA1402/MA0048 house rules).
/// </summary>
internal sealed partial class PosixFileSystem
{
    // The macOS pathconf name _PC_CASE_SENSITIVE, verified against the SDK header
    // (/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/sys/unistd.h: "#define _PC_CASE_SENSITIVE 11").
    // It is a macOS-specific constant; Linux has no such query, which is why this const and its P/Invoke live in the
    // macOS-only fragments rather than the link-shared source.
    private const int PosixCaseSensitiveName = 11;

    // open(2) O_CLOEXEC and errno EWOULDBLOCK on macOS (sys/fcntl.h: 0x01000000; sys/errno.h: 35).
    private const int OpenCloseOnExec = 0x01000000;
    private const int WouldBlockErrno = 35;

    /// <inheritdoc />
    // macOS keeps per-user application data under ~/Library/Application Support (the owner's choice for Denice's
    // settings folder), so the base is the home directory.
    public UserDataFolder UserDataFolder { get; } =
        new(EnvironmentVariable: null, RelativePath: Path.Combine("Library", "Application Support", "Denice"));

    // The macOS body of the native case-sensitivity query the shared PosixFileSystem declares. macOS answers directly
    // with pathconf(path, _PC_CASE_SENSITIVE): 1 (case-sensitive) or 0 (not). When pathconf cannot answer (returns -1)
    // this reports failure so the shared read-only probe takes over.
    private static partial bool TryQueryNativeCaseSensitivity(string path, out bool caseSensitive)
    {
        long native = PosixNativeMethods.PathConf(path, PosixCaseSensitiveName);
        if (native >= 0)
        {
            caseSensitive = native == 1;
            return true;
        }

        caseSensitive = false;
        return false;
    }
}
