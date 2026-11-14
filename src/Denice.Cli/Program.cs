// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Denice.Abstractions.Contracts;
using Denice.Engine;

namespace Denice.Cli;

/// <summary>
/// Hosts the entry point for the Denice command-line interface. It is the single composition point: it builds the
/// one <see cref="ISystemServices"/> container with <see cref="SystemServices.Create"/> — the internal container
/// factory in <c>Denice.Engine</c>, reached through Engine's <c>InternalsVisibleTo</c> grant to <c>denice</c> and
/// the only Engine internal this assembly may reach (DN0041) — and threads it into every
/// handler, so console, environment, version, and clock access all run through the owned services. Its one command,
/// <c>version</c>, is exposed through System.CommandLine.
/// </summary>
internal sealed class Program
{
    private readonly string[] _args;

    /// <summary>
    /// Initializes a new instance of the <see cref="Program"/> class holding the command-line arguments. It is
    /// <c>internal</c> (not private) so <c>Denice.Cli.Tests</c> can construct it through the
    /// <c>InternalsVisibleTo</c> grant and drive <see cref="Run(ISystemServices)"/> with a test-built container
    /// (program-run-seam: an instantiable internal class; cli-tests-reach-run-via-ivt). <see cref="Main"/> is the
    /// only in-assembly caller.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    internal Program(string[] args) => _args = args;

    /// <summary>
    /// Parses the arguments and invokes the matched command, threading the given container into every handler. This is
    /// the seam that owns the real work: production passes the real container from <see cref="Main"/>, and a test passes
    /// a container built by the test <c>SystemServicesBuilder</c> whose fake environment points at a test directory.
    /// </summary>
    /// <param name="services">The OS/CLR service container every handler draws its owned services from.</param>
    /// <returns>The process exit code.</returns>
    internal async Task<int> Run(ISystemServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        RootCommand root = BuildRootCommand(services);
        return await root.Parse(_args).InvokeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the real service container and runs the CLI. The static entry point owns only the composition — building
    /// the one <see cref="ISystemServices"/> container with <see cref="SystemServices.Create"/> — and delegates every
    /// bit of real work to <see cref="Run(ISystemServices)"/>, which a test drives with a container built by the test
    /// <c>SystemServicesBuilder</c>, so <see cref="Main"/> itself is never under test.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    private static Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return new Program(args).Run(SystemServices.Create());
    }

    private static RootCommand BuildRootCommand(ISystemServices services)
    {
        var root = new RootCommand("Denice.");
        root.Subcommands.Add(BuildVersionCommand(services));
        return root;
    }

    /// <summary>
    /// Builds the <c>version</c> command, which prints the three versions this binary was built with
    /// (decision 18): the SemVer (informational), the AssemblyVersion, and the FileVersion.
    /// </summary>
    /// <param name="services">The composition container the version and console owners are pulled off.</param>
    /// <returns>The configured <c>version</c> command.</returns>
    private static Command BuildVersionCommand(ISystemServices services)
    {
        var command = new Command("version", "Print the SemVer, AssemblyVersion, and FileVersion this binary was built with.");
        command.SetAction(_ =>
        {
            IBuildInfo buildInfo = services.BuildInfo;
            services.Console.WriteLine($"SemVer: {buildInfo.SemVer}");
            services.Console.WriteLine($"AssemblyVersion: {buildInfo.AssemblyVersion}");
            services.Console.WriteLine($"FileVersion: {buildInfo.FileVersion}");
        });
        return command;
    }
}
