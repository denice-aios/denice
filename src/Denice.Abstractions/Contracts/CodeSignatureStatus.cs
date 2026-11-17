// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>
/// The outcome of one <see cref="IPlatformFileSystem.CheckCodeSignature"/>. Only <see cref="Valid"/> means the file is signed by Denice;
/// <see cref="NotApplicable"/> means the operating system has no code signature to check.
/// </summary>
public enum CodeSignatureStatus
{
    /// <summary>The operating system has no code signature (Linux); nothing was checked.</summary>
    NotApplicable,

    /// <summary>The signature is intact and its leaf signing certificate is pinned.</summary>
    Valid,

    /// <summary>The file carries no code signature.</summary>
    NotSigned,

    /// <summary>The signature is present but broken: the file was modified after signing, or the signature is
    /// malformed, or the file could not be checked.</summary>
    Invalid,

    /// <summary>The signature is intact, but its leaf signing certificate is not one of the pinned certificates (or there
    /// is no signing certificate, as with an ad-hoc signature).</summary>
    UnpinnedSigner,
}
