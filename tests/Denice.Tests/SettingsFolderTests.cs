// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using Denice.Updates;
using FluentAssertions;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// Where <c>settings.yaml</c> lives: Windows <c>%LOCALAPPDATA%\Denice\</c>, macOS
/// <c>~/Library/Application Support/Denice/</c>, Linux <c>~/.config/denice/</c>. The real platform this test runs on
/// must report its own OS's folder — told apart without an OS branch by the platform's code-signature format, the one
/// per-OS value that already distinguishes all three — and the resolution of every OS's folder runs on every OS through
/// the simulated platform.
/// </summary>
public sealed class SettingsFolderTests : IDisposable
{
    private const string LocalAppData = "LOCALAPPDATA";

    private static readonly Dictionary<CodeSignatureFormat, UserDataFolder> FolderByOs = new()
    {
        [CodeSignatureFormat.Authenticode] = new UserDataFolder(LocalAppData, "Denice"),
        [CodeSignatureFormat.AppleCodeSignature] = new UserDataFolder(null, Path.Combine("Library", "Application Support", "Denice")),
        [CodeSignatureFormat.None] = new UserDataFolder(null, Path.Combine(".config", "denice")),
    };

    private readonly ISystemServices real;
    private readonly string home;
    private readonly string localAppData;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsFolderTests"/> class over a temporary home and a separate
    /// temporary <c>LOCALAPPDATA</c> folder.
    /// </summary>
    public SettingsFolderTests()
    {
        real = SystemServicesBuilder.Real().Build();
        home = real.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-settings-home-");
        localAppData = real.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-settings-appdata-");
    }

    [Fact]
    public void TheRealPlatform_ReportsItsOwnOperatingSystemsFolder()
    {
        IPlatformFileSystem platform = real.Platform.FileSystem;

        platform.UserDataFolder.Should().Be(FolderByOs[platform.CodeSignatureFormat]);
    }

    [Theory]
    [InlineData(CodeSignatureFormat.Authenticode)]
    [InlineData(CodeSignatureFormat.AppleCodeSignature)]
    [InlineData(CodeSignatureFormat.None)]
    public void EveryOperatingSystemsFolder_ResolvesFromItsBase(CodeSignatureFormat os)
    {
        UserDataFolder folder = FolderByOs[os];
        string expectedBase = folder.EnvironmentVariable is null ? home : localAppData;

        var settings = new SettingsFile(Simulated(folder, localAppData));

        settings.FilePath.Should().Be(Path.Combine(expectedBase, folder.RelativePath, SettingsFile.FileName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative")]
    public void AVariableBase_ThatIsNotAnAbsolutePath_IsRefused(string? value)
    {
        Action act = () => _ = new SettingsFile(Simulated(FolderByOs[CodeSignatureFormat.Authenticode], value));

        act.Should().Throw<InvalidOperationException>().WithMessage("*LOCALAPPDATA*not set to an absolute path*");
    }

    [Fact]
    public void AFakePlatform_WithNoFolderSimulated_SaysHowToSimulateOne()
    {
        ISystemServices fake = SystemServicesBuilder.Fake().Build();

        Func<UserDataFolder> act = () => fake.Platform.FileSystem.UserDataFolder;

        act.Should().Throw<InvalidOperationException>().WithMessage("*SimulateUserDataFolder*");
    }

    public void Dispose()
    {
        real.FileSystem.GetDirectoryWriter().DeleteDirectory(home, recursive: true);
        real.FileSystem.GetDirectoryWriter().DeleteDirectory(localAppData, recursive: true);
    }

    private ISystemServices Simulated(UserDataFolder folder, string? localAppDataValue)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        if (localAppDataValue is not null)
        {
            variables[LocalAppData] = localAppDataValue;
        }

        SystemServicesBuilder builder = SystemServicesBuilder.Real()
            .SimulateFileSystem()
            .With(FakeEnvironment.Create(home: home, variables: variables));
        builder.OnPlatform().SimulateUserDataFolder(folder);
        return builder.Build();
    }
}
