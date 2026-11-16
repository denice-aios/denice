// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.TestHelpers;

namespace Denice.Runtime.Tests;

/// <summary>
/// Runs the CLI through the internal <c>Program.Run(ISystemServices)</c> seam with the real production services except
/// the console, which is recorded, so a test reads exactly what a command printed and its exit code.
/// </summary>
internal static class CliRunner
{
    /// <summary>
    /// Invokes the CLI with the given arguments and empty standard input.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The captured exit code and console output.</returns>
    public static CliResult Run(params string[] args)
    {
        var recorder = new RecordingConsole(string.Empty);
        ISystemServices services = SystemServicesBuilder.Real()
            .With(recorder.Console)
            .Build();

        int exitCode = new Program(args).Run(services).GetAwaiter().GetResult();
        return new CliResult(exitCode, recorder.Output, recorder.Error);
    }
}
