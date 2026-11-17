// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Denice.Abstractions.Contracts;

namespace Denice.Updates;

/// <summary>
/// Finds and installs a newer Denice from the GitHub Releases of denice-aios/denice (Tim: "Source: GitHub Releases of
/// denice-aios/denice. Channel set in Denice's Settings"; "The .NET DLL performs the downloading"). One call checks once:
/// it honours the off switch, picks the highest version on the channel, downloads this
/// platform's release file, checks it against its <c>.zip.sha256</c>, and hands it to <see cref="VersionInstaller"/>,
/// which does the release's own checks and the one-step install. When a check runs is the caller's decision.
/// </summary>
public sealed class UpdateService : IDisposable
{
    /// <summary>GitHub's list of Denice releases, newest first.</summary>
    internal static readonly Uri ReleasesUrl = new("https://api.github.com/repos/denice-aios/denice/releases?per_page=100");

    private const int ChunkBytes = 81920;

    private readonly ISystemServices _services;
    private readonly HttpClient _http;
    private readonly ReleaseTrust _trust;

    /// <summary>Initializes a new instance of the <see cref="UpdateService"/> class over the network.</summary>
    /// <param name="services">The owned services the settings, the clock, the version and every file come from.</param>
    public UpdateService(ISystemServices services)
        : this(services, null, ReleaseTrust.Denice)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateService"/> class over the given HTTP handler and release trust
    /// (tests stand in for GitHub and for the cosign check; production uses the network and <see cref="ReleaseTrust.Denice"/>).
    /// </summary>
    /// <param name="services">The owned services.</param>
    /// <param name="handler">The HTTP handler every request goes through, which the service disposes; null for the
    /// network.</param>
    /// <param name="trust">Who a release must come from.</param>
    internal UpdateService(ISystemServices services, HttpMessageHandler? handler, ReleaseTrust trust)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(trust);
        _services = services;
        _trust = trust;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
    }

    /// <summary>
    /// Checks once for a newer version on the chosen channel and installs it. Only a completed check (nothing newer, or
    /// installed) is recorded as the last check; a failed or cancelled one records nothing. The last check is kept for
    /// information only: it never holds a check back, because Denice checks at every start and the caller decides when
    /// to check while running.
    /// </summary>
    /// <param name="request">A check at start or while running honours the off switch; a manual one checks now.</param>
    /// <param name="progress">Receives the release file's bytes downloaded so far and its size, when given.</param>
    /// <param name="ct">Cancels the check; a cancelled install leaves no version behind.</param>
    /// <returns>What the check did. Network errors and refused downloads are <see cref="UpdateResult.Failed"/>.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public async Task<UpdateOutcome> CheckAndInstallAsync(
        UpdateRequest request, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        try
        {
            return await CheckAsync(request, progress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return UpdateOutcome.Failed("the request to GitHub timed out");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or JsonException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            return UpdateOutcome.Failed(e.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private async Task<UpdateOutcome> CheckAsync(UpdateRequest request, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        // A developer run (no loader) has nothing to update and never touches the user's settings.
        LoaderHandshake? handshake = LoaderHandshake.Read(_services);
        if (handshake is null)
        {
            return UpdateOutcome.NotInstalledByLoader;
        }

        bool manual = request == UpdateRequest.Manual;
        UpdateSettings settings = UpdateSettings.Read(_services);
        if (!manual && !settings.Enabled)
        {
            return UpdateOutcome.Disabled;
        }

        var lastCheck = new LastUpdateCheck(_services);
        string running = _services.BuildInfo.SemVer;
        DeniceVersion runningVersion = DeniceVersion.Parse(running)
            ?? throw new InvalidDataException($"The running version '{running}' is not a Denice version.");
        string rid = RuntimeIdentifier(handshake.VersionDirectory, running);
        string releases = await GetTextAsync(ReleasesUrl, "application/vnd.github+json", ct).ConfigureAwait(false);
        ReleaseCandidate? newest = ReleaseCandidate.Highest(releases, settings.Channel, rid);
        if (newest is null || DeniceVersion.Order.Compare(newest.Version, runningVersion) <= 0)
        {
            lastCheck.Record(_services.Clock.GetUtcNow());
            return UpdateOutcome.NoNewerVersion;
        }

        string version = newest.Version.Text;
        string installed = Path.Combine(handshake.InstallRoot, LoaderProtocol.VersionsDirectory, version);

        // A newer version the loader marked as failing to start is not offered again (it would restart every interval).
        if (_services.FileSystem.GetFileReader().Exists(Path.Combine(installed, LoaderProtocol.BadMarker)))
        {
            lastCheck.Record(_services.Clock.GetUtcNow());
            return UpdateOutcome.NoNewerVersion;
        }

        if (!_services.FileSystem.GetDirectoryReader().DirectoryExists(installed))
        {
            byte[] zip = await DownloadCheckedAsync(newest, progress, ct).ConfigureAwait(false);
            await new VersionInstaller(_services, handshake.InstallRoot, rid, _trust).InstallAsync(zip, ct).ConfigureAwait(false);
        }

        lastCheck.Record(_services.Clock.GetUtcNow());
        return UpdateOutcome.Installed(version);
    }

    // This version's entry is versions/<version>/denice-v<version>-<rid> (.exe on Windows); its name gives the rid.
    private string RuntimeIdentifier(string versionDirectory, string running)
    {
        string? rid = _services.FileSystem.GetDirectoryReader().EnumerateChildren(versionDirectory)
            .Where(child => !child.IsDirectory)
            .Select(child => LoaderProtocol.EntryRid(Path.GetFileName(child.FullPath), running))
            .FirstOrDefault(found => found is not null);
        return rid ?? throw new InvalidDataException(
            $"This version's folder '{versionDirectory}' has no {LoaderProtocol.EntryPrefix}{running}-<rid> entry.");
    }

    private async Task<byte[]> DownloadCheckedAsync(ReleaseCandidate release, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (release.Checksum is null)
        {
            throw new InvalidDataException($"Denice {release.Version} has no .zip.sha256 for its release file.");
        }

        string sums = await GetTextAsync(release.Checksum, "application/octet-stream", ct).ConfigureAwait(false);
        string expected = sums.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        byte[] zip = await DownloadAsync(release, progress, ct).ConfigureAwait(false);
        string actual = Convert.ToHexStringLower(SHA256.HashData(zip));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The downloaded release file of Denice {release.Version} does not match its .zip.sha256; it was not installed.");
        }

        return zip;
    }

    private async Task<byte[]> DownloadAsync(ReleaseCandidate release, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(release.Zip, "application/octet-stream", ct).ConfigureAwait(false);
        using var bytes = new MemoryStream((int)Math.Clamp(release.ZipSize, 0, int.MaxValue));
        Stream body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (body.ConfigureAwait(false))
        {
            byte[] chunk = new byte[ChunkBytes];
            int read;
            while ((read = await body.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                await bytes.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
                progress?.Report(new DownloadProgress(bytes.Length, release.ZipSize));
            }
        }

        return bytes.ToArray();
    }

    private async Task<string> GetTextAsync(Uri url, string accept, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(url, accept, ct).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    // GitHub's API asks for a User-Agent naming the client and for its JSON media type; release files are plain bytes.
    private async Task<HttpResponseMessage> SendAsync(Uri url, string accept, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Denice", _services.BuildInfo.SemVer));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string status = $"{(int)response.StatusCode} {response.ReasonPhrase}";
            response.Dispose();
            throw new HttpRequestException($"GitHub answered {status} for {url}.");
        }

        return response;
    }
}
