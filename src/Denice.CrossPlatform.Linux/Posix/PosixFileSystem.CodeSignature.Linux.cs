// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform.Posix;

/// <summary>
/// The Linux-only code-signature fragment of the POSIX file system (os-specific-file-naming-convention): Linux has no
/// operating-system code signature, so every check reports <see cref="CodeSignatureStatus.NotApplicable"/> (the cosign
/// bundle covers Linux). It still validates the pinned fingerprints, so a malformed pin fails on every OS alike. This
/// is genuinely Linux-specific behavior, not a copy of the macOS Security-framework check; it is compiled only into the
/// Linux project, never link-shared into the macOS build.
/// </summary>
internal sealed partial class PosixFileSystem
{
    /// <inheritdoc />
    public CodeSignatureFormat CodeSignatureFormat => CodeSignatureFormat.None;

    /// <inheritdoc />
    public CodeSignatureCheck CheckCodeSignature(string path, IReadOnlyCollection<string> pinnedSignerSha256)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        CodeSignatureVerdict.RequireWellFormed(pinnedSignerSha256);
        return new CodeSignatureCheck(
            CodeSignatureStatus.NotApplicable,
            null,
            $"Linux has no operating-system code signature; '{path}' was not checked.");
    }
}
