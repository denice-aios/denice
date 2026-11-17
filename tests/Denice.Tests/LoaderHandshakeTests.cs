// Copyright (c) Denice.ai. All rights reserved.

using System.Collections.Generic;
using System.IO;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using Denice.Updates;
using FluentAssertions;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// Denice Runtime's side of the loader handshake: it recognises a loader start from the four protocol variables and
/// records that it started in its version folder, which is what lets the loader keep it and remove older versions.
/// </summary>
public sealed class LoaderHandshakeTests
{
    private const string LoaderVersion = "0.1.100-pre-release+aaaaaaa";

    private readonly string root;
    private readonly string versionDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoaderHandshakeTests"/> class over a fresh temporary install root.
    /// </summary>
    public LoaderHandshakeTests()
    {
        ISystemServices real = SystemServicesBuilder.Real().Build();
        root = real.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-handshake-");
        versionDirectory = Path.Combine(root, LoaderProtocol.VersionsDirectory, "0.1.200-pre-release+bbbbbbb");
        real.FileSystem.GetDirectoryWriter().CreateDirectory(versionDirectory);
    }

    [Fact]
    public void Read_WithoutTheLoaderVariables_IsNoHandshake()
    {
        ISystemServices services = SystemServicesBuilder.Real()
            .With(FakeEnvironment.Create(home: root, variables: new Dictionary<string, string>(System.StringComparer.Ordinal)))
            .Build();

        LoaderHandshake.Read(services).Should().BeNull();
    }

    [Theory]
    [InlineData(LoaderProtocol.EnvInstallRoot)]
    [InlineData(LoaderProtocol.EnvLoaderPath)]
    [InlineData(LoaderProtocol.EnvLoaderVersion)]
    [InlineData(LoaderProtocol.EnvVersionDirectory)]
    public void Read_WithAnyVariableMissing_IsNoHandshake(string missing)
    {
        Dictionary<string, string> variables = LoaderVariables();
        variables.Remove(missing);
        ISystemServices services = SystemServicesBuilder.Real()
            .With(FakeEnvironment.Create(home: root, variables: variables))
            .Build();

        LoaderHandshake.Read(services).Should().BeNull();
    }

    [Fact]
    public void Read_WithTheLoaderVariables_ExposesThem()
    {
        ISystemServices services = SystemServicesBuilder.Real()
            .With(FakeEnvironment.Create(home: root, variables: LoaderVariables()))
            .Build();

        LoaderHandshake handshake = LoaderHandshake.Read(services)!;

        handshake.Should().NotBeNull();
        handshake.InstallRoot.Should().Be(root);
        handshake.LoaderVersion.Should().Be(LoaderVersion);
        handshake.VersionDirectory.Should().Be(versionDirectory);
    }

    [Fact]
    public void RecordStarted_WritesTheStartedMarkerInTheVersionFolder()
    {
        ISystemServices services = SystemServicesBuilder.Real()
            .With(FakeEnvironment.Create(home: root, variables: LoaderVariables()))
            .Build();

        LoaderHandshake.Read(services)!.RecordStarted();

        services.FileSystem.GetFileReader().Exists(Path.Combine(versionDirectory, LoaderProtocol.StartedMarker)).Should().BeTrue();
    }

    private Dictionary<string, string> LoaderVariables() => new(System.StringComparer.Ordinal)
    {
        [LoaderProtocol.EnvInstallRoot] = root,
        [LoaderProtocol.EnvLoaderPath] = Path.Combine(root, "denice"),
        [LoaderProtocol.EnvLoaderVersion] = LoaderVersion,
        [LoaderProtocol.EnvVersionDirectory] = versionDirectory,
    };
}
