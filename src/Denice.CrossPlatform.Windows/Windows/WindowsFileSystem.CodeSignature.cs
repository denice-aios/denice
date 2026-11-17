// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform.Windows;

/// <summary>
/// The code-signature fragment of the Windows file system: the Authenticode signature of a PE file, checked with
/// <c>WinVerifyTrust</c> (generic verify v2, no UI, no revocation, no network). It lives on the class that implements
/// <see cref="IPlatformFileSystem"/> because that class is the one per-OS owner of native interop (DN0101). A zero
/// result, or <c>CERT_E_UNTRUSTEDROOT</c>/<c>CERT_E_CHAINING</c> — which <c>WinVerifyTrust</c> reports only after the
/// file digest and the signature itself have verified — means the signature is intact; the signer's leaf certificate
/// is then read from the verification state and judged against the pinned fingerprints by the shared
/// <see cref="CodeSignatureVerdict"/>. So Denice's self-signed certificate, which never chains to a trusted root,
/// passes when pinned, and a bad digest, a missing signature or a different signer fails.
/// </summary>
internal sealed partial class WindowsFileSystem
{
    // INVALID_HANDLE_VALUE as the WinVerifyTrust window: there is no interactive user.
    private static readonly IntPtr NoInteractiveUser = new(-1);

    /// <inheritdoc />
    public CodeSignatureFormat CodeSignatureFormat => CodeSignatureFormat.Authenticode;

    /// <inheritdoc />
    public CodeSignatureCheck CheckCodeSignature(string path, IReadOnlyCollection<string> pinnedSignerSha256)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        CodeSignatureVerdict.RequireWellFormed(pinnedSignerSha256);

        int fileInfoSize = Marshal.SizeOf<WintrustFileInfo>();
        IntPtr filePath = Marshal.StringToHGlobalUni(path);
        IntPtr file = Marshal.AllocHGlobal(fileInfoSize);
        try
        {
            Marshal.StructureToPtr(
                new WintrustFileInfo { StructSize = (uint)fileInfoSize, FilePath = filePath }, file, fDeleteOld: false);
            var data = new WintrustData
            {
                StructSize = (uint)Marshal.SizeOf<WintrustData>(),
                UiChoice = WinTrustNativeMethods.UiNone,
                RevocationChecks = WinTrustNativeMethods.RevokeNone,
                UnionChoice = WinTrustNativeMethods.ChoiceFile,
                File = file,
                StateAction = WinTrustNativeMethods.StateActionVerify,
                ProviderFlags = WinTrustNativeMethods.OfflineProviderFlags,
            };
            Guid action = WinTrustNativeMethods.GenericVerifyV2;
            int result = WinTrustNativeMethods.WinVerifyTrust(NoInteractiveUser, ref action, ref data);
            int lastError = Marshal.GetLastPInvokeError();
            try
            {
                return JudgeAuthenticode(path, result, lastError, data.StateData, pinnedSignerSha256);
            }
            finally
            {
                data.StateAction = WinTrustNativeMethods.StateActionClose;
                _ = WinTrustNativeMethods.WinVerifyTrust(NoInteractiveUser, ref action, ref data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(file);
            Marshal.FreeHGlobal(filePath);
        }
    }

    // The documented reading of a WinVerifyTrust result: TRUST_E_NOSIGNATURE means "not signed" only when the last
    // error agrees (no signature, an unsignable form, or no provider for it) — otherwise, for example for a missing
    // file, the file could not be checked at all.
    private static CodeSignatureCheck JudgeAuthenticode(
        string path, int result, int lastError, IntPtr state, IReadOnlyCollection<string> pinned)
    {
        if (result == WinTrustNativeMethods.NoSignature
            && lastError is WinTrustNativeMethods.NoSignature
                or WinTrustNativeMethods.SubjectFormUnknown
                or WinTrustNativeMethods.ProviderUnknown)
        {
            return CodeSignatureVerdict.NotSigned(path);
        }

        if (result is not (0 or WinTrustNativeMethods.UntrustedRoot or WinTrustNativeMethods.Chaining))
        {
            string code = result.ToString("X8", CultureInfo.InvariantCulture);
            string error = lastError.ToString("X8", CultureInfo.InvariantCulture);
            return CodeSignatureVerdict.Invalid(
                path, $"its Authenticode signature is not intact (WinVerifyTrust 0x{code}, last error 0x{error})");
        }

        byte[]? leaf = LeafCertificate(state);
        return leaf is null
            ? CodeSignatureVerdict.Invalid(path, "its signing certificate could not be read")
            : CodeSignatureVerdict.Signed(leaf, pinned);
    }

    // The DER bytes of the primary signer's leaf certificate in the verification state, or null when the state holds
    // none. CRYPT_PROVIDER_CERT starts { DWORD cbStruct; PCCERT_CONTEXT pCert; … } and CERT_CONTEXT is
    // { DWORD dwCertEncodingType; BYTE* pbCertEncoded; DWORD cbCertEncoded; … }; each pointer-sized field is aligned to
    // the pointer size, so pCert and pbCertEncoded sit at offset IntPtr.Size and cbCertEncoded at 2 * IntPtr.Size.
    private static byte[]? LeafCertificate(IntPtr state)
    {
        IntPtr providerData = WinTrustNativeMethods.WTHelperProvDataFromStateData(state);
        IntPtr signer = providerData == IntPtr.Zero
            ? IntPtr.Zero
            : WinTrustNativeMethods.WTHelperGetProvSignerFromChain(providerData, 0, 0, 0);
        IntPtr providerCertificate = signer == IntPtr.Zero
            ? IntPtr.Zero
            : WinTrustNativeMethods.WTHelperGetProvCertFromChain(signer, 0);
        IntPtr context = providerCertificate == IntPtr.Zero
            ? IntPtr.Zero
            : Marshal.ReadIntPtr(providerCertificate, IntPtr.Size);
        if (context == IntPtr.Zero)
        {
            return null;
        }

        byte[] der = new byte[Marshal.ReadInt32(context, 2 * IntPtr.Size)];
        Marshal.Copy(Marshal.ReadIntPtr(context, IntPtr.Size), der, 0, der.Length);
        return der;
    }
}
