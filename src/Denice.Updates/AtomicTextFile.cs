// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.IO;
using Denice.Abstractions.Contracts;

namespace Denice.Updates;

/// <summary>
/// Writes a small text file whole in one step: the text goes to a temporary sibling in the same folder, which then
/// replaces the file. A move within one folder replaces the file at once, so a reader sees the old file or the new one,
/// never half of either. The temporary file is removed if anything fails before the move. Denice's per-user files
/// (<c>settings.yaml</c>, the last update check) are all written this way.
/// </summary>
internal sealed class AtomicTextFile
{
    private readonly ISystemServices _services;

    /// <summary>Initializes a new instance of the <see cref="AtomicTextFile"/> class.</summary>
    /// <param name="services">The owned services every file operation goes through.</param>
    internal AtomicTextFile(ISystemServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <summary>Replaces <paramref name="path"/> with <paramref name="contents"/>, creating its folder if needed.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="contents">The file's whole new text.</param>
    internal void Write(string path, string contents)
    {
        IFileWriter writer = _services.FileSystem.GetFileWriter();
        _services.FileSystem.GetDirectoryWriter().CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = _services.Random.TemporarySiblingPath(path);
        try
        {
            writer.WriteAllText(temporary, contents);
            writer.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (_services.FileSystem.GetFileReader().Exists(temporary))
            {
                writer.DeleteFile(temporary);
            }
        }
    }
}
