// Copyright (c) Denice.ai. All rights reserved.

using System.IO;
using System.Runtime.InteropServices;
using Denice.Abstractions.Contracts;
using Microsoft.Win32.SafeHandles;

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
    // open(2) O_CLOEXEC and errno EWOULDBLOCK on Linux (asm-generic/fcntl.h: 02000000 octal; asm-generic/errno-base.h:
    // EAGAIN 11).
    private const int OpenCloseOnExec = 0x80000;
    private const int WouldBlockErrno = 11;

    /// <inheritdoc />
    // The owner's choice for Linux is exactly ~/.config/denice/ (the base is the home directory); XDG_CONFIG_HOME is
    // deliberately not consulted.
    public UserDataFolder UserDataFolder { get; } =
        new(EnvironmentVariable: null, RelativePath: Path.Combine(".config", "denice"));

    /// <summary>Exclusively creates a seekable regular recording file without replacing an existing entry.</summary>
    /// <param name="path">The new output path.</param>
    /// <returns>The owned descriptor, retained until the media pipeline releases it.</returns>
    internal static SafeFileHandle CreateRecordingFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        const int create = 0x40;
        const int exclusive = 0x80;

        // O_CREAT | O_EXCL creates a regular file and rejects existing entries,
        // including final-component symbolic links. No check-then-open race.
        SafeFileHandle handle = PosixNativeMethods.OpenCreate(path, OpenReadWrite | create | exclusive | OpenCloseOnExec, 0x1B6);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException($"Cannot exclusively create recording '{path}' (errno {error}).");
        }

        try
        {
            // Validate the opened descriptor itself, not its pathname. mp4mux
            // needs a seekable destination to rewrite its final headers.
            if (PosixNativeMethods.Seek(handle, 0, 0) != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new IOException($"The recording destination is not seekable (errno {error}).");
            }

            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    // Linux has no native case-sensitivity query (the macOS _PC_CASE_SENSITIVE pathconf name has no Linux meaning), so
    // this reports failure and the shared PosixFileSystem falls back to the OS-uniform read-only probe.
    private static partial bool TryQueryNativeCaseSensitivity(string path, out bool caseSensitive)
    {
        caseSensitive = false;
        return false;
    }
}
