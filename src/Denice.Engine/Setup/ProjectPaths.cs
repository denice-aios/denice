// Copyright (c) 4thWAIV. All rights reserved.

using System.IO;
using Denice.Engine;

namespace Denice.Setup;

/// <summary>
/// The single owner of the per-project wiring layout under a repository root. The runtime-store locations reuse
/// the hardcoded core-system constants so the paths the setup surface writes cannot drift from the paths the File
/// Guard protects.
/// </summary>
internal static class ProjectPaths
{
    /// <summary>
    /// Returns the project guard directory, <c>{root}/.denice</c>. The in-repo segment comes from its
    /// core-system owner, not from the machine-home root name, so the two are not conflated by a shared literal.
    /// </summary>
    /// <param name="context">The setup context.</param>
    /// <returns>The absolute project guard directory.</returns>
    internal static string DeniceDirectory(SetupContext context) =>
        CoreSystemPaths.Absolute(context.ProjectRoot, CoreSystemPaths.InRepoDirectoryName);

    /// <summary>
    /// Returns the project configuration file, <c>{root}/.denice/config.json</c>.
    /// </summary>
    /// <param name="context">The setup context.</param>
    /// <returns>The absolute configuration path.</returns>
    internal static string ConfigFile(SetupContext context) =>
        CoreSystemPaths.Absolute(context.ProjectRoot, CoreSystemPaths.ProjectConfigRelative);

    /// <summary>
    /// Returns the project state file, <c>{root}/.denice/state.json</c>.
    /// </summary>
    /// <param name="context">The setup context.</param>
    /// <returns>The absolute project state path.</returns>
    internal static string StateFile(SetupContext context) =>
        Path.Combine(DeniceDirectory(context), MachinePaths.StateFileName);

    /// <summary>
    /// Returns the signed-grant store directory, <c>{root}/.denice/grants</c>.
    /// </summary>
    /// <param name="context">The setup context.</param>
    /// <returns>The absolute grant store directory.</returns>
    internal static string GrantsDirectory(SetupContext context) =>
        CoreSystemPaths.Absolute(context.ProjectRoot, CoreSystemPaths.GrantStoreRelative);

    /// <summary>
    /// Returns the committed grant public-key file, <c>{root}/.denice/grant-public-key</c>.
    /// </summary>
    /// <param name="context">The setup context.</param>
    /// <returns>The absolute public-key path.</returns>
    internal static string PublicKeyFile(SetupContext context) =>
        CoreSystemPaths.Absolute(context.ProjectRoot, CoreSystemPaths.GrantPublicKeyRelative);

    /// <summary>
    /// Returns the Claude Code settings file, <c>{root}/.claude/settings.json</c>.
    /// </summary>
    /// <param name="context">The setup context.</param>
    /// <returns>The absolute Claude settings path.</returns>
    internal static string ClaudeSettingsFile(SetupContext context) =>
        CoreSystemPaths.Absolute(context.ProjectRoot, CoreSystemPaths.ClaudeSettingsRelative);

    /// <summary>
    /// Returns the repository ignore file, <c>{root}/.gitignore</c>.
    /// </summary>
    /// <param name="context">The setup context.</param>
    /// <returns>The absolute ignore-file path.</returns>
    internal static string GitignoreFile(SetupContext context) => Path.Combine(context.ProjectRoot, ".gitignore");

    /// <summary>
    /// Gets a value indicating whether the project has been initialized (its <c>.denice</c> directory exists).
    /// </summary>
    /// <param name="context">The setup context.</param>
    /// <returns><see langword="true"/> when the project is initialized.</returns>
    internal static bool IsInitialized(SetupContext context) => context.Directories.DirectoryExists(DeniceDirectory(context));
}
