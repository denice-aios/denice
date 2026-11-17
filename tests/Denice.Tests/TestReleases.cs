// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Denice.Tests;

/// <summary>
/// Builds Denice release files in memory, laid out as CI lays them out: the loader at the top and one
/// <c>versions/&lt;version&gt;/</c> folder holding the version's files, its <c>SHA256SUMS</c> and the cosign bundle.
/// </summary>
internal static class TestReleases
{
    private const int UnixExecutable = 0b111_101_101 << 16;

    /// <summary>A version's files, by path relative to its version folder.</summary>
    /// <param name="version">The version the files belong to (it names the entry).</param>
    /// <returns>The files.</returns>
    internal static Dictionary<string, string> Files(string version) => new(StringComparer.Ordinal)
    {
        ["Denice.Runtime.dll"] = "runtime",
        ["Denice.Updates.dll"] = "updates",
        [$"denice-v{version}-osx-arm64"] = "entry",
        ["de/System.CommandLine.resources.dll"] = "german",
    };

    /// <summary>The <c>SHA256SUMS</c> text listing <paramref name="files"/>.</summary>
    /// <param name="files">The files to list.</param>
    /// <returns>The checksums text.</returns>
    internal static string Sums(Dictionary<string, string> files) => string.Concat(
        files.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            $"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Value)))}  {pair.Key}\n"));

    /// <summary>A release file whose checksums list exactly its files.</summary>
    /// <param name="version">The version it carries.</param>
    /// <param name="files">The version's files.</param>
    /// <returns>The release file's bytes.</returns>
    internal static byte[] Release(string version, Dictionary<string, string> files) => Release(version, files, Sums(files));

    /// <summary>A release file with the given checksums text, or none.</summary>
    /// <param name="version">The version it carries.</param>
    /// <param name="files">The version's files.</param>
    /// <param name="sums">The <c>SHA256SUMS</c> text, or null for a release without one (and without a cosign bundle).</param>
    /// <returns>The release file's bytes.</returns>
    internal static byte[] Release(string version, Dictionary<string, string> files, string? sums)
    {
        var entries = files.ToDictionary(pair => $"versions/{version}/{pair.Key}", pair => pair.Value, StringComparer.Ordinal);
        if (sums is not null)
        {
            entries[$"versions/{version}/SHA256SUMS"] = sums;
            entries[$"versions/{version}/SHA256SUMS.cosign.bundle"] = "{}";
        }

        entries["denice"] = "loader";
        return Zip(entries);
    }

    /// <summary>A zip of the given entries; entries named like a versioned entry are marked executable.</summary>
    /// <param name="entries">The entries' text, by path.</param>
    /// <returns>The zip's bytes.</returns>
    internal static byte[] Zip(Dictionary<string, string> entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (KeyValuePair<string, string> pair in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(pair.Key);
                if (pair.Key.Contains("denice-v", StringComparison.Ordinal))
                {
                    entry.ExternalAttributes = UnixExecutable;
                }

                using Stream content = entry.Open();
                content.Write(Encoding.UTF8.GetBytes(pair.Value));
            }
        }

        return stream.ToArray();
    }
}
