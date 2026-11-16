// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>
/// The operating-system code-signature format <see cref="IPlatformFileSystem.CheckCodeSignature"/> checks.
/// </summary>
public enum CodeSignatureFormat
{
    /// <summary>The operating system has no code signature (Linux); every check is
    /// <see cref="CodeSignatureStatus.NotApplicable"/>.</summary>
    None,

    /// <summary>The Apple code signature on app bundles and Mach-O executables (macOS).</summary>
    AppleCodeSignature,

    /// <summary>The Authenticode signature on PE files — <c>.exe</c> and <c>.dll</c> (Windows).</summary>
    Authenticode,
}
