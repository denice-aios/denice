// Copyright (c) Denice.ai. All rights reserved.

using System.Collections.Generic;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// Spec for <see cref="IPlatformFileSystem.CheckCodeSignature"/> against REAL signatures. The fixtures under
/// <c>Fixtures/CodeSignature</c> were signed with throwaway self-signed, untrusted code-signing certificates the same
/// way CI signs Denice (<c>codesign --options runtime</c> on macOS, Authenticode SHA-256 on Windows; see
/// <c>make-fixtures.sh</c> there), so a pinned fixture certificate stands in for Denice's pinned certificate. Every test
/// runs on every OS through the real per-OS platform and never branches on the OS: it picks the fixture set by the
/// platform's own <see cref="IPlatformFileSystem.CodeSignatureFormat"/>, and on Linux (no OS code signature) every
/// check is expected to be <see cref="CodeSignatureStatus.NotApplicable"/>. Fixture copies are made in a temporary
/// directory through the owned filesystem services, so a test may modify its copy.
/// </summary>
public sealed class CodeSignatureSpecTests : IDisposable
{
    // The Windows fixture: a small managed PE signed with the fixture certificate (Authenticode, SHA-256), and the same
    // file unsigned. The offset lies in hashed section data (osslsigncode verify reports a digest mismatch after the flip).
    private const int WindowsTamperOffset = 0x400;
    private const string WindowsSigner =
        "56:51:C6:CB:D1:6D:5E:08:DE:E0:4E:D6:42:DD:3B:88:2D:7B:4F:D9:FF:F0:0D:2F:1C:F7:DE:81:E5:E9:F7:18";

    private static readonly string NotPinned = string.Join(':', Enumerable.Repeat("00", 32));

    // The macOS fixtures: a thin x86_64 Mach-O signed with the fixture certificate (and the same file unsigned and
    // ad-hoc signed), and a minimal app bundle signed with it. Offset 0x800 lies in the first code page (hashed), past
    // the load commands. On Linux the same files are passed in and never read.
    private static readonly SignatureFixtures MacOS = new(
        "macos",
        "signed-macho",
        "unsigned-macho",
        0x800,
        "2B:EA:8E:85:D4:18:37:7F:D9:46:45:ED:16:34:2C:3F:AD:AC:49:AC:AD:61:DC:7D:D1:2E:3E:BD:3E:8D:A6:4A",
        [
            new("the signed app bundle", "Signed.app", null, 0, CodeSignatureStatus.Valid),
            new("the app bundle with its sealed Info.plist modified", "Signed.app", "Signed.app/Contents/Info.plist", 0, CodeSignatureStatus.Invalid),
            new("the app bundle with its executable modified", "Signed.app", "Signed.app/Contents/MacOS/Signed", 0x800, CodeSignatureStatus.Invalid),
            new("the ad-hoc signed Mach-O (no signing certificate)", "adhoc-macho", null, 0, CodeSignatureStatus.UnpinnedSigner),
        ]);

    private static readonly Dictionary<CodeSignatureFormat, SignatureFixtures> FixturesByFormat =
        new Dictionary<CodeSignatureFormat, SignatureFixtures>
        {
            [CodeSignatureFormat.None] = MacOS,
            [CodeSignatureFormat.AppleCodeSignature] = MacOS,
            [CodeSignatureFormat.Authenticode] = new(
                "windows",
                "signed.dll",
                "unsigned.dll",
                WindowsTamperOffset,
                WindowsSigner,
                []),
        };

    private readonly ISystemServices services;
    private readonly string root;
    private readonly IPlatformFileSystem fileSystem;
    private readonly SignatureFixtures fixtures;
    private readonly string fixtureDirectory;

    public CodeSignatureSpecTests()
    {
        services = SystemServicesBuilder.Real().Build();
        root = services.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-code-signature-");
        fileSystem = services.Platform.FileSystem;
        fixtures = FixturesByFormat[fileSystem.CodeSignatureFormat];
        fixtureDirectory = Path.Combine(
            services.Environment.GetBaseDirectory(), "Fixtures", "CodeSignature", fixtures.Directory);
    }

    private bool HasNoOsSignature => fileSystem.CodeSignatureFormat == CodeSignatureFormat.None;

    [Fact]
    public void CheckCodeSignature_WhenTheSignerIsPinned_IsValid()
    {
        CodeSignatureCheck check = fileSystem.CheckCodeSignature(CopyIn(fixtures.Signed), [fixtures.Signer]);

        check.Status.Should().Be(Expected(CodeSignatureStatus.Valid), check.Detail);
        check.SignerSha256.Should().Be(HasNoOsSignature ? null : fixtures.Signer);
    }

    [Fact]
    public void CheckCodeSignature_WhenTheSignerIsPinnedAmongOthers_IsValid()
    {
        CodeSignatureCheck check = fileSystem.CheckCodeSignature(CopyIn(fixtures.Signed), [NotPinned, fixtures.Signer]);

        check.Status.Should().Be(Expected(CodeSignatureStatus.Valid), check.Detail);
    }

    [Fact]
    public void CheckCodeSignature_WhenTheSignerIsNotPinned_IsUnpinnedAndNamesTheSigner()
    {
        CodeSignatureCheck check = fileSystem.CheckCodeSignature(CopyIn(fixtures.Signed), [NotPinned]);

        check.Status.Should().Be(Expected(CodeSignatureStatus.UnpinnedSigner), check.Detail);
        check.SignerSha256.Should().Be(HasNoOsSignature ? null : fixtures.Signer);
    }

