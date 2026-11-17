// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Denice.Abstractions.Contracts;

namespace Denice.Updates;

/// <summary>
/// The last-known-good settings: <c>settings-history/</c> beside <c>settings.yaml</c>, a rolling list of up to
/// <see cref="Capacity"/> exact copies of <c>settings.yaml</c> text that loaded successfully. Tim, pkg-d4e90c3ce176
/// broken-settings: "Upon successful loads, the file is backed up (or checked to see if already is then added). A
/// rolling list is held (say 20) and we can revert back to the last setting that worked and was still close to user
/// preferene." Each copy is named <c>&lt;sequence&gt;-&lt;UTC time&gt;.yaml</c> (e.g.
/// <c>000042-20261117T093000Z.yaml</c>): the sequence number orders the copies even if the clock moves back, and the
/// time is when the copy was saved. Copies are written whole in one step, like the settings file.
/// </summary>
internal sealed class SettingsHistory
{
    /// <summary>The history folder's name, beside <c>settings.yaml</c> in the per-user Denice data folder.</summary>
    internal const string FolderName = "settings-history";

    /// <summary>How many copies the history keeps; a new copy beyond this removes the oldest.</summary>
    internal const int Capacity = 20;

    private const string Extension = ".yaml";
    private const string SavedFormat = "yyyyMMdd'T'HHmmss'Z'";
    private const string SequenceFormat = "D6";

    private readonly ISystemServices _services;

    /// <summary>Initializes a new instance of the <see cref="SettingsHistory"/> class.</summary>
    /// <param name="services">The owned services the clock and every file operation come from.</param>
    /// <param name="settingsFolder">The per-user Denice data folder <c>settings.yaml</c> is in.</param>
    internal SettingsHistory(ISystemServices services, string settingsFolder)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        FolderPath = Path.Combine(settingsFolder, FolderName);
    }

    /// <summary>Gets the history folder's absolute path.</summary>
    internal string FolderPath { get; }

    /// <summary>Lists the copies, newest first. Files in the folder that are not named as copies are ignored.</summary>
    /// <returns>Every copy, newest first; empty when the folder does not exist.</returns>
    internal IReadOnlyList<SettingsHistoryEntry> Entries()
    {
        IDirectoryEnumerator directories = _services.FileSystem.GetDirectoryReader();
        if (!directories.DirectoryExists(FolderPath))
        {
            return [];
        }

        return directories.EnumerateChildren(FolderPath)
            .Where(child => !child.IsDirectory)
            .Select(child => Parse(child.FullPath))
            .OfType<SettingsHistoryEntry>()
            .OrderByDescending(entry => entry.Sequence)
            .ToList();
    }

    /// <summary>
    /// Adds <paramref name="text"/> as the newest copy, unless the newest copy already holds exactly that text; then
    /// removes the oldest copies beyond <see cref="Capacity"/>.
    /// </summary>
    /// <param name="text">The exact text of a <c>settings.yaml</c> that just loaded successfully.</param>
    internal void Add(string text)
    {
        IReadOnlyList<SettingsHistoryEntry> entries = Entries();
        if (entries.Count > 0 && string.Equals(Read(entries[0]), text, StringComparison.Ordinal))
        {
            return;
        }

        long sequence = (entries.Count > 0 ? entries[0].Sequence : 0) + 1;
        DateTimeOffset saved = _services.Clock.GetUtcNow();
        string name = sequence.ToString(SequenceFormat, CultureInfo.InvariantCulture) + "-"
            + saved.UtcDateTime.ToString(SavedFormat, CultureInfo.InvariantCulture) + Extension;
        new AtomicTextFile(_services).Write(Path.Combine(FolderPath, name), text);

        IFileWriter writer = _services.FileSystem.GetFileWriter();
        foreach (SettingsHistoryEntry oldest in entries.Skip(Capacity - 1))
        {
            writer.DeleteFile(oldest.Path);
        }
    }

    /// <summary>Reads one copy's exact text.</summary>
    /// <param name="entry">The copy.</param>
    /// <returns>The copy's text.</returns>
    internal string Read(SettingsHistoryEntry entry) => _services.FileSystem.GetFileReader().ReadAllText(entry.Path);

    // A copy's sequence number and saved time from its name, or null when the file is not named as a copy.
    private static SettingsHistoryEntry? Parse(string path)
    {
        string name = Path.GetFileName(path);
        if (!name.EndsWith(Extension, StringComparison.Ordinal))
        {
            return null;
        }

        string[] parts = name[..^Extension.Length].Split('-');
        return parts.Length == 2
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long sequence)
            && DateTimeOffset.TryParseExact(
                parts[1], SavedFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset saved)
                ? new SettingsHistoryEntry(sequence, saved.ToUniversalTime(), path)
                : null;
    }
}
