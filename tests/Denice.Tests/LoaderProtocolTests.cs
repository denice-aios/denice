// Copyright (c) Denice.ai. All rights reserved.

using System.IO;
using System.Text.Json;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using Denice.Updates;
using FluentAssertions;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// The .NET constants equal the one definition of the loader protocol, <c>eng/loader-protocol.json</c>; the Rust side
/// has the same test (rust/crates/denice-core/src/protocol.rs), so the two sides cannot drift apart.
/// </summary>
public sealed class LoaderProtocolTests
{
    private const string Version = "0.1.5-pre-release+abcdef0";

    private readonly ISystemServices services = SystemServicesBuilder.Real().Build();

    [Fact]
    public void Constants_EqualTheSharedProtocolFile()
    {
        JsonElement spec = Spec();
        JsonElement exits = spec.GetProperty("exitCodes");
        JsonElement env = spec.GetProperty("environment");
        JsonElement files = spec.GetProperty("files");

        exits.GetProperty("restartIntoNewerVersion").GetInt32().Should().Be(LoaderProtocol.ExitRestartIntoNewerVersion);
        exits.GetProperty("loaderReplaced").GetInt32().Should().Be(LoaderProtocol.ExitLoaderReplaced);
        env.GetProperty("installRoot").GetString().Should().Be(LoaderProtocol.EnvInstallRoot);
        env.GetProperty("loaderPath").GetString().Should().Be(LoaderProtocol.EnvLoaderPath);
        env.GetProperty("loaderVersion").GetString().Should().Be(LoaderProtocol.EnvLoaderVersion);
        env.GetProperty("versionDirectory").GetString().Should().Be(LoaderProtocol.EnvVersionDirectory);
        files.GetProperty("versionsDirectory").GetString().Should().Be(LoaderProtocol.VersionsDirectory);
        files.GetProperty("partialSuffix").GetString().Should().Be(LoaderProtocol.PartialSuffix);
        files.GetProperty("startedMarker").GetString().Should().Be(LoaderProtocol.StartedMarker);
        files.GetProperty("badMarker").GetString().Should().Be(LoaderProtocol.BadMarker);
        files.GetProperty("loaderStagingSuffix").GetString().Should().Be(LoaderProtocol.LoaderStagingSuffix);
        files.GetProperty("loaderBackupSuffix").GetString().Should().Be(LoaderProtocol.LoaderBackupSuffix);
        files.GetProperty("runtimeAssembly").GetString().Should().Be(LoaderProtocol.RuntimeAssembly);
        files.GetProperty("entryPrefix").GetString().Should().Be(LoaderProtocol.EntryPrefix);
    }

    [Theory]
    [InlineData("osx-arm64", "denice-v0.1.5-pre-release+abcdef0-osx-arm64")]
    [InlineData("linux-x64", "denice-v0.1.5-pre-release+abcdef0-linux-x64")]
    [InlineData("win-x64", "denice-v0.1.5-pre-release+abcdef0-win-x64.exe")]
    [InlineData("win-arm64", "denice-v0.1.5-pre-release+abcdef0-win-arm64.exe")]
    public void EntryFileName_IsTheNameTheRustBuildGivesTheEntry(string rid, string expected)
    {
        // The Rust side builds the same name (rust/crates/denice-core/src/platform.rs entry_file_name).
        LoaderProtocol.EntryFileName(Version, rid).Should().Be(expected);
        LoaderProtocol.EntryRid(expected, Version).Should().Be(rid);
    }

    [Theory]
    [InlineData("denice-v0.1.5-pre-release+abcdef0-win-x64.pdb")]
    [InlineData("denice-v0.1.5-pre-release+abcdef0-osx-arm64.dSYM")]
    [InlineData("denice-v0.1.5-pre-release+abcdef0-win-x64")]
    [InlineData("denice-v0.1.5-pre-release+abcdef0-")]
    [InlineData("denice-v0.1.5-pre-release+abcdef0-.exe")]
    [InlineData("denice-v0.1.6-pre-release+abcdef0-osx-arm64")]
    [InlineData("Denice.Runtime.dll")]
    public void EntryRid_OfAFileThatIsNotExactlyTheVersionsEntry_IsNull(string fileName) =>
        LoaderProtocol.EntryRid(fileName, Version).Should().BeNull();

    private JsonElement Spec()
    {
        IFileReader reader = services.FileSystem.GetFileReader();
        string? directory = services.Environment.GetBaseDirectory();
        while (directory is not null && !reader.Exists(Path.Combine(directory, "eng", "loader-protocol.json")))
        {
            directory = Path.GetDirectoryName(directory.TrimEnd('/', '\\'));
        }

        directory.Should().NotBeNull("the test runs inside the repository");
        using JsonDocument document = JsonDocument.Parse(reader.ReadAllText(Path.Combine(directory!, "eng", "loader-protocol.json")));
        return document.RootElement.Clone();
    }
}
