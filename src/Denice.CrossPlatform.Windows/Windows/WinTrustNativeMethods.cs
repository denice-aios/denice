// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Windows;

/// <summary>
/// The Windows Authenticode bindings (<c>wintrust.dll</c>) the code-signature check needs: <c>WinVerifyTrust</c> with
/// the generic-verify-v2 action, and the three <c>WTHelper*</c> functions that read the signer's leaf certificate out of
/// the verification state. Every binding is blittable — handles and structure pointers are <see cref="IntPtr"/>, Win32
/// <c>BOOL</c> is <see cref="int"/>, and the two structures are blittable — and binds with a System32-only search path.
/// Native interop lives ONLY in per-OS impl assemblies.
/// </summary>
internal static partial class WinTrustNativeMethods
{
    /// <summary>WTD_UI_NONE: never show UI.</summary>
    internal const uint UiNone = 2;

    /// <summary>WTD_REVOKE_NONE: no revocation checks.</summary>
    internal const uint RevokeNone = 0;

    /// <summary>WTD_CHOICE_FILE: the subject is a file (<see cref="WintrustData.File"/>).</summary>
    internal const uint ChoiceFile = 1;

    /// <summary>WTD_STATEACTION_VERIFY: verify and keep the state for reading the signer.</summary>
    internal const uint StateActionVerify = 1;

    /// <summary>WTD_STATEACTION_CLOSE: free the state a verify kept.</summary>
    internal const uint StateActionClose = 2;

    /// <summary>
    /// WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL: no revocation checks and no network retrieval — the check
    /// is an offline integrity-and-pin check.
    /// </summary>
    internal const uint OfflineProviderFlags = 0x00000010 | 0x00001000;

    /// <summary>TRUST_E_NOSIGNATURE: the file is not signed.</summary>
    internal const int NoSignature = unchecked((int)0x800B0100);

    /// <summary>TRUST_E_SUBJECT_FORM_UNKNOWN: the file is not a form Authenticode can sign.</summary>
    internal const int SubjectFormUnknown = unchecked((int)0x800B0003);

    /// <summary>TRUST_E_PROVIDER_UNKNOWN: no trust provider knows the file's form.</summary>
    internal const int ProviderUnknown = unchecked((int)0x800B0001);

    /// <summary>
    /// CERT_E_UNTRUSTEDROOT: the digest and signature are valid but the chain ends in a root this machine does not trust —
    /// the expected result for Denice's self-signed certificate.
    /// </summary>
    internal const int UntrustedRoot = unchecked((int)0x800B0109);

    /// <summary>
    /// CERT_E_CHAINING: the digest and signature are valid but no chain to a root could be built — also expected for a
    /// self-signed certificate on some systems.
    /// </summary>
    internal const int Chaining = unchecked((int)0x800B010A);

    /// <summary>
    /// The WINTRUST_ACTION_GENERIC_VERIFY_V2 action: verify an Authenticode signature
    /// ({00AAC56B-CD44-11d0-8CC2-00C04FC295EE}).
    /// </summary>
    internal static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    /// <summary>
    /// Verifies a subject's trust (<c>WinVerifyTrust</c>).
    /// </summary>
    /// <param name="window">The window handle; <c>INVALID_HANDLE_VALUE</c> for no interactive user.</param>
    /// <param name="action">The action (<see cref="GenericVerifyV2"/>).</param>
    /// <param name="data">The <see cref="WintrustData"/>.</param>
    /// <returns>Zero when trusted; otherwise an HRESULT such as <see cref="NoSignature"/> or <see cref="UntrustedRoot"/>.
    /// With <see cref="NoSignature"/> the last error tells why: <see cref="NoSignature"/>,
    /// <see cref="SubjectFormUnknown"/> or <see cref="ProviderUnknown"/> mean the file is not signed; anything else (for
    /// example a missing file) means it could not be checked.</returns>
    [LibraryImport("wintrust.dll", EntryPoint = "WinVerifyTrust", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int WinVerifyTrust(IntPtr window, ref Guid action, ref WintrustData data);

    /// <summary>
    /// Reads the provider data from a verification state (<c>WTHelperProvDataFromStateData</c>).
    /// </summary>
    /// <param name="stateData">The <see cref="WintrustData.StateData"/> a verify kept.</param>
    /// <returns>The <c>CRYPT_PROVIDER_DATA</c>, or <see cref="IntPtr.Zero"/>.</returns>
    [LibraryImport("wintrust.dll", EntryPoint = "WTHelperProvDataFromStateData")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

    /// <summary>
    /// Reads a signer from the provider data (<c>WTHelperGetProvSignerFromChain</c>).
    /// </summary>
    /// <param name="providerData">The <c>CRYPT_PROVIDER_DATA</c>.</param>
    /// <param name="signerIndex">The signer index (0 for the primary signer).</param>
    /// <param name="counterSigner">Win32 <c>BOOL</c>: non-zero to read a counter-signer.</param>
    /// <param name="counterSignerIndex">The counter-signer index.</param>
    /// <returns>The <c>CRYPT_PROVIDER_SGNR</c>, or <see cref="IntPtr.Zero"/>.</returns>
    [LibraryImport("wintrust.dll", EntryPoint = "WTHelperGetProvSignerFromChain")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial IntPtr WTHelperGetProvSignerFromChain(
        IntPtr providerData,
        uint signerIndex,
        int counterSigner,
        uint counterSignerIndex);

    /// <summary>
    /// Reads a certificate from a signer's chain (<c>WTHelperGetProvCertFromChain</c>); index 0 is the leaf.
    /// </summary>
    /// <param name="signer">The <c>CRYPT_PROVIDER_SGNR</c>.</param>
    /// <param name="certificateIndex">The chain index (0 for the leaf signing certificate).</param>
    /// <returns>The <c>CRYPT_PROVIDER_CERT</c>, or <see cref="IntPtr.Zero"/>.</returns>
    [LibraryImport("wintrust.dll", EntryPoint = "WTHelperGetProvCertFromChain")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificateIndex);
}
