// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Denice.Updates;

/// <summary>
/// Who a release must come from (pkg-697310396f74 download-proof, "All proofs"): the cosign signature over
/// <c>SHA256SUMS</c> by Denice's release workflow, and the OS code signature of Denice's own executables by one of
/// Denice's pinned certificates. Production always uses <see cref="Denice"/>; tests pin their own fixture signers.
/// </summary>
/// <param name="CosignProblem">Checks the cosign bundle over <c>SHA256SUMS</c>; returns why it fails, or null.</param>
/// <param name="PinnedSigners">The SHA-256 fingerprints of the certificates that may sign Denice's executables.</param>
internal sealed record ReleaseTrust(
    Func<ReadOnlyMemory<byte>, string, CancellationToken, Task<string?>> CosignProblem,
    IReadOnlyCollection<string> PinnedSigners)
{
    /// <summary>Gets Denice's own release trust: its CI workflow identity and its pinned signing certificates.</summary>
    internal static ReleaseTrust Denice { get; } =
        new(ReleaseSignature.ProblemAsync, [.. DeniceSigningCertificates.Sha256Fingerprints]);
}
