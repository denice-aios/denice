// Copyright (c) Denice.ai. All rights reserved.

using System.Text.RegularExpressions;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using Denice.Updates;
using FluentAssertions;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// The pinned list of Denice's code-signing certificates (<see cref="DeniceSigningCertificates"/>) is well formed and is
/// accepted, as written, by the real platform's code-signature check — the one place it is used.
/// </summary>
public sealed partial class DeniceSigningCertificatesTests
{
    [Fact]
    public void Sha256Fingerprints_AreDistinctUpperCaseColonSeparatedSha256Fingerprints()
    {
        DeniceSigningCertificates.Sha256Fingerprints.Should().NotBeEmpty()
            .And.OnlyHaveUniqueItems()
            .And.AllSatisfy(fingerprint => Sha256Fingerprint().IsMatch(fingerprint).Should().BeTrue(fingerprint));
    }

    [Fact]
    public void Sha256Fingerprints_AreAcceptedByTheRealCodeSignatureCheck()
    {
        ISystemServices services = SystemServicesBuilder.Real().Build();
        string directory = services.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-pins-");
        try
        {
            CodeSignatureCheck check = services.Platform.FileSystem.CheckCodeSignature(
                Path.Combine(directory, "missing"), DeniceSigningCertificates.Sha256Fingerprints);

            check.Status.Should().NotBe(CodeSignatureStatus.Valid, "a missing file is never signed by Denice");
        }
        finally
        {
            services.FileSystem.GetDirectoryWriter().DeleteDirectory(directory, recursive: true);
        }
    }

    [GeneratedRegex("^[0-9A-F]{2}(:[0-9A-F]{2}){31}$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Sha256Fingerprint();
}