    [Fact]
    public async Task CheckCodeSignature_WhenTheSignedFileWasModified_IsInvalid()
    {
        string copy = CopyIn(fixtures.Signed);
        await FlipByteAsync(copy, fixtures.TamperOffset);

        CodeSignatureCheck check = fileSystem.CheckCodeSignature(copy, [fixtures.Signer]);

        check.Status.Should().Be(Expected(CodeSignatureStatus.Invalid), check.Detail);
    }

    [Fact]
    public void CheckCodeSignature_WhenTheFileIsUnsigned_IsNotSigned()
    {
        CodeSignatureCheck check = fileSystem.CheckCodeSignature(CopyIn(fixtures.Unsigned), [fixtures.Signer]);

        check.Status.Should().Be(Expected(CodeSignatureStatus.NotSigned), check.Detail);
        check.SignerSha256.Should().BeNull();
    }

    [Fact]
    public void CheckCodeSignature_WhenTheFileIsMissing_IsInvalid()
    {
        CodeSignatureCheck check = fileSystem.CheckCodeSignature(Path.Combine(root, "missing"), [fixtures.Signer]);

        check.Status.Should().Be(Expected(CodeSignatureStatus.Invalid), check.Detail);
    }

    [Fact]
    public async Task CheckCodeSignature_FormatSpecificCases_HaveTheirExpectedStatus()
    {
        foreach (SignatureCase signatureCase in fixtures.ExtraCases)
        {
            string copy = CopyIn(signatureCase.Path);
            if (signatureCase.TamperFile is not null)
            {
                await FlipByteAsync(Path.Combine(root, signatureCase.TamperFile), signatureCase.TamperOffset);
            }

            CodeSignatureCheck check = fileSystem.CheckCodeSignature(copy, [fixtures.Signer]);

            check.Status.Should().Be(Expected(signatureCase.Expected), $"{signatureCase.Description}: {check.Detail}");
            DeleteCopy(signatureCase.Path);
        }
    }

    [Fact]
    public void CheckCodeSignature_WhenAPinIsMalformed_Throws()
    {
        Action act = () => fileSystem.CheckCodeSignature(CopyIn(fixtures.Signed), [fixtures.Signer, "not-a-fingerprint"]);

        act.Should().Throw<ArgumentException>().WithMessage("*not-a-fingerprint*");
    }

    [Fact]
    public void CheckCodeSignature_WhenThePathIsEmpty_Throws()
    {
        Action act = () => fileSystem.CheckCodeSignature(string.Empty, [fixtures.Signer]);

        act.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        try
        {
            services.FileSystem.GetDirectoryWriter().DeleteDirectory(root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing to clean up.
        }
    }

    // What a check reports on this platform: the given status where the OS has a code signature, NotApplicable where it
    // has none (Linux).
    private CodeSignatureStatus Expected(CodeSignatureStatus withAnOsSignature) =>
        HasNoOsSignature ? CodeSignatureStatus.NotApplicable : withAnOsSignature;

    // Copies a fixture file or bundle into the temporary directory and returns the copy's path.
    private string CopyIn(string relative)
    {
        string source = Path.Combine(fixtureDirectory, relative);
        string destination = Path.Combine(root, relative);
        if (services.FileSystem.GetDirectoryReader().DirectoryExists(source))
        {
            CopyTree(source, destination);
        }
        else
        {
            services.FileSystem.GetFileWriter().Copy(source, destination);
        }

        return destination;
    }

    private void CopyTree(string source, string destination)
    {
        services.FileSystem.GetDirectoryWriter().CreateDirectory(destination);
        foreach (DirectoryChild child in services.FileSystem.GetDirectoryReader().EnumerateChildren(source))
        {
            string target = Path.Combine(destination, Path.GetFileName(child.FullPath));
            if (child.IsDirectory)
            {
                CopyTree(child.FullPath, target);
            }
            else
            {
                services.FileSystem.GetFileWriter().Copy(child.FullPath, target);
            }
        }
    }

    private void DeleteCopy(string relative)
    {
        string copy = Path.Combine(root, relative);
        if (services.FileSystem.GetDirectoryReader().DirectoryExists(copy))
        {
            services.FileSystem.GetDirectoryWriter().DeleteDirectory(copy, recursive: true);
        }
        else
        {
            services.FileSystem.GetFileWriter().DeleteFile(copy);
        }
    }

    private async Task FlipByteAsync(string path, int offset)
    {
        byte[] bytes = services.FileSystem.GetFileReader().ReadAllBytes(path);
        bytes[offset] ^= 0xFF;
        await services.FileSystem.GetFileWriter().WriteAllBytesAsync(path, bytes, CancellationToken.None).ConfigureAwait(false);
    }

    // One OS's fixture set: the directory under Fixtures/CodeSignature, the signed and unsigned files, a byte offset whose
    // change breaks the signed file's signature, the fixture certificate's SHA-256 fingerprint, and the cases only that
    // format has.
    private sealed record SignatureFixtures(
        string Directory,
        string Signed,
        string Unsigned,
        int TamperOffset,
        string Signer,
        IReadOnlyList<SignatureCase> ExtraCases);

    // A format-specific case: a fixture to copy, an optional file inside the copy and the offset of the byte flipped in
    // it, and the status the check must report.
    private sealed record SignatureCase(
        string Description,
        string Path,
        string? TamperFile,
        int TamperOffset,
        CodeSignatureStatus Expected);
}
