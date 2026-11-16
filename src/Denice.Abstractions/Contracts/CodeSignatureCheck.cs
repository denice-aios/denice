// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>
/// The result of one <see cref="IPlatformFileSystem.CheckCodeSignature"/>.
/// </summary>
/// <param name="Status">The outcome; only <see cref="CodeSignatureStatus.Valid"/> means signed by a pinned
/// certificate.</param>
/// <param name="SignerSha256">The leaf signing certificate's SHA-256 fingerprint as upper-case, <c>:</c>-separated hex
/// (the form <c>openssl x509 -fingerprint -sha256</c> prints), or <see langword="null"/> when no signing certificate was
/// read.</param>
/// <param name="Detail">A human-readable explanation of the outcome, for logs and error messages.</param>
public readonly record struct CodeSignatureCheck(CodeSignatureStatus Status, string? SignerSha256, string Detail);
