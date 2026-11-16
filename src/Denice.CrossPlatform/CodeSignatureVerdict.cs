// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform;

/// <summary>
/// The one owner of the OS-uniform half of every per-OS <see cref="IPlatformFileSystem.CheckCodeSignature"/>: validating
/// the pinned signer-certificate fingerprints, computing a certificate's SHA-256 fingerprint, deciding whether an intact
/// signature's leaf certificate is pinned, and wording the results. The per-OS code only proves a signature intact and
/// reads the leaf certificate's DER bytes; the pin decision is made here, once, so macOS and Windows cannot drift apart.
/// </summary>
internal static class CodeSignatureVerdict
{
    private const int Sha256Bytes = 32;

    /// <summary>
    /// Validates the pinned fingerprints, so a malformed pin fails loudly on every OS — including Linux, where nothing
    /// else is checked — instead of silently matching nothing.
    /// </summary>
    /// <param name="pinnedSignerSha256">The pinned fingerprints.</param>
    /// <exception cref="ArgumentException">A fingerprint is not 64 hex digits (with or without <c>:</c>).</exception>
    internal static void RequireWellFormed(IReadOnlyCollection<string> pinnedSignerSha256)
    {
        ArgumentNullException.ThrowIfNull(pinnedSignerSha256);
        foreach (string pinned in pinnedSignerSha256)
        {
            _ = Normalize(pinned);
        }
    }

    /// <summary>
    /// Formats a certificate's SHA-256 fingerprint as upper-case, <c>:</c>-separated hex — the form
    /// <c>openssl x509 -fingerprint -sha256</c> prints and the pinned list uses.
    /// </summary>
    /// <param name="certificateDer">The certificate's DER encoding.</param>
    /// <returns>The fingerprint, for example <c>51:DC:…:D4</c>.</returns>
    internal static string Fingerprint(ReadOnlySpan<byte> certificateDer)
    {
        string hex = Convert.ToHexString(SHA256.HashData(certificateDer));
        return string.Join(':', Enumerable.Range(0, Sha256Bytes).Select(index => hex.Substring(index * 2, 2)));
    }

    /// <summary>
    /// Judges an intact signature by its leaf signing certificate: <see cref="CodeSignatureStatus.Valid"/> when the
    /// certificate's fingerprint is pinned, otherwise <see cref="CodeSignatureStatus.UnpinnedSigner"/>.
    /// </summary>
    /// <param name="leafCertificateDer">The leaf signing certificate's DER encoding.</param>
    /// <param name="pinnedSignerSha256">The pinned fingerprints (already validated).</param>
    /// <returns>The final check result, carrying the signer's fingerprint.</returns>
    internal static CodeSignatureCheck Signed(ReadOnlySpan<byte> leafCertificateDer, IReadOnlyCollection<string> pinnedSignerSha256)
    {
        string fingerprint = Fingerprint(leafCertificateDer);
        string bare = Normalize(fingerprint);
        bool pinned = pinnedSignerSha256.Any(candidate => string.Equals(Normalize(candidate), bare, StringComparison.Ordinal));
        return pinned
            ? new CodeSignatureCheck(CodeSignatureStatus.Valid, fingerprint, "The signature is intact and its signer is pinned.")
            : new CodeSignatureCheck(
                CodeSignatureStatus.UnpinnedSigner,
                fingerprint,
                $"The signature is intact but its signer {fingerprint} is not a pinned Denice certificate.");
    }

    /// <summary>
    /// The result for an intact signature that carries no signing certificate (a macOS ad-hoc signature): it cannot be
    /// Denice's.
    /// </summary>
    /// <param name="path">The checked path.</param>
    /// <returns>An <see cref="CodeSignatureStatus.UnpinnedSigner"/> result with no fingerprint.</returns>
    internal static CodeSignatureCheck NoSigningCertificate(string path) =>
        new(
            CodeSignatureStatus.UnpinnedSigner,
            null,
            $"'{path}' is signed without a signing certificate (ad-hoc), so it is not signed by Denice.");

    /// <summary>
    /// The result for a file with no code signature.
    /// </summary>
    /// <param name="path">The checked path.</param>
    /// <returns>An <see cref="CodeSignatureStatus.NotSigned"/> result.</returns>
    internal static CodeSignatureCheck NotSigned(string path) =>
        new(CodeSignatureStatus.NotSigned, null, $"'{path}' has no code signature.");

    /// <summary>
    /// The result for a signature that is present but broken, or a file that could not be checked.
    /// </summary>
    /// <param name="path">The checked path.</param>
    /// <param name="reason">What went wrong, including the OS status code.</param>
    /// <returns>An <see cref="CodeSignatureStatus.Invalid"/> result.</returns>
    internal static CodeSignatureCheck Invalid(string path, string reason) =>
        new(CodeSignatureStatus.Invalid, null, $"'{path}' failed its code-signature check: {reason}.");

    // 64 upper-case hex digits with the separators removed, or an ArgumentException naming the bad value.
    private static string Normalize(string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        string bare = fingerprint.Replace(":", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return bare.Length == Sha256Bytes * 2 && bare.All(char.IsAsciiHexDigit)
            ? bare
            : throw new ArgumentException(
                $"'{fingerprint}' is not a SHA-256 certificate fingerprint (64 hex digits, optionally ':'-separated).",
                nameof(fingerprint));
    }
}
