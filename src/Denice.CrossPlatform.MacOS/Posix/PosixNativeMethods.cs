// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Denice.CrossPlatform.Posix;

/// <summary>
/// The POSIX <c>rename(2)</c> binding. Atomically replacing an existing symlink with another — which is how a
/// version pointer is flipped without a window where it is absent — is a single <c>rename</c>; the managed
/// <c>File.Move</c>/<c>Directory.Move</c> APIs do not replace a directory symlink atomically. The same libc
/// <c>rename</c> serves macOS and Linux, so this source is authored once here and linked into the Linux impl
/// unchanged. Native interop lives ONLY in per-OS impl assemblies — never in the contract assembly. This class is
/// <c>partial</c>: the OS-divergent <c>pathconf</c> binding lives in the macOS-only fragment
/// (<c>PosixFileSystem.MacOS.cs</c>) that only the macOS project compiles, so the Linux build never carries it.
/// </summary>
internal static partial class PosixNativeMethods
{
    /// <summary>
    /// Atomically renames <paramref name="oldPath"/> to <paramref name="newPath"/>, replacing any existing entry
    /// at the destination on the same filesystem.
    /// </summary>
    /// <param name="oldPath">The source path.</param>
    /// <param name="newPath">The destination path.</param>
    /// <returns>Zero on success; a non-zero value with <c>errno</c> set on failure.</returns>
    [LibraryImport("libc", EntryPoint = "rename", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int Rename(string oldPath, string newPath);

    /// <summary>
    /// Opens an existing file (<c>open(2)</c> without <c>O_CREAT</c>, so with no mode argument).
    /// </summary>
    /// <param name="path">The file to open.</param>
    /// <param name="flags">The open flags.</param>
    /// <returns>The descriptor, closed when disposed; invalid with <c>errno</c> set on failure.</returns>
    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial SafeFileHandle Open(string path, int flags);

    /// <summary>
    /// Applies or removes an advisory lock on an open file (<c>flock(2)</c>), the lock Rust's <c>File::lock</c> takes.
    /// </summary>
    /// <param name="handle">The open file.</param>
    /// <param name="operation">The lock operation (for example <c>LOCK_EX | LOCK_NB</c>).</param>
    /// <returns>Zero on success; -1 with <c>errno</c> set on failure.</returns>
    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int Flock(SafeFileHandle handle, int operation);
}
