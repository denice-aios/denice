// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using Denice.Updates;
using FluentAssertions;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// The cosign check against a genuine keyless Sigstore signature: cosign's own v3.1.3 release signs
/// <c>cosign_checksums.txt</c> (identity <c>keyless@projectsigstore.iam.gserviceaccount.com</c>, issuer
/// <c>https://accounts.google.com</c>). With that identity pinned the signature verifies against the trust root shipped in
/// Denice Runtime; with Denice's own workflow identity pinned, or a single byte changed, it does not.
/// </summary>
public sealed class ReleaseSignatureTests
{
    private const string CosignIdentity = "^keyless@projectsigstore\\.iam\\.gserviceaccount\\.com$";
    private const string GoogleIssuer = "https://accounts.google.com";

    private readonly byte[] artifact;
    private readonly string bundle;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReleaseSignatureTests"/> class from the fixture files.
    /// </summary>
    public ReleaseSignatureTests()
    {
        ISystemServices services = SystemServicesBuilder.Real().Build();
        string fixtures = Path.Combine(services.Environment.GetBaseDirectory(), "Fixtures", "Sigstore");
        IFileReader reader = services.FileSystem.GetFileReader();
        artifact = reader.ReadAllBytes(Path.Combine(fixtures, "cosign_checksums.txt"));
        bundle = reader.ReadAllText(Path.Combine(fixtures, "cosign_checksums.txt.sigstore.json"));
    }

    [Fact]
    public async Task AGenuineSignature_WithItsSignerPinned_Verifies()
    {
        string? problem = await ReleaseSignature.ProblemAsync(artifact, bundle, CosignIdentity, GoogleIssuer, CancellationToken.None);

        problem.Should().BeNull();
    }

    [Fact]
    public async Task AGenuineSignature_ByAnotherSigner_IsNotDenices()
    {
        string? problem = await ReleaseSignature.ProblemAsync(artifact, bundle, CancellationToken.None);

        problem.Should().Contain("not a valid signature by Denice's release workflow");
    }

    [Fact]
    public async Task TheRightSigner_WithTheWrongIssuer_IsRefused()
    {
        string? problem = await ReleaseSignature.ProblemAsync(
            artifact, bundle, CosignIdentity, ReleaseSignature.GitHubActionsIssuer, CancellationToken.None);

        problem.Should().NotBeNull();
    }

    [Fact]
    public async Task OneChangedByte_IsRefused()
    {
        byte[] changed = (byte[])artifact.Clone();
        changed[10] ^= 1;

        string? problem = await ReleaseSignature.ProblemAsync(changed, bundle, CosignIdentity, GoogleIssuer, CancellationToken.None);

        problem.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json")]
    public async Task AnUnreadableBundle_IsRefusedWithoutThrowing(string unreadable)
    {
        string? problem = await ReleaseSignature.ProblemAsync(artifact, unreadable, CosignIdentity, GoogleIssuer, CancellationToken.None);

        problem.Should().NotBeNull();
    }

    [Fact]
    public void DenicesPinnedIdentity_IsTheReleaseWorkflowOnDevAndMainOnly()
    {
        var pattern = new System.Text.RegularExpressions.Regex(ReleaseSignature.DeniceWorkflowPattern, System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

        pattern.IsMatch("https://github.com/denice-aios/denice/.github/workflows/ci.yml@refs/heads/dev").Should().BeTrue();
        pattern.IsMatch("https://github.com/denice-aios/denice/.github/workflows/ci.yml@refs/heads/main").Should().BeTrue();
        pattern.IsMatch("https://github.com/denice-aios/denice/.github/workflows/ci.yml@refs/pull/2/merge").Should().BeFalse();
        pattern.IsMatch("https://github.com/someone/denice/.github/workflows/ci.yml@refs/heads/main").Should().BeFalse();
        Encoding.UTF8.GetByteCount(ReleaseSignature.GitHubActionsIssuer).Should().BePositive();
    }
}
