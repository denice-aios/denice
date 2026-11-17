// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
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
    public static CliResult Run(params string[] args) => Run(SystemServicesBuilder.Real(), args);

    /// <summary>
    /// Invokes the CLI with the given arguments in a fake environment whose home directory — and <c>LOCALAPPDATA</c>, so
    /// the per-user Denice folder is inside it on every OS — is <paramref name="home"/>, never the real user folder.
    /// </summary>
    /// <param name="home">The temporary folder that stands in for the user's home.</param>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The captured exit code and console output.</returns>
    public static CliResult RunInHome(string home, params string[] args) =>
        Run(
            SystemServicesBuilder.Real().With(FakeEnvironment.Create(
                home: home,
                variables: new Dictionary<string, string>(StringComparer.Ordinal) { ["LOCALAPPDATA"] = home })),
            args);

    private static CliResult Run(SystemServicesBuilder builder, string[] args)
    {
        var recorder = new RecordingConsole(string.Empty);
        ISystemServices services = builder.With(recorder.Console).Build();

        int exitCode = new Program(args).Run(services).GetAwaiter().GetResult();
        return new CliResult(exitCode, recorder.Output, recorder.Error);
    }
}
