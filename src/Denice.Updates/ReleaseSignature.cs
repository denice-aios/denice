// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sigstore;

namespace Denice.Updates;

/// <summary>
/// Checks a release's cosign signature over its <c>SHA256SUMS</c>, the way Tim decided (pkg-697310396f74 download-proof:
/// "the cosign bundle against the Sigstore public trust root, pinned to Denice's own release workflow identity (the same
/// identity CI checks)"; pkg-471f394bb261: "Use Sigstore"). The trust root ships inside Denice Runtime, so the check
/// never fetches trust material; it needs the signature's own transparency-log proof, which the bundle carries.
/// </summary>
internal static class ReleaseSignature
{
    /// <summary>The OIDC issuer of GitHub Actions, which signs Denice's releases keylessly.</summary>
    internal const string GitHubActionsIssuer = "https://token.actions.githubusercontent.com";

    /// <summary>
    /// Denice's release workflow on the two branches that publish releases (dev: pre-releases; main: releases), the same
    /// identity eng/ci-verify-layout.sh pins in CI.
    /// </summary>
    internal const string DeniceWorkflowPattern =
        @"^https://github\.com/denice-aios/denice/\.github/workflows/ci\.yml@refs/heads/(dev|main)$";

    private const string TrustedRootResource = "Denice.Updates.SigstoreTrustedRoot.json";

    private static readonly Lazy<TrustedRoot> TrustedRootValue = new(LoadTrustedRoot);

    /// <summary>
    /// Checks <paramref name="checksums"/> against <paramref name="bundleJson"/> for Denice's release workflow.
    /// </summary>
    /// <param name="checksums">The bytes of the release's <c>SHA256SUMS</c>.</param>
    /// <param name="bundleJson">The release's <c>SHA256SUMS.cosign.bundle</c>.</param>
    /// <param name="ct">Cancels the check.</param>
    /// <returns><see langword="null"/> when the signature is Denice's; otherwise why it is not.</returns>
    internal static Task<string?> ProblemAsync(ReadOnlyMemory<byte> checksums, string bundleJson, CancellationToken ct) =>
        ProblemAsync(checksums, bundleJson, DeniceWorkflowPattern, GitHubActionsIssuer, ct);

    /// <summary>
    /// Checks <paramref name="artifact"/> against <paramref name="bundleJson"/> for the given signer identity.
    /// </summary>
    /// <param name="artifact">The signed bytes.</param>
    /// <param name="bundleJson">The Sigstore bundle.</param>
    /// <param name="identityPattern">The signer identity (certificate SAN) the signature must carry, as a pattern.</param>
    /// <param name="issuer">The OIDC issuer the signing certificate must name.</param>
    /// <param name="ct">Cancels the check.</param>
    /// <returns><see langword="null"/> when the signature verifies; otherwise why it does not.</returns>
    internal static async Task<string?> ProblemAsync(
        ReadOnlyMemory<byte> artifact, string bundleJson, string identityPattern, string issuer, CancellationToken ct)
    {
        SigstoreBundle bundle;
        try
        {
            bundle = SigstoreBundle.Deserialize(bundleJson);
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or ArgumentException)
        {
            return $"the cosign bundle cannot be read ({e.Message})";
        }

        var verifier = new SigstoreVerifier(new InMemoryTrustRootProvider(TrustedRootValue.Value));
        var policy = new VerificationPolicy
        {
            CertificateIdentity = new CertificateIdentity { SubjectAlternativeNamePattern = identityPattern, Issuer = issuer },
        };
        using var stream = new MemoryStream(artifact.ToArray(), writable: false);
        (bool success, _) = await verifier.TryVerifyStreamAsync(stream, bundle, policy, ct).ConfigureAwait(false);
        return success ? null : "the cosign signature is not a valid signature by Denice's release workflow";
    }

    private static TrustedRoot LoadTrustedRoot()
    {
        using Stream resource = typeof(ReleaseSignature).Assembly.GetManifestResourceStream(TrustedRootResource)
            ?? throw new InvalidOperationException($"Denice Runtime is missing its embedded {TrustedRootResource}.");
        using var bytes = new MemoryStream();
        resource.CopyTo(bytes);
        return TrustedRoot.Deserialize(System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
    }
}
