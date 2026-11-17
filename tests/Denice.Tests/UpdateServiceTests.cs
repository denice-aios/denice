// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using Denice.Updates;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// Finding and installing a newer Denice from GitHub Releases: the settings switch and the interval, the channel, the
/// version order, this platform's release file, its <c>.zip.sha256</c>, and what is recorded as the last check. GitHub
/// is a fake handler (no network), the disk is simulated, the clock is fake, and the cosign check is stood in for; the
/// installer's own checks are proven in <see cref="VersionInstallerTests"/>.
/// </summary>
public sealed class UpdateServiceTests : IDisposable
{
    private const string Running = "0.1.200-pre-release+bbbbbbb";
    private const string Rid = "osx-arm64";
    private const string Downloads = "https://github.test/download/";

    private static readonly DateTimeOffset Start = new(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

    private readonly FakeGitHub github = new();
    private readonly FakeTimeProvider clock = new(Start);
    private readonly List<object> releases = [];
    private readonly ISystemServices services;
    private readonly string root;
    private readonly string versionDirectory;
    private UpdateService? service;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateServiceTests"/> class: a loader-started install of
    /// <see cref="Running"/> on a simulated disk, whose temporary install root is also the user's home.
    /// </summary>
    public UpdateServiceTests()
    {
        ISystemServices disk = SystemServicesBuilder.Real().SimulateFileSystem().Build();
        root = disk.FileSystem.GetDirectoryWriter().CreateTempSubdirectory("denice-update-");
        versionDirectory = Path.Combine(root, LoaderProtocol.VersionsDirectory, Running);
        services = Services(LoaderVariables(), entry: $"denice-v{Running}-{Rid}");
    }

    private static ReleaseTrust AcceptingCosign { get; } =
        new((_, _, _) => Task.FromResult<string?>(null), DeniceSigningCertificates.Sha256Fingerprints.ToArray());

    private UpdateService Service => service ??= new UpdateService(services, github, AcceptingCosign);

    [Fact]
    public async Task WhenUpdatesAreSwitchedOff_NothingIsChecked()
    {
        new SettingsFile(services).Set("updates.enabled", "false");

        UpdateOutcome outcome = await Service.CheckAndInstallAsync(CancellationToken.None);

        outcome.Should().Be(UpdateOutcome.Disabled);
        github.Requests.Should().BeEmpty();
        LastCheck().Should().BeNull();
    }

    [Fact]
    public async Task WithoutALoader_ThereIsNothingToUpdate()
    {
        var developerRun = new UpdateService(Services(new Dictionary<string, string>(StringComparer.Ordinal), entry: null), github, AcceptingCosign);
        using (developerRun)
        {
            UpdateOutcome outcome = await developerRun.CheckAndInstallAsync(CancellationToken.None);

            outcome.Should().Be(UpdateOutcome.NotInstalledByLoader);
        }

        github.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BeforeTheIntervalHasPassed_NothingIsChecked()
    {
        RecordLastCheck(Start - TimeSpan.FromHours(5));

        UpdateOutcome outcome = await Service.CheckAndInstallAsync(CancellationToken.None);

        outcome.Should().Be(UpdateOutcome.NotDue);
        github.Requests.Should().BeEmpty();
        LastCheck().Should().Be(Start - TimeSpan.FromHours(5));
    }

    [Theory]
    [InlineData(6, 6)]
    [InlineData(7, 6)]
    [InlineData(2, 1)]
    public async Task OnceTheIntervalHasPassed_ItChecksAndRecordsTheCheck(int hoursAgo, int intervalHours)
    {
        new SettingsFile(services).Set("updates.checkIntervalHours", intervalHours.ToString(System.Globalization.CultureInfo.InvariantCulture));
        RecordLastCheck(Start - TimeSpan.FromHours(hoursAgo));
        ServeReleases();

        UpdateOutcome outcome = await Service.CheckAndInstallAsync(CancellationToken.None);

        outcome.Should().Be(UpdateOutcome.NoNewerVersion);
        LastCheck().Should().Be(Start);
    }

    [Fact]
    public async Task ALastCheckInTheFuture_DoesNotStopChecks()
    {
        RecordLastCheck(Start + TimeSpan.FromDays(1));
        ServeReleases();

        (await Service.CheckAndInstallAsync(CancellationToken.None)).Should().Be(UpdateOutcome.NoNewerVersion);
        LastCheck().Should().Be(Start);
    }

    [Fact]
    public async Task AnUnreadableLastCheck_CountsAsNoCheck()
    {
        new LastUpdateCheck(services).Record(Start);
        services.FileSystem.GetFileWriter().WriteAllText(new LastUpdateCheck(services).FilePath, "yesterday");
        ServeReleases();

        (await Service.CheckAndInstallAsync(CancellationToken.None)).Should().Be(UpdateOutcome.NoNewerVersion);
    }

    [Fact]
    public void TheLastCheck_IsOneTimeInItsOwnFileBesideTheSettings()
    {
        var lastCheck = new LastUpdateCheck(services);
        lastCheck.Record(Start);

        Path.GetDirectoryName(lastCheck.FilePath).Should().Be(Path.GetDirectoryName(new SettingsFile(services).FilePath));
        Path.GetFileName(lastCheck.FilePath).Should().Be("last-update-check.txt");
        services.FileSystem.GetFileReader().ReadAllText(lastCheck.FilePath).Should().Be("2026-11-16T09:30:00.0000000+00:00\n");
        new SettingsFile(services).List().Select(setting => setting.Name).Should().NotContain(name => name.Contains("last", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheStableChannel_IgnoresPreReleases()
    {
        new SettingsFile(services).Set("updates.channel", "stable");
        AddRelease("0.1.300+ccccccc", prerelease: false);
        AddRelease("0.1.400-pre-release+ddddddd", prerelease: true);
        ServeReleases();

        UpdateOutcome outcome = await Service.CheckAndInstallAsync(CancellationToken.None);

        outcome.Should().Be(UpdateOutcome.Installed("0.1.300+ccccccc"));
        Installed("0.1.300+ccccccc").Should().BeTrue();
        github.WasRequested(ZipUrl("0.1.400-pre-release+ddddddd")).Should().BeFalse();
    }

    [Theory]
    [InlineData("0.1.300+ccccccc", "0.1.400-pre-release+ddddddd", "0.1.400-pre-release+ddddddd")]
    [InlineData("0.1.500+ccccccc", "0.1.400-pre-release+ddddddd", "0.1.500+ccccccc")]
    [InlineData("0.1.300+ccccccc", "0.1.300-pre-release+ddddddd", "0.1.300+ccccccc")]
    public async Task ThePreReleaseChannel_TakesTheHighestOfReleasesAndPreReleases(string release, string preRelease, string expected)
    {
        AddRelease(release, prerelease: false);
        AddRelease(preRelease, prerelease: true);
        ServeReleases();

        UpdateOutcome outcome = await Service.CheckAndInstallAsync(CancellationToken.None);

        outcome.Should().Be(UpdateOutcome.Installed(expected));
        Installed(expected).Should().BeTrue();
    }

    [Fact]
    public async Task OfTwoBuildsOfOneVersion_TheBuildHashBreaksTheTie()
    {
        AddRelease("0.1.300-pre-release+aaaaaaa", prerelease: true);
        AddRelease("0.1.300-pre-release+fffffff", prerelease: true);
        AddRelease("0.1.300-pre-release+ccccccc", prerelease: true);
        ServeReleases();

        (await Service.CheckAndInstallAsync(CancellationToken.None))
            .Should().Be(UpdateOutcome.Installed("0.1.300-pre-release+fffffff"));
    }

    [Fact]
    public async Task Drafts_AreSkipped()
    {
        AddRelease("0.1.900-pre-release+eeeeeee", prerelease: true, draft: true);
        AddRelease("0.1.300-pre-release+ccccccc", prerelease: true);
        ServeReleases();

        (await Service.CheckAndInstallAsync(CancellationToken.None))
            .Should().Be(UpdateOutcome.Installed("0.1.300-pre-release+ccccccc"));
        github.WasRequested(ZipUrl("0.1.900-pre-release+eeeeeee")).Should().BeFalse();
    }

    [Theory]
    [InlineData(Running)]
    [InlineData("0.1.200-pre-release+aaaaaaa")]
    [InlineData("0.1.100+fffffff")]
    public async Task WithNothingNewer_NothingIsDownloadedAndTheCheckIsRecorded(string highest)
    {
        AddRelease(highest, prerelease: true);
        ServeReleases();

        UpdateOutcome outcome = await Service.CheckAndInstallAsync(CancellationToken.None);

        outcome.Should().Be(UpdateOutcome.NoNewerVersion);
        github.Requests.Should().ContainSingle();
        LastCheck().Should().Be(Start);
    }

    [Fact]
    public async Task OnlyThisPlatformsReleaseFile_IsConsidered()
    {
        AddRelease("0.1.500-pre-release+eeeeeee", prerelease: true, rids: ["linux-x64", "win-x64"]);
        AddRelease("0.1.300-pre-release+ccccccc", prerelease: true, rids: ["linux-x64", Rid, "win-x64"]);
        ServeReleases();

        (await Service.CheckAndInstallAsync(CancellationToken.None))
            .Should().Be(UpdateOutcome.Installed("0.1.300-pre-release+ccccccc"));
        github.Requests.Select(request => request.RequestUri!.AbsoluteUri)
            .Should().NotContain(url => url.Contains("linux", StringComparison.Ordinal) || url.Contains("win", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnWindows_TheEntrysExeSuffixIsNotPartOfThePlatform()
    {
        using var windows = new UpdateService(Services(LoaderVariables(), entry: $"denice-v{Running}-win-x64.exe"), github, AcceptingCosign);
        AddRelease("0.1.300-pre-release+ccccccc", prerelease: true, rids: [Rid, "win-x64"]);
        ServeReleases();

        await windows.CheckAndInstallAsync(CancellationToken.None);

        github.WasRequested(ZipUrl("0.1.300-pre-release+ccccccc", "win-x64")).Should().BeTrue();
    }

    [Fact]
    public async Task AVersionFolderWithoutItsEntry_Fails()
    {
        using var broken = new UpdateService(Services(LoaderVariables(), entry: "something-else"), github, AcceptingCosign);
        ServeReleases();

        UpdateOutcome outcome = await broken.CheckAndInstallAsync(CancellationToken.None);

        outcome.Result.Should().Be(UpdateResult.Failed);
        outcome.Reason.Should().Contain("<rid> entry");
        github.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AReleaseWithoutItsZipSha256_FailsAndRecordsNothing()
    {
        AddRelease("0.1.300-pre-release+ccccccc", prerelease: true, withChecksum: false);
        ServeReleases();

        UpdateOutcome outcome = await Service.CheckAndInstallAsync(CancellationToken.None);

        outcome.Result.Should().Be(UpdateResult.Failed);
        outcome.Reason.Should().Contain("no .zip.sha256");
        NothingInstalledOrRecorded("0.1.300-pre-release+ccccccc");
    }

    [Fact]
    public async Task AZipThatDoesNotMatchItsSha256_FailsInstallsNothingAndRecordsNothing()
    {
        const string version = "0.1.300-pre-release+ccccccc";
        AddRelease(version, prerelease: true);
        ServeReleases();
        github.Answer(ZipUrl(version) + ".sha256", $"{new string('0', 64)}  denice-v{version}-{Rid}.zip\n");

        UpdateOutcome outcome = await Service.CheckAndInstallAsync(CancellationToken.None);

        outcome.Result.Should().Be(UpdateResult.Failed);
        outcome.Reason.Should().Contain("does not match its .zip.sha256");
        NothingInstalledOrRecorded(version);
    }

    [Fact]
    public async Task AReleaseTheInstallerRefuses_FailsAndRecordsNothing()
    {
        const string version = "0.1.300-pre-release+ccccccc";
        using var refusing = new UpdateService(
            services, github, new ReleaseTrust((_, _, _) => Task.FromResult<string?>("not Denice's release workflow"), []));
        AddRelease(version, prerelease: true);
        ServeReleases();

        UpdateOutcome outcome = await refusing.CheckAndInstallAsync(CancellationToken.None);

        outcome.Result.Should().Be(UpdateResult.Failed);
        outcome.Reason.Should().Contain("not Denice's release workflow");
        NothingInstalledOrRecorded(version);
    }

    [Fact]
    public async Task ASuccessfulDownload_IsInstalledAndTheCheckRecorded()
    {
        const string version = "0.1.300-pre-release+ccccccc";
        AddRelease(version, prerelease: true);
        ServeReleases();

        UpdateOutcome outcome = await Service.CheckAndInstallAsync(CancellationToken.None);

        outcome.Should().Be(UpdateOutcome.Installed(version));
        outcome.Version.Should().Be(version);
        Installed(version).Should().BeTrue();
        services.FileSystem.GetFileReader()
            .ReadAllText(Path.Combine(root, LoaderProtocol.VersionsDirectory, version, "Denice.Runtime.dll")).Should().Be("runtime");
        LastCheck().Should().Be(Start);
    }

    [Fact]
    public async Task EveryRequest_NamesDeniceAndItsVersion_AndTheListAsksForGitHubsJson()
    {
        AddRelease("0.1.300-pre-release+ccccccc", prerelease: true);
        ServeReleases();

        await Service.CheckAndInstallAsync(CancellationToken.None);

        github.Requests.Should().HaveCount(3).And.OnlyContain(request =>
            request.Method == HttpMethod.Get && request.Headers.UserAgent.ToString() == $"Denice/{Running}");
        HttpRequestMessage list = github.Requests[0];
        list.RequestUri!.AbsoluteUri.Should().Be("https://api.github.com/repos/denice-aios/denice/releases?per_page=100");
        list.Headers.Accept.ToString().Should().Be("application/vnd.github+json");
    }

    [Fact]
    public async Task AVersionAlreadyInstalled_IsReadyWithoutDownloadingItAgain()
    {
        const string version = "0.1.300-pre-release+ccccccc";
        services.FileSystem.GetDirectoryWriter().CreateDirectory(Path.Combine(root, LoaderProtocol.VersionsDirectory, version));
        AddRelease(version, prerelease: true);
        ServeReleases();

        (await Service.CheckAndInstallAsync(CancellationToken.None)).Should().Be(UpdateOutcome.Installed(version));
        github.WasRequested(ZipUrl(version)).Should().BeFalse();
        LastCheck().Should().Be(Start);
    }

    [Fact]
    public async Task ANetworkError_FailsAndRecordsNothing()
    {
        github.Answer(UpdateService.ReleasesUrl.AbsoluteUri, () => throw new HttpRequestException("No such host is known."));

        UpdateOutcome outcome = await Service.CheckAndInstallAsync(CancellationToken.None);

        outcome.Should().Be(UpdateOutcome.Failed("No such host is known."));
        LastCheck().Should().BeNull();
    }

    [Fact]
    public async Task ATimedOutRequest_Fails()
    {
        github.Answer(UpdateService.ReleasesUrl.AbsoluteUri, () => throw new TaskCanceledException("timed out"));

        UpdateOutcome outcome = await Service.CheckAndInstallAsync(CancellationToken.None);

        outcome.Should().Be(UpdateOutcome.Failed("the request to GitHub timed out"));
        LastCheck().Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnHttpErrorFromTheList_Fails(HttpStatusCode status)
    {
        github.Answer(UpdateService.ReleasesUrl.AbsoluteUri, () => new HttpResponseMessage(status));

        UpdateOutcome outcome = await Service.CheckAndInstallAsync(CancellationToken.None);

        outcome.Result.Should().Be(UpdateResult.Failed);
        outcome.Reason.Should().Contain($"GitHub answered {(int)status}");
        LastCheck().Should().BeNull();
    }

    [Fact]
    public async Task AnHttpErrorFromTheDownload_FailsAndRecordsNothing()
    {
        const string version = "0.1.300-pre-release+ccccccc";
        AddRelease(version, prerelease: true);
        ServeReleases();
        github.Answer(ZipUrl(version), () => new HttpResponseMessage(HttpStatusCode.BadGateway));

        UpdateOutcome outcome = await Service.CheckAndInstallAsync(CancellationToken.None);

        outcome.Reason.Should().Contain("GitHub answered 502");
        NothingInstalledOrRecorded(version);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"message\": \"Not Found\"}")]
    public async Task AListThatIsNotAListOfReleases_Fails(string body)
    {
        github.Answer(UpdateService.ReleasesUrl.AbsoluteUri, body);

        (await Service.CheckAndInstallAsync(CancellationToken.None)).Result.Should().Be(UpdateResult.Failed);
        LastCheck().Should().BeNull();
    }

    [Fact]
    public async Task AnInvalidSettingsFile_Fails()
    {
        string settings = new SettingsFile(services).FilePath;
        services.FileSystem.GetDirectoryWriter().CreateDirectory(Path.GetDirectoryName(settings)!);
        services.FileSystem.GetFileWriter().WriteAllText(settings, "updates.enabled: maybe\n");

        (await Service.CheckAndInstallAsync(CancellationToken.None)).Result.Should().Be(UpdateResult.Failed);
    }

    [Fact]
    public async Task Cancellation_PropagatesAndRecordsNothing()
    {
        const string version = "0.1.300-pre-release+ccccccc";
        AddRelease(version, prerelease: true);
        ServeReleases();
        using var cancel = new CancellationTokenSource();
        github.BeforeAnswer = request =>
        {
            if (request.RequestUri!.AbsoluteUri.EndsWith(".zip", StringComparison.Ordinal))
            {
                cancel.Cancel();
            }
        };

        Func<Task> act = () => Service.CheckAndInstallAsync(cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        NothingInstalledOrRecorded(version);
    }

    [Fact]
    public async Task Progress_ReportsTheBytesSoFarUpToTheWholeFile()
    {
        const string version = "0.1.300-pre-release+ccccccc";
        Dictionary<string, string> files = TestReleases.Files(version);
        files["large.bin"] = Incompressible(400_000);
        byte[] zip = TestReleases.Release(version, files);
        AddRelease(version, prerelease: true, zip: zip);
        ServeReleases();
        var progress = new RecordingProgress();

        UpdateOutcome outcome = await Service.CheckAndInstallAsync(progress, CancellationToken.None);

        outcome.Should().Be(UpdateOutcome.Installed(version));
        progress.Reports.Count.Should().BeGreaterThan(1);
        progress.Reports.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        progress.Reports[^1].Should().Be(zip.Length);
    }

    [Fact]
    public void TheNetworkService_BuildsAndDisposesWithoutTouchingTheNetwork()
    {
        Action build = () => new UpdateService(services).Dispose();

        build.Should().NotThrow();
    }

    [Fact]
    public void Constructor_RejectsMissingArguments()
    {
        Action noServices = () => _ = new UpdateService(null!);
        Action noTrust = () => _ = new UpdateService(services, null, null!);

        noServices.Should().Throw<ArgumentNullException>();
        noTrust.Should().Throw<ArgumentNullException>();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        service?.Dispose();
        github.Dispose();
    }

    private static string ZipUrl(string version, string rid = Rid) => $"{Downloads}{version}/denice-v{version}-{rid}.zip";

    private static Dictionary<string, object> Asset(string name, string url, long size) => new(StringComparer.Ordinal)
    {
        ["name"] = name,
        ["browser_download_url"] = url,
        ["size"] = size,
    };

    private static string Incompressible(int length)
    {
        var text = new StringBuilder(length);
        byte[] block = SHA256.HashData([1]);
        while (text.Length < length)
        {
            block = SHA256.HashData(block);
            text.Append(Convert.ToBase64String(block));
        }

        return text.ToString();
    }

    private ISystemServices Services(Dictionary<string, string> variables, string? entry)
    {
        variables["LOCALAPPDATA"] = root;
        ISystemServices built = SystemServicesBuilder.Real()
            .SimulateFileSystem()
            .With(FakeEnvironment.Create(home: root, variables: variables))
            .With(FixedBuildInfo.Create(Running))
            .With(clock)
            .Build();
        built.FileSystem.GetDirectoryWriter().CreateDirectory(root);
        built.FileSystem.GetDirectoryWriter().CreateDirectory(versionDirectory);
        if (entry is not null)
        {
            built.FileSystem.GetFileWriter().WriteAllText(Path.Combine(versionDirectory, entry), "entry");
        }

        return built;
    }

    private Dictionary<string, string> LoaderVariables() => new(StringComparer.Ordinal)
    {
        [LoaderProtocol.EnvInstallRoot] = root,
        [LoaderProtocol.EnvLoaderPath] = Path.Combine(root, "denice"),
        [LoaderProtocol.EnvLoaderVersion] = "0.1.100-pre-release+aaaaaaa",
        [LoaderProtocol.EnvVersionDirectory] = versionDirectory,
    };

    // One GitHub release (as the REST API lists it) with this version's file for each rid, the .zip.sha256 beside each
    // when asked, and the files GitHub always carries besides; the zip and its checksum are served at their URLs.
    private void AddRelease(
        string version, bool prerelease, bool draft = false, string[]? rids = null, bool withChecksum = true, byte[]? zip = null)
    {
        ArgumentNullException.ThrowIfNull(version);
        byte[] release = zip ?? TestReleases.Release(version, TestReleases.Files(version));
        var assets = new List<object> { Asset("evidence.zip", "https://github.test/download/evidence.zip", 1) };
        foreach (string rid in rids ?? [Rid])
        {
            string name = $"denice-v{version}-{rid}.zip";
            assets.Add(Asset(name, ZipUrl(version, rid), release.Length));
            github.Answer(ZipUrl(version, rid), release);
            if (withChecksum)
            {
                assets.Add(Asset(name + ".sha256", ZipUrl(version, rid) + ".sha256", 100));
                github.Answer(ZipUrl(version, rid) + ".sha256", $"{Convert.ToHexStringLower(SHA256.HashData(release))}  {name}\n");
            }
        }

        string tag = version.Split('+')[0];
        releases.Add(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["tag_name"] = tag,
            ["draft"] = draft,
            ["prerelease"] = prerelease,
            ["assets"] = assets,
        });
    }

    private void ServeReleases() => github.Answer(UpdateService.ReleasesUrl.AbsoluteUri, JsonSerializer.Serialize(releases));

    private void RecordLastCheck(DateTimeOffset when) => new LastUpdateCheck(services).Record(when);

    private DateTimeOffset? LastCheck() => new LastUpdateCheck(services).Read();

    private bool Installed(string version) =>
        services.FileSystem.GetDirectoryReader().DirectoryExists(Path.Combine(root, LoaderProtocol.VersionsDirectory, version));

    private void NothingInstalledOrRecorded(string version)
    {
        Installed(version).Should().BeFalse();
        services.FileSystem.GetDirectoryReader()
            .DirectoryExists(Path.Combine(root, LoaderProtocol.VersionsDirectory, version + LoaderProtocol.PartialSuffix))
            .Should().BeFalse();
        LastCheck().Should().BeNull();
    }

    private sealed class RecordingProgress : IProgress<long>
    {
        public List<long> Reports { get; } = [];

        public void Report(long value) => Reports.Add(value);
    }
}
