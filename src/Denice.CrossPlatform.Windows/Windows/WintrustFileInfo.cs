// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Windows;

/// <summary>
/// The blittable <c>WINTRUST_FILE_INFO</c>: names the file <c>WinVerifyTrust</c> checks. Field order and types mirror
/// the Windows SDK header (<c>wintrust.h</c>) exactly; sequential layout reproduces its native alignment.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WintrustFileInfo
{
    /// <summary>cbStruct: the size of this structure in bytes.</summary>
    public uint StructSize;

    /// <summary>pcwszFilePath: the NUL-terminated UTF-16 path of the file to check.</summary>
    public IntPtr FilePath;

    /// <summary>hFile: an open handle to the file, or <see cref="IntPtr.Zero"/> to open it by path.</summary>
    public IntPtr FileHandle;

    /// <summary>pgKnownSubject: the subject type, or <see cref="IntPtr.Zero"/> to detect it.</summary>
    public IntPtr KnownSubject;
}
