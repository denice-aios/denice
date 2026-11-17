// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>
/// The platform's file-system capability: the complete set of operations the engine needs that the managed BCL
/// cannot do OS-uniformly — the version-pointer symlinks the machine layout depends on, the POSIX executable
/// bit, per-filesystem case-sensitivity detection, the OS-divergent raw path-separator value, and the operating-system
/// code signature of a file (Apple code signature on macOS, Authenticode on Windows, none on Linux). It is a
/// capability surface and is allowed to grow, but every member is part of one coherent complete set.
/// Implementations are per-OS behind this locked interface; the engine consumes only the interface and stays
/// OS-agnostic.
/// </summary>
public interface IPlatformFileSystem
{
    /// <summary>
    /// Gets the OS-native directory-separator character (<c>/</c> on POSIX, <c>\</c> on Windows). Reading the raw
    /// <c>Path.DirectorySeparatorChar</c> value is not input-deterministic, so it is owned here (per the Path-purity
    /// rule) and every other site reaches the separator through this interface.
    /// </summary>
    char DirectorySeparator { get; }

    /// <summary>
    /// Gets the operating-system code-signature format this OS uses, so an OS-agnostic caller can tell which files carry
    /// a signature (an app bundle and Mach-O executables on macOS; <c>.exe</c> and <c>.dll</c> files on Windows) and
    /// whether there is anything to check at all (<see cref="CodeSignatureFormat.None"/> on Linux).
    /// </summary>
    CodeSignatureFormat CodeSignatureFormat { get; }

    /// <summary>
    /// Gets where this OS keeps Denice's per-user data folder (the folder <c>settings.yaml</c> lives in): Windows
    /// <c>%LOCALAPPDATA%\Denice\</c>, macOS <c>~/Library/Application Support/Denice/</c>, Linux <c>~/.config/denice/</c>.
    /// It is a description resolved by the caller through <see cref="IEnvironment"/>, so it reads nothing itself.
    /// </summary>
    UserDataFolder UserDataFolder { get; }

    /// <summary>
    /// Gets a value indicating whether the path is a symlink.
    /// </summary>
    /// <param name="linkPath">The path to test.</param>
    /// <returns><see langword="true"/> when <paramref name="linkPath"/> is a symlink; otherwise
    /// <see langword="false"/> (a regular file, a directory, or absent).</returns>
    bool IsLinkTarget(string linkPath);

    /// <summary>
    /// Reads a symlink's raw (unresolved) target string.
    /// </summary>
    /// <param name="linkPath">The path expected to be a symlink.</param>
    /// <returns>The raw target string the link points at, or <see langword="null"/> when
    /// <paramref name="linkPath"/> is not a symlink (a regular file, a directory, or absent).</returns>
    string? ReadLinkTarget(string linkPath);

    /// <summary>
    /// Atomically points the symlink at <paramref name="linkPath"/> at <paramref name="relativeTarget"/>, creating
    /// the link when it is absent and replacing it when it already exists — the link is never momentarily absent.
    /// Any missing parent directory is created first. This operation is unconditional: it does not compare the
    /// current target, so deciding whether a re-point is needed is the caller's concern. It refuses (throws) when
    /// <paramref name="linkPath"/> holds a real file or directory rather than a symlink, so a real file is never
    /// replaced by a link.
    /// </summary>
    /// <param name="linkPath">The symlink path to create or replace.</param>
    /// <param name="relativeTarget">The relative target the link should resolve to.</param>
    void MakeLinkTarget(string linkPath, string relativeTarget);

    /// <summary>
    /// Removes the symlink at <paramref name="linkPath"/>, and only the link. The target the link points at —
    /// whether a file or a directory, and everything inside it — is never touched. It refuses (throws) when
    /// <paramref name="linkPath"/> holds a real file or directory rather than a symlink.
    /// </summary>
    /// <param name="linkPath">The symlink path to remove.</param>
    void RemoveLinkTarget(string linkPath);

    /// <summary>
    /// Gets a value indicating whether this OS uses (or supports) the executable bit. It is hard <see langword="true"/>
    /// on POSIX and hard <see langword="false"/> on Windows. Callers guard the other three executable-flag methods
    /// on this, so the bit is touched only where the OS has one.
    /// </summary>
    /// <returns><see langword="true"/> on POSIX; <see langword="false"/> on Windows.</returns>
    bool NeedsExecutableFlag();

    /// <summary>
    /// Gets a value indicating whether the file at <paramref name="path"/> has its executable bit set. Valid only
    /// where <see cref="NeedsExecutableFlag"/> is <see langword="true"/>; on Windows it throws.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns><see langword="true"/> when the executable bit is set (POSIX only).</returns>
    bool IsExecutable(string path);

    /// <summary>
    /// Adds the executable bit to the file at <paramref name="path"/> (POSIX <c>chmod +x</c>), preserving its
    /// existing read/write bits. Valid only where <see cref="NeedsExecutableFlag"/> is <see langword="true"/>; on
    /// Windows it throws.
    /// </summary>
    /// <param name="path">The file path.</param>
    void MakeExecutable(string path);

    /// <summary>
    /// Removes the executable bit from the file at <paramref name="path"/> (POSIX <c>chmod -x</c>), preserving its
    /// existing read/write bits. Valid only where <see cref="NeedsExecutableFlag"/> is <see langword="true"/>; on
    /// Windows it throws.
    /// </summary>
    /// <param name="path">The file path.</param>
    void MakeNonExecutable(string path);

    /// <summary>
    /// Gets a value indicating whether the file system backing <paramref name="path"/> is case-sensitive, detected
    /// against the exact directory being searched. Case sensitivity is a property of the file system, not the OS —
    /// it is neither always insensitive on Windows nor always sensitive on macOS — so it is probed per path.
    /// </summary>
    /// <param name="path">The directory path whose backing file system is probed.</param>
    /// <returns><see langword="true"/> when name matching against <paramref name="path"/> is case-sensitive.</returns>
    bool IsCaseSensitive(string path);

    /// <summary>
    /// Checks the operating-system code signature of the file or app bundle at <paramref name="path"/>: the signature
    /// must be present and intact, and its leaf signing certificate's SHA-256 fingerprint must be one of
    /// <paramref name="pinnedSignerSha256"/>. Denice signs with self-signed certificates that never chain to a trusted
    /// root, so trust in an operating-system root store is neither required nor sufficient — only the pin decides who
    /// signed. On Linux, which has no OS code signature, the result is <see cref="CodeSignatureStatus.NotApplicable"/>.
    /// </summary>
    /// <param name="path">The file or app bundle to check.</param>
    /// <param name="pinnedSignerSha256">The accepted signer-certificate SHA-256 fingerprints, as 64 hex digits with or
    /// without <c>:</c> separators, in either case.</param>
    /// <returns>The outcome, the signer fingerprint that was found, and a human-readable detail.</returns>
    /// <exception cref="ArgumentException">A pinned fingerprint is not a SHA-256 fingerprint.</exception>
    CodeSignatureCheck CheckCodeSignature(string path, IReadOnlyCollection<string> pinnedSignerSha256);
}
