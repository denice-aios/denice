// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Denice.Abstractions.Contracts;
using Denice.Engine;
using Denice.Updates;

namespace Denice.Runtime;

/// <summary>
/// Hosts the entry point for the Denice command-line interface. It is the single composition point: it builds the
/// one <see cref="ISystemServices"/> container with <see cref="SystemServices.Create"/> — the internal container
/// factory in <c>Denice.Engine</c>, reached through Engine's <c>InternalsVisibleTo</c> grant to <c>denice</c> and
/// the only Engine internal this assembly may reach (DN0041) — and threads it into every
/// handler, so console, environment, version, and clock access all run through the owned services. Its commands,
/// <c>version</c> and <c>settings</c> (with <c>set</c>, <c>reset</c>, <c>history</c>, and <c>restore</c>), are exposed
/// through System.CommandLine. A long-running command runs through <see cref="LongRunningHost"/> (D6), which checks for a
/// newer version every <c>updates.checkIntervalHours</c> while it runs and ends the session with the restart exit code
/// when one is ready; every other command is one-shot (D5).
/// </summary>
internal sealed class Program
{
    private const string UpdateCommandName = "update";

    private readonly string[] _args;
    private readonly IReadOnlyList<LongRunningCommand> _longRunningCommands;
    private readonly Func<ISystemServices, LongRunningHost>? _host;

    /// <summary>
    /// Initializes a new instance of the <see cref="Program"/> class holding the command-line arguments. It is
    /// <c>internal</c> (not private) so <c>Denice.Runtime.Tests</c> can construct it through the
    /// <c>InternalsVisibleTo</c> grant and drive <see cref="Run(ISystemServices)"/> with a test-built container
    /// (program-run-seam: an instantiable internal class; cli-tests-reach-run-via-ivt). <see cref="Main"/> is the
    /// only in-assembly caller.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    internal Program(string[] args)
        : this(args, [], null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Program"/> class with long-running commands and, optionally, the host
    /// they run in. Only <c>Denice.Runtime.Tests</c> calls it: Denice ships no long-running command yet, so the tests
    /// register their own to prove the host (D6) through <see cref="Run(ISystemServices)"/>.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="longRunningCommands">The long-running commands added to the root command.</param>
    /// <param name="host">Builds the host each long-running command runs in; null for the update checks over GitHub.</param>
    internal Program(string[] args, IReadOnlyList<LongRunningCommand> longRunningCommands, Func<ISystemServices, LongRunningHost>? host)
    {
        _args = args;
        _longRunningCommands = longRunningCommands;
        _host = host;
    }

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

        // Started by the loader: record that this version started, so the loader keeps it and removes older ones.
        LoaderHandshake.Read(services)?.RecordStarted();

        RootCommand root = BuildRootCommand(services);
        foreach (LongRunningCommand longRunning in _longRunningCommands)
        {
            root.Subcommands.Add(BuildLongRunningCommand(services, longRunning));
        }

        ParseResult parsed = root.Parse(_args);

        // The update check at every start runs in the background, never delaying the command. A one-shot command does not
        // wait for it: when the command ends the check stops and the next launch tries again (pkg-79dc8dd1e15d,
        // "retry-next-launch"); `denice update` checks in the foreground instead.
        using var stop = new CancellationTokenSource();
        Task background = IsUpdateCommand(parsed) ? Task.CompletedTask : Task.Run(() => StartCheckAsync(services, stop.Token), stop.Token);
        int exitCode = await parsed.InvokeAsync().ConfigureAwait(false);
        await stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await background.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped with the command; the next launch tries again.
        }

        return exitCode;
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
        root.Subcommands.Add(BuildSettingsCommand(services));
        root.Subcommands.Add(BuildUpdateCommand(services));
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

    /// <summary>
    /// Builds the <c>settings</c> command and its subcommands, over the per-user <c>settings.yaml</c> Denice owns:
    /// <c>denice settings</c> lists every setting with its current value and default; <c>denice settings set
    /// &lt;name&gt; &lt;value&gt;</c> changes one with validation; <c>denice settings reset &lt;name&gt;</c> returns it
    /// to the default; <c>denice settings history</c> lists the earlier settings that loaded, and <c>denice settings
    /// restore &lt;number&gt;</c> brings one back (Tim, pkg-d4e90c3ce176 broken-settings: "we can revert back to the last
    /// setting that worked and was still close to user preferene."). A refused name, value, version, or settings folder
    /// prints the reason on standard error and exits 1.
    /// </summary>
    /// <param name="services">The composition container the settings file and console are reached through.</param>
    /// <returns>The configured <c>settings</c> command.</returns>
    private static Command BuildSettingsCommand(ISystemServices services)
    {
        var command = new Command("settings", "List every setting with its current value and default.");
        command.SetAction(_ => RunSettings(services, settings =>
        {
            services.Console.WriteLine($"Settings file: {settings.FilePath}");
            foreach (SettingState state in settings.List())
            {
                services.Console.WriteLine($"{state.Name}: {state.Value} (default: {state.DefaultValue})");
            }
        }));

        var setName = new Argument<string>("name") { Description = "The setting to change." };
        var setValue = new Argument<string>("value") { Description = "The new value." };
        var set = new Command("set", "Change one setting; the value is checked before anything is written.");
        set.Arguments.Add(setName);
        set.Arguments.Add(setValue);
        set.SetAction(parse => RunSettings(services, settings =>
        {
            SettingState state = settings.Set(parse.GetValue(setName)!, parse.GetValue(setValue)!);
            services.Console.WriteLine($"{state.Name}: {state.Value} (saved to {settings.FilePath})");
        }));

        var resetName = new Argument<string>("name") { Description = "The setting to return to its default." };
        var reset = new Command("reset", "Return one setting to its default.");
        reset.Arguments.Add(resetName);
        reset.SetAction(parse => RunSettings(services, settings =>
        {
            SettingState state = settings.Reset(parse.GetValue(resetName)!);
            services.Console.WriteLine($"{state.Name}: {state.Value} (the default; saved to {settings.FilePath})");
        }));

        var history = new Command("history", "List the earlier settings that loaded, newest first, to restore one.");
        history.SetAction(_ => RunSettings(services, settings =>
        {
            services.Console.WriteLine($"Settings history for {settings.FilePath}, newest first (restore one with: denice settings restore <number>):");
            foreach (SettingsVersion version in settings.History())
            {
                services.Console.WriteLine($"{version.Number}  {version.SavedDescription}  {version.SettingsDescription}");
            }
        }));

        var restoreNumber = new Argument<int>("number") { Description = "The version to restore, as 'denice settings history' lists it; 0 is every default." };
        var restore = new Command("restore", "Make an earlier version of the settings the current settings.");
        restore.Arguments.Add(restoreNumber);
        restore.SetAction(parse => RunSettings(services, settings =>
        {
            SettingsVersion version = settings.Restore(parse.GetValue(restoreNumber));
            services.Console.WriteLine($"Restored settings {version.Description} to {settings.FilePath}");
        }));

        command.Subcommands.Add(set);
        command.Subcommands.Add(reset);
        command.Subcommands.Add(history);
        command.Subcommands.Add(restore);
        return command;
    }

