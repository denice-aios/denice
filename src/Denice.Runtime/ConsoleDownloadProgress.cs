// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Globalization;
using Denice.Abstractions.Contracts;
using Denice.Updates;

namespace Denice.Runtime;

/// <summary>
/// Shows an update download's progress on the console as "Downloading: 12.0 MB of 40.0 MB", one line per tenth of the file,
/// so a slow download is visibly moving without flooding the console.
/// </summary>
internal sealed class ConsoleDownloadProgress : IProgress<DownloadProgress>
{
    private const double Megabyte = 1024 * 1024;
    private readonly IConsole _console;
    private int _tenthsShown = -1;

    /// <summary>Initializes a new instance of the <see cref="ConsoleDownloadProgress"/> class.</summary>
    /// <param name="console">The console the lines go to.</param>
    internal ConsoleDownloadProgress(IConsole console) => _console = console;

    /// <inheritdoc />
    public void Report(DownloadProgress value)
    {
        int tenths = value.Total > 0 ? (int)Math.Min(10, value.Received * 10 / value.Total) : 0;
        if (tenths <= _tenthsShown)
        {
            return;
        }

        _tenthsShown = tenths;
        _console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Downloading: {value.Received / Megabyte:0.0} MB of {value.Total / Megabyte:0.0} MB"));
    }
}
