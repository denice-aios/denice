// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Globalization;
using System.IO;
using Denice.Abstractions.Contracts;

namespace Denice.Updates;

/// <summary>
/// When Denice last completed an update check: <c>last-update-check.txt</c> beside <c>settings.yaml</c> in the per-user
/// Denice data folder, holding one round-trip UTC time (e.g. <c>2026-11-16T09:30:00.0000000+00:00</c>). It is state, not
/// a setting, so it lives in its own file and never appears among the settings. A missing or unreadable file means no
/// check has completed yet. It is written whole in one step, like the settings file.
/// </summary>
internal sealed class LastUpdateCheck
{
    /// <summary>The file's name inside the per-user Denice data folder.</summary>
    internal const string FileName = "last-update-check.txt";

    private readonly ISystemServices _services;

    /// <summary>Initializes a new instance of the <see cref="LastUpdateCheck"/> class for this user on this OS.</summary>
    /// <param name="services">The owned services the folder and every file operation come from.</param>
    /// <exception cref="InvalidOperationException">The per-user Denice data folder cannot be resolved.</exception>
    internal LastUpdateCheck(ISystemServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        FilePath = Path.Combine(SettingsFile.ResolveFolder(services), FileName);
    }

    /// <summary>Gets the file's absolute path.</summary>
    internal string FilePath { get; }

    /// <summary>Reads when the last check completed.</summary>
    /// <returns>The time, or <see langword="null"/> when no check has completed or the file cannot be read as one.</returns>
    internal DateTimeOffset? Read()
    {
        IFileReader reader = _services.FileSystem.GetFileReader();
        return reader.Exists(FilePath)
            && DateTimeOffset.TryParseExact(
                reader.ReadAllText(FilePath).Trim(), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset when)
                ? when
                : null;
    }

    /// <summary>Records that a check completed at <paramref name="when"/>.</summary>
    /// <param name="when">The time the check completed, from the injected clock.</param>
    internal void Record(DateTimeOffset when) =>
        new AtomicTextFile(_services).Write(FilePath, when.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "\n");
}