    private static bool IsUpdateCommand(ParseResult parsed) =>
        string.Equals(parsed.CommandResult.Command.Name, UpdateCommandName, StringComparison.Ordinal);

    private static async Task StartCheckAsync(ISystemServices services, CancellationToken ct)
    {
        using var updates = new UpdateService(services);
        _ = await updates.CheckAndInstallAsync(UpdateRequest.AtStart, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the <c>update</c> command: checks now, whatever the off switch says (Tim, pkg-79dc8dd1e15d:
    /// "We'll also add an "update" command to allow the user to manually override."), shows the download's progress, and
    /// installs a newer version for the next launch. It does not restart this command (pkg-189647304b25, "finish once").
    /// </summary>
    /// <param name="services">The composition container.</param>
    /// <returns>The configured <c>update</c> command.</returns>
    private static Command BuildUpdateCommand(ISystemServices services)
    {
        var command = new Command(UpdateCommandName, "Check for a newer Denice now and install it; it starts the next time you run Denice.");
        command.SetAction((_, ct) => UpdateNowAsync(services, ct));
        return command;
    }

    private static async Task<int> UpdateNowAsync(ISystemServices services, CancellationToken ct)
    {
        using var updates = new UpdateService(services);
        var progress = new ConsoleDownloadProgress(services.Console);
        UpdateOutcome outcome = await updates.CheckAndInstallAsync(UpdateRequest.Manual, progress, ct).ConfigureAwait(false);
        switch (outcome.Result)
        {
            case UpdateResult.Installed:
                services.Console.WriteLine($"Denice {outcome.Version} is installed. It starts the next time you run Denice.");
                return 0;
            case UpdateResult.NoNewerVersion:
                services.Console.WriteLine($"Denice {services.BuildInfo.SemVer} is the newest version on your update channel.");
                return 0;
            case UpdateResult.NotInstalledByLoader:
                services.Console.ErrorWriteLine("This Denice was not started by its loader, so there is nothing to update.");
                return 1;
            default:
                services.Console.ErrorWriteLine($"The update failed: {outcome.Reason}");
                return 1;
        }
    }

    // Runs one settings action over the user's settings file. A refusal the user can fix (an unknown name, an invalid
    // value, a version that does not exist, an unresolvable or unwritable folder) is printed on standard error with exit
    // code 1 instead of a stack trace.
    private static int RunSettings(ISystemServices services, Action<SettingsFile> action)
    {
        try
        {
            action(new SettingsFile(services));
            return 0;
        }
        catch (Exception exception) when (exception is InvalidSettingException or InvalidOperationException or IOException
            or UnauthorizedAccessException)
        {
            services.Console.ErrorWriteLine(exception.Message);
            return 1;
        }
    }

    /// <summary>
    /// Makes <paramref name="longRunning"/> a long-running command: its action runs through <see cref="LongRunningHost"/>,
    /// which checks for a newer version every <c>updates.checkIntervalHours</c> while it runs (when updates are on), stops
    /// it once a newer version is installed and ends the session with the restart exit code (D6); when it ends first, its
    /// own exit code wins. This is the one way a command declares itself long-running.
    /// </summary>
    /// <param name="services">The composition container.</param>
    /// <param name="longRunning">The command and what it runs.</param>
    /// <returns>The command, with its action set.</returns>
    private Command BuildLongRunningCommand(ISystemServices services, LongRunningCommand longRunning)
    {
        longRunning.Command.SetAction((parse, ct) => RunLongRunningAsync(services, token => longRunning.Run(parse, token), ct));
        return longRunning.Command;
    }

    private async Task<int> RunLongRunningAsync(ISystemServices services, Func<CancellationToken, Task<int>> run, CancellationToken ct)
    {
        if (_host is not null)
        {
            return await _host(services).RunAsync(run, ct).ConfigureAwait(false);
        }

        using var updates = new UpdateService(services);
        return await new LongRunningHost(new UpdateLoop(services, updates)).RunAsync(run, ct).ConfigureAwait(false);
    }
}
