// Copyright (c) Denice.ai. All rights reserved.

using System.IO;

namespace Denice.Tests.Shared;

/// <summary>
/// The machine-and-project on-disk layout the guard writes, derived here — independently of the production path
/// helpers (<c>MachinePaths</c>, <c>ProjectPaths</c>) — so the tests cross-check the real layout rather than trust
/// it. It is the single owner of that derivation shared by the setup and CLI test harnesses, so the two never
/// re-spell the same paths. The source file is linked into each test project that needs it.
/// </summary>
internal sealed class DeniceLayout
{
    /// <summary>Initializes the layout for an isolated home and project directory.</summary>
    /// <param name="home">The isolated HOME directory the machine install lives under.</param>
    /// <param name="project">The isolated project root the wiring is written under.</param>
    public DeniceLayout(string home, string project)
    {
        Home = home;
        Project = project;
    }

    /// <summary>Gets the isolated HOME directory the machine install lives under.</summary>
    public string Home { get; }

    /// <summary>Gets the isolated project root the wiring is written under.</summary>
    public string Project { get; }

    /// <summary>Gets the machine install root, <c>{Home}/.denice</c>.</summary>
    public string DeniceRoot => Path.Combine(Home, ".denice");

    /// <summary>Gets the stable launcher, <c>{Home}/.denice/bin/denice</c>.</summary>
    public string BinGuard => Path.Combine(DeniceRoot, "bin", "denice");

    /// <summary>Gets the machine state record, <c>{Home}/.denice/state.json</c>.</summary>
    public string MachineStateFile => Path.Combine(DeniceRoot, "state.json");

    /// <summary>Gets the <c>current</c> version symlink, <c>{Home}/.denice/current</c>.</summary>
    public string Current => Path.Combine(DeniceRoot, "current");

    /// <summary>Gets the binary reached through <c>current</c>, <c>{Home}/.denice/current/denice</c>.</summary>
    public string CurrentBinary => Path.Combine(Current, "denice");

    /// <summary>Gets the shell profile the PATH line is written to, <c>{Home}/.zshrc</c>.</summary>
    public string ShellProfilePath => Path.Combine(Home, ".zshrc");

    /// <summary>Gets the project's <c>.claude/settings.json</c> path.</summary>
    public string ClaudeSettings => Path.Combine(Project, ".claude", "settings.json");

    /// <summary>Gets the project's <c>.gitignore</c> path.</summary>
    public string Gitignore => Path.Combine(Project, ".gitignore");

    /// <summary>Gets the project's guard directory, <c>{Project}/.denice</c>.</summary>
    public string ProjectDenice => Path.Combine(Project, ".denice");

    /// <summary>Gets the project's config path, <c>{Project}/.denice/config.json</c>.</summary>
    public string ProjectConfig => Path.Combine(ProjectDenice, "config.json");

    /// <summary>Gets the project's state path, <c>{Project}/.denice/state.json</c>.</summary>
    public string ProjectStateFile => Path.Combine(ProjectDenice, "state.json");

    /// <summary>Gets the project's grants directory, <c>{Project}/.denice/grants</c>.</summary>
    public string ProjectGrants => Path.Combine(ProjectDenice, "grants");

    /// <summary>Returns a version directory's binary, <c>{Home}/.denice/versions/{version}/denice</c>.</summary>
    /// <param name="version">The normalized version.</param>
    /// <returns>The version binary path.</returns>
    public string VersionBinary(string version) => Path.Combine(DeniceRoot, "versions", version, "denice");
}
