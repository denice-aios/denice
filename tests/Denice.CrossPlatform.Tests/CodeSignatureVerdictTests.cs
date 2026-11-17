// Copyright (c) Denice.ai. All rights reserved.

using System.Text;
using Denice.Abstractions.Contracts;
using FluentAssertions;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// The OS-uniform half of the code-signature check (<see cref="CodeSignatureVerdict"/>): fingerprint formatting, pin
/// validation and normalisation, and the pinned/unpinned decision. These run identically on every OS, so the pin logic
/// the macOS and Windows checks share is proven on all three CI legs, including Linux, where no OS signature exists.
/// </summary>
public sealed class CodeSignatureVerdictTests
{
    // SHA-256("abc") from FIPS 180-2, appendix B.1 — a published vector, so the formatting is checked against an
    // independent value rather than against the code under test.
    private const string AbcSha256 =
        "BA:78:16:BF:8F:01:CF:EA:41:41:40:DE:5D:AE:22:23:B0:03:61:A3:96:17:7A:9C:B4:10:FF:61:F2:00:15:AD";

    private static readonly byte[] Abc = Encoding.ASCII.GetBytes("abc");

    [Fact]
    public void Fingerprint_IsUpperCaseColonSeparatedSha256()
    {
        CodeSignatureVerdict.Fingerprint(Abc).Should().Be(AbcSha256);
    }

    [Fact]
    public void Signed_WhenTheLeafIsPinned_IsValidAndReportsTheSigner()
    {
        CodeSignatureCheck check = CodeSignatureVerdict.Signed(Abc, [AbcSha256]);

        check.Status.Should().Be(CodeSignatureStatus.Valid);
        check.SignerSha256.Should().Be(AbcSha256);
    }

    [Fact]
    public void Signed_MatchesAPinWrittenInLowerCaseWithoutSeparators()
    {
        const string bareLowerCase = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

        CodeSignatureVerdict.Signed(Abc, [bareLowerCase]).Status.Should().Be(CodeSignatureStatus.Valid);
    }

    [Fact]
    public void Signed_WhenTheLeafIsNotPinned_IsUnpinnedAndStillReportsTheSigner()
    {
        string other = string.Join(':', Enumerable.Repeat("00", 32));

        CodeSignatureCheck check = CodeSignatureVerdict.Signed(Abc, [other]);

        check.Status.Should().Be(CodeSignatureStatus.UnpinnedSigner);
        check.SignerSha256.Should().Be(AbcSha256);
        check.Detail.Should().Contain(AbcSha256);
    }

    [Fact]
    public void Signed_WithNoPins_IsUnpinned()
    {
        CodeSignatureVerdict.Signed(Abc, []).Status.Should().Be(CodeSignatureStatus.UnpinnedSigner);
    }

    [Theory]
    [InlineData("BA:78:16")]
    [InlineData("ZZ:78:16:BF:8F:01:CF:EA:41:41:40:DE:5D:AE:22:23:B0:03:61:A3:96:17:7A:9C:B4:10:FF:61:F2:00:15:AD")]
    [InlineData("")]
    public void RequireWellFormed_RejectsAPinThatIsNotASha256Fingerprint(string pin)
    {
        Action act = () => CodeSignatureVerdict.RequireWellFormed([AbcSha256, pin]);

        act.Should().Throw<ArgumentException>().WithMessage($"*'{pin}' is not a SHA-256 certificate fingerprint*");
    }

    [Fact]
    public void RequireWellFormed_RejectsANullList()
    {
        Action act = () => CodeSignatureVerdict.RequireWellFormed(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TheOtherResults_CarryTheirStatusAndThePath()
    {
        CodeSignatureVerdict.NotSigned("/a").Should().Be(
            new CodeSignatureCheck(CodeSignatureStatus.NotSigned, null, "'/a' has no code signature."));
        CodeSignatureVerdict.Invalid("/a", "why").Should().Be(
            new CodeSignatureCheck(CodeSignatureStatus.Invalid, null, "'/a' failed its code-signature check: why."));
        CodeSignatureCheck adHoc = CodeSignatureVerdict.NoSigningCertificate("/a");
        adHoc.Status.Should().Be(CodeSignatureStatus.UnpinnedSigner);
        adHoc.SignerSha256.Should().BeNull();
        adHoc.Detail.Should().Contain("'/a'");
    }
}
