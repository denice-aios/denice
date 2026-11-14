// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// Acceptance #6 (contract step 6): the owned <see cref="IEnvironment.GetBaseDirectory"/> accessor is exercised on all
/// three CI legs. This test lives in the OS-agnostic test project (not per-OS gated) and asserts, through the real container, that the accessor returns a non-empty path to a directory
/// that actually exists. Existence is checked through the owned <see cref="IDirectoryEnumerator"/> — never against raw
/// <c>AppContext.BaseDirectory</c>, which DN0011 blocks in this project.
/// </summary>
public sealed class BaseDirectoryAccessorTests
{
    [Fact]
    public void GetBaseDirectory_ReturnsANonEmptyPathToADirectoryThatExists()
    {
        ISystemServices services = SystemServicesBuilder.Real().Build();

        string baseDirectory = services.Environment.GetBaseDirectory();

        baseDirectory.Should().NotBeNullOrWhiteSpace(
            "GetBaseDirectory() returns the real base directory the application was loaded from");
        services.FileSystem.GetDirectoryReader().DirectoryExists(baseDirectory).Should().BeTrue(
            "the base directory the assemblies were loaded from is a real directory on disk");
    }
}
