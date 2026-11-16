// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Posix;

/// <summary>
/// The macOS Security and CoreFoundation bindings the Apple code-signature check needs, compiled only into the macOS
/// project (os-specific-file-naming-convention). Every binding is blittable: Core Foundation and Security objects are
/// opaque <see cref="IntPtr"/> references, <c>OSStatus</c> is <see cref="int"/>, <c>SecCSFlags</c> is
/// <see cref="uint"/>, <c>CFIndex</c> is <see cref="long"/> on the LP64 ABI, and the one native <c>Boolean</c> is
/// marshalled as <see cref="UnmanagedType.I1"/> (DN0103). Every object returned by a <c>Create</c>/<c>Copy</c> function
/// is owned by the caller and released with <see cref="CFRelease"/>.
/// </summary>
internal static partial class AppleSecurityNativeMethods
{
    /// <summary>The Security framework binary.</summary>
    internal const string Security = "/System/Library/Frameworks/Security.framework/Security";

    /// <summary>kSecCSDefaultFlags.</summary>
    internal const uint DefaultFlags = 0;

    /// <summary>
    /// kSecCSCheckAllArchitectures | kSecCSCheckNestedCode | kSecCSStrictValidate: validate every architecture of a
    /// universal binary, every nested piece of code in a bundle, and apply the strict validation <c>codesign --verify
    /// --strict</c> applies.
    /// </summary>
    internal const uint StrictValidityFlags = (1u << 0) | (1u << 3) | (1u << 4);

    /// <summary>kSecCSSigningInformation: include the signing certificates in the signing information.</summary>
    internal const uint SigningInformationFlags = 1u << 1;

    /// <summary>errSecSuccess.</summary>
    internal const int Success = 0;

    /// <summary>errSecCSUnsigned: the code object is not signed at all.</summary>
    internal const int CodeNotSigned = -67062;

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary>
    /// Creates a file URL from a UTF-8 file-system path (<c>CFURLCreateFromFileSystemRepresentation</c>).
    /// </summary>
    /// <param name="allocator">The allocator, <see cref="IntPtr.Zero"/> for the default.</param>
    /// <param name="buffer">The UTF-8 path bytes.</param>
    /// <param name="bufferLength">The number of path bytes.</param>
    /// <param name="isDirectory">Whether the path names a directory (an app bundle).</param>
    /// <returns>The URL, or <see cref="IntPtr.Zero"/> on failure; release it with <see cref="CFRelease"/>.</returns>
    [LibraryImport(CoreFoundation, EntryPoint = "CFURLCreateFromFileSystemRepresentation")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial IntPtr CFURLCreateFromFileSystemRepresentation(
        IntPtr allocator,
        byte[] buffer,
        long bufferLength,
        [MarshalAs(UnmanagedType.I1)] bool isDirectory);

    /// <summary>Releases a Core Foundation object (<c>CFRelease</c>).</summary>
    /// <param name="cf">The object to release; must not be <see cref="IntPtr.Zero"/>.</param>
    [LibraryImport(CoreFoundation, EntryPoint = "CFRelease")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial void CFRelease(IntPtr cf);

    /// <summary>Reads a dictionary value without retaining it (<c>CFDictionaryGetValue</c>).</summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <see cref="IntPtr.Zero"/> when absent.</returns>
    [LibraryImport(CoreFoundation, EntryPoint = "CFDictionaryGetValue")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

    /// <summary>Counts an array's elements (<c>CFArrayGetCount</c>).</summary>
    /// <param name="array">The array.</param>
    /// <returns>The element count.</returns>
    [LibraryImport(CoreFoundation, EntryPoint = "CFArrayGetCount")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial long CFArrayGetCount(IntPtr array);

    /// <summary>Reads an array element without retaining it (<c>CFArrayGetValueAtIndex</c>).</summary>
    /// <param name="array">The array.</param>
    /// <param name="index">The element index.</param>
    /// <returns>The element.</returns>
    [LibraryImport(CoreFoundation, EntryPoint = "CFArrayGetValueAtIndex")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);

    /// <summary>Counts a data object's bytes (<c>CFDataGetLength</c>).</summary>
    /// <param name="data">The data object.</param>
    /// <returns>The byte count.</returns>
    [LibraryImport(CoreFoundation, EntryPoint = "CFDataGetLength")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial long CFDataGetLength(IntPtr data);

    /// <summary>Points at a data object's bytes (<c>CFDataGetBytePtr</c>); valid while the object lives.</summary>
    /// <param name="data">The data object.</param>
    /// <returns>The first byte.</returns>
    [LibraryImport(CoreFoundation, EntryPoint = "CFDataGetBytePtr")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial IntPtr CFDataGetBytePtr(IntPtr data);

    /// <summary>Creates a static code object for a file or bundle on disk (<c>SecStaticCodeCreateWithPath</c>).</summary>
    /// <param name="path">The file URL.</param>
    /// <param name="flags">The SecCSFlags (<see cref="DefaultFlags"/>).</param>
    /// <param name="staticCode">Receives the static code object; release it with <see cref="CFRelease"/>.</param>
    /// <returns>The OSStatus; <see cref="Success"/> on success.</returns>
    [LibraryImport(Security, EntryPoint = "SecStaticCodeCreateWithPath")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int SecStaticCodeCreateWithPath(IntPtr path, uint flags, out IntPtr staticCode);

    /// <summary>
    /// Validates a static code object's signature against the file contents (<c>SecStaticCodeCheckValidity</c>). With no
    /// requirement it proves the signature intact; it does not require the signer to chain to a trusted root.
    /// </summary>
    /// <param name="staticCode">The static code object.</param>
    /// <param name="flags">The SecCSFlags (<see cref="StrictValidityFlags"/>).</param>
    /// <param name="requirement">The code requirement, <see cref="IntPtr.Zero"/> for none.</param>
    /// <returns>The OSStatus; <see cref="Success"/> when valid, <see cref="CodeNotSigned"/> when unsigned.</returns>
    [LibraryImport(Security, EntryPoint = "SecStaticCodeCheckValidity")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int SecStaticCodeCheckValidity(IntPtr staticCode, uint flags, IntPtr requirement);

    /// <summary>Reads a code object's signing information (<c>SecCodeCopySigningInformation</c>).</summary>
    /// <param name="code">The static code object.</param>
    /// <param name="flags">The SecCSFlags (<see cref="SigningInformationFlags"/>).</param>
    /// <param name="information">Receives the information dictionary; release it with <see cref="CFRelease"/>.</param>
    /// <returns>The OSStatus; <see cref="Success"/> on success.</returns>
    [LibraryImport(Security, EntryPoint = "SecCodeCopySigningInformation")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int SecCodeCopySigningInformation(IntPtr code, uint flags, out IntPtr information);

    /// <summary>Copies a certificate's DER encoding (<c>SecCertificateCopyData</c>).</summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The DER data; release it with <see cref="CFRelease"/>.</returns>
    [LibraryImport(Security, EntryPoint = "SecCertificateCopyData")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial IntPtr SecCertificateCopyData(IntPtr certificate);
}
