// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform.Posix;

/// <summary>
/// The macOS-only code-signature fragment of the POSIX file system (os-specific-file-naming-convention): the Apple code
/// signature of an app bundle or Mach-O executable, checked with the Security framework. It lives on the class that
/// implements <see cref="IPlatformFileSystem"/> because that class is the one per-OS owner of native interop (DN0101).
/// <c>SecStaticCodeCheckValidity</c> (strict, all architectures, nested code, no requirement) proves the signature
/// intact — the same check <c>codesign --verify --strict</c> makes, which does not need the signer to chain to a
/// trusted root, so Denice's self-signed certificate passes it. The leaf signing certificate is then read with
/// <c>SecCodeCopySigningInformation</c> and judged against the pinned fingerprints by the shared
/// <see cref="CodeSignatureVerdict"/>. Compiled only into the macOS project; the Linux project compiles its own
/// fragment (Linux has no OS code signature).
/// </summary>
internal sealed partial class PosixFileSystem
{
    // The exported CFStringRef key of the signing-information dictionary entry that holds the certificate chain, leaf
    // first. Read from the Security framework's export table, never re-spelled as its string value.
    private const string CertificatesKeySymbol = "kSecCodeInfoCertificates";

    /// <inheritdoc />
    public CodeSignatureFormat CodeSignatureFormat => CodeSignatureFormat.AppleCodeSignature;

    /// <inheritdoc />
    public CodeSignatureCheck CheckCodeSignature(string path, IReadOnlyCollection<string> pinnedSignerSha256)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        CodeSignatureVerdict.RequireWellFormed(pinnedSignerSha256);

        byte[] utf8 = Encoding.UTF8.GetBytes(path);
        IntPtr url = AppleSecurityNativeMethods.CFURLCreateFromFileSystemRepresentation(
            IntPtr.Zero, utf8, utf8.Length, isDirectory: false);
        if (url == IntPtr.Zero)
        {
            return CodeSignatureVerdict.Invalid(path, "the path could not be turned into a file URL");
        }

        try
        {
            int status = AppleSecurityNativeMethods.SecStaticCodeCreateWithPath(
                url, AppleSecurityNativeMethods.DefaultFlags, out IntPtr staticCode);
            if (status != AppleSecurityNativeMethods.Success)
            {
                return CodeSignatureVerdict.Invalid(path, $"it could not be opened as code (OSStatus {status})");
            }

            try
            {
                return CheckStaticCode(path, staticCode, pinnedSignerSha256);
            }
            finally
            {
                AppleSecurityNativeMethods.CFRelease(staticCode);
            }
        }
        finally
        {
            AppleSecurityNativeMethods.CFRelease(url);
        }
    }

    private static CodeSignatureCheck CheckStaticCode(string path, IntPtr staticCode, IReadOnlyCollection<string> pinned)
    {
        int validity = AppleSecurityNativeMethods.SecStaticCodeCheckValidity(
            staticCode, AppleSecurityNativeMethods.StrictValidityFlags, IntPtr.Zero);
        if (validity == AppleSecurityNativeMethods.CodeNotSigned)
        {
            return CodeSignatureVerdict.NotSigned(path);
        }

        if (validity != AppleSecurityNativeMethods.Success)
        {
            return CodeSignatureVerdict.Invalid(path, $"its code signature is not intact (OSStatus {validity})");
        }

        int copied = AppleSecurityNativeMethods.SecCodeCopySigningInformation(
            staticCode, AppleSecurityNativeMethods.SigningInformationFlags, out IntPtr information);
        if (copied != AppleSecurityNativeMethods.Success)
        {
            return CodeSignatureVerdict.Invalid(path, $"its signing information could not be read (OSStatus {copied})");
        }

        try
        {
            byte[]? leaf = LeafCertificate(information);
            return leaf is null
                ? CodeSignatureVerdict.NoSigningCertificate(path)
                : CodeSignatureVerdict.Signed(leaf, pinned);
        }
        finally
        {
            AppleSecurityNativeMethods.CFRelease(information);
        }
    }

    // The DER bytes of the first (leaf) certificate in the signing information, or null when it lists none (an ad-hoc
    // signature carries no certificate).
    private static byte[]? LeafCertificate(IntPtr information)
    {
        IntPtr certificates = AppleSecurityNativeMethods.CFDictionaryGetValue(information, CertificatesKey());
        if (certificates == IntPtr.Zero || AppleSecurityNativeMethods.CFArrayGetCount(certificates) == 0)
        {
            return null;
        }

        IntPtr data = AppleSecurityNativeMethods.SecCertificateCopyData(
            AppleSecurityNativeMethods.CFArrayGetValueAtIndex(certificates, 0));
        try
        {
            byte[] der = new byte[AppleSecurityNativeMethods.CFDataGetLength(data)];
            Marshal.Copy(AppleSecurityNativeMethods.CFDataGetBytePtr(data), der, 0, der.Length);
            return der;
        }
        finally
        {
            AppleSecurityNativeMethods.CFRelease(data);
        }
    }

    // The kSecCodeInfoCertificates CFStringRef, read through the exported symbol (a pointer to the CFStringRef). The
    // Security framework is part of the OS and already loaded by the calls above, so loading it again only takes a
    // reference; it is never unloaded.
    private static IntPtr CertificatesKey()
    {
        IntPtr security = NativeLibrary.Load(AppleSecurityNativeMethods.Security);
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(security, CertificatesKeySymbol));
    }
}
