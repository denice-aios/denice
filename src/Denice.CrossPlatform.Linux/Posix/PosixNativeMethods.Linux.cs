// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Denice.CrossPlatform.Posix;

/// <summary>Linux file-descriptor creation used by the existing filesystem owner.</summary>
internal static partial class PosixNativeMethods
{
    /// <summary>Opens a file with an explicit creation mode.</summary>
    /// <param name="path">The output path.</param>
    /// <param name="flags">The Linux open flags.</param>
    /// <param name="mode">The permission bits filtered by the process umask.</param>
    /// <returns>The owned descriptor, or an invalid handle with errno set.</returns>
    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial SafeFileHandle OpenCreate(string path, int flags, uint mode);

    /// <summary>Moves the file offset of the opened descriptor.</summary>
    /// <param name="handle">The opened file.</param>
    /// <param name="offset">The requested offset.</param>
    /// <param name="origin">The SEEK origin.</param>
    /// <returns>The new position, or minus one with errno set.</returns>
    [LibraryImport("libc", EntryPoint = "lseek", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial long Seek(SafeFileHandle handle, long offset, int origin);
}
