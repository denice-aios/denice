// Copyright (c) Denice.ai. All rights reserved.

using System.Collections.Generic;

namespace Denice.Updates;

/// <summary>
/// The one list of Denice's operating-system code-signing certificates that Denice Runtime accepts on a downloaded
/// version: the SHA-256 fingerprint of each certificate CI signs with (<c>codesign</c> on macOS, <c>signtool</c> on
/// Windows). The certificates are self-signed and never chain to a trusted root, so this list — not an OS root store —
/// is what makes a signature Denice's. Denice Runtime passes it to <c>IPlatformFileSystem.CheckCodeSignature</c> and the
/// tests read it from here; it is written nowhere else. A macOS certificate never signs a PE file and a Windows certificate never signs a
/// Mach-O, so one list serves both operating systems.
/// </summary>
/// <remarks>
/// <para>
/// Renewal rule. Each certificate lasts one year and is renewed, which is why this is a list and not one value: a new
/// certificate's fingerprint is added in a release still signed by the old certificate, before CI switches to the
/// new one — so an installed Denice Runtime already accepts the new signer when the first release signed with it
/// arrives.
/// </para>
/// </remarks>
public static class DeniceSigningCertificates
{
    /// <summary>
    /// Gets the accepted signer-certificate SHA-256 fingerprints, as upper-case <c>:</c>-separated hex (the form
    /// <c>openssl x509 -noout -fingerprint -sha256</c> prints). In order: the macOS certificate (CN=Denice CI macOS code
    /// signing (self-signed, untrusted), O=denice-aios) and the Windows certificate (CN=Denice CI Windows code signing
    /// (self-signed, untrusted), O=denice-aios).
    /// </summary>
    public static IReadOnlyList<string> Sha256Fingerprints { get; } =
    [
        "51:DC:0C:88:24:16:E0:A9:AD:F4:45:EE:94:A7:2D:BA:25:EE:E5:9C:FB:0A:1D:EC:26:1E:B3:B1:79:4C:0F:D4",
        "14:62:45:8A:15:96:90:D1:43:70:0A:A0:0C:DE:80:39:0E:98:49:CE:B8:4D:1E:49:41:A1:F6:68:1C:18:6A:A9",
    ];
}
