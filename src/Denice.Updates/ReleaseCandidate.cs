// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Denice.Updates;

/// <summary>
/// One GitHub release of Denice that carries this platform's release file, <c>denice-v&lt;version&gt;-&lt;rid&gt;.zip</c>,
/// with the <c>.zip.sha256</c> beside it when the release has one. The version comes from the file's name, because the
/// release's tag has no <c>+hash</c>.
/// </summary>
/// <param name="Version">The version the release file carries.</param>
/// <param name="Zip">Where the release file downloads from.</param>
/// <param name="ZipSize">The release file's size in bytes, as GitHub lists it.</param>
/// <param name="Checksum">Where the release file's <c>.zip.sha256</c> downloads from, or null when it has none.</param>
internal sealed record ReleaseCandidate(DeniceVersion Version, Uri Zip, long ZipSize, Uri? Checksum)
{
    private const string AssetPrefix = "denice-v";
    private const string ChecksumSuffix = ".sha256";

    private static readonly IComparer<ReleaseCandidate> ByVersion =
        Comparer<ReleaseCandidate>.Create((left, right) => DeniceVersion.Order.Compare(left?.Version, right?.Version));

    /// <summary>
    /// Picks the highest version on the channel from GitHub's list of releases (<c>GET /repos/{owner}/{repo}/releases</c>):
    /// drafts are skipped, the stable channel skips pre-releases, and a release without this platform's file is skipped.
    /// </summary>
    /// <param name="releasesJson">GitHub's JSON list of releases.</param>
    /// <param name="channel">The channel the settings choose.</param>
    /// <param name="rid">This platform's runtime identifier, e.g. <c>osx-arm64</c>.</param>
    /// <returns>The highest release on the channel, or null when there is none.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    /// <exception cref="InvalidOperationException">The JSON is not a list of releases.</exception>
    internal static ReleaseCandidate? Highest(string releasesJson, UpdateChannel channel, string rid)
    {
        using JsonDocument document = JsonDocument.Parse(releasesJson);
        return document.RootElement.EnumerateArray()
            .Where(release => !IsTrue(release, "draft")
                && (channel == UpdateChannel.PreRelease || !IsTrue(release, "prerelease")))
            .Select(release => FromRelease(release, rid))
            .OfType<ReleaseCandidate>()
            .Max(ByVersion);
    }

    private static bool IsTrue(JsonElement release, string property) =>
        release.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static ReleaseCandidate? FromRelease(JsonElement release, string rid)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string zipSuffix = "-" + rid + ".zip";
        var byName = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (asset.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String)
            {
                byName[name.GetString()!] = asset;
            }
        }

        return byName
            .Where(pair => pair.Key.StartsWith(AssetPrefix, StringComparison.Ordinal)
                && pair.Key.EndsWith(zipSuffix, StringComparison.Ordinal)
                && pair.Key.Length > AssetPrefix.Length + zipSuffix.Length)
            .Select(pair => Candidate(
                DeniceVersion.Parse(pair.Key[AssetPrefix.Length..^zipSuffix.Length]),
                pair.Value,
                byName.TryGetValue(pair.Key + ChecksumSuffix, out JsonElement checksum) ? checksum : null))
            .OfType<ReleaseCandidate>()
            .Max(ByVersion);
    }

    private static ReleaseCandidate? Candidate(DeniceVersion? version, JsonElement zip, JsonElement? checksum)
    {
        Uri? zipUrl = DownloadUrl(zip);
        if (version is null || zipUrl is null)
        {
            return null;
        }

        long size = zip.TryGetProperty("size", out JsonElement value) && value.TryGetInt64(out long bytes) ? bytes : 0;
        return new ReleaseCandidate(version, zipUrl, size, checksum is { } sums ? DownloadUrl(sums) : null);
    }

    private static Uri? DownloadUrl(JsonElement asset) =>
        asset.TryGetProperty("browser_download_url", out JsonElement url) && url.ValueKind == JsonValueKind.String
            && Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                ? uri
                : null;
}
