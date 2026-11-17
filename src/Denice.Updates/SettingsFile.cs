// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Denice.Abstractions.Contracts;

namespace Denice.Updates;

/// <summary>
/// The settings file Denice owns and changes through Denice: <c>settings.yaml</c> in the per-user Denice data folder
/// (Windows <c>%LOCALAPPDATA%\Denice\</c>, macOS <c>~/Library/Application Support/Denice/</c>, Linux
/// <c>~/.config/denice/</c>), outside the install folder so updates never touch it. A missing file means every default.
/// The file holds only the settings that were set and not reset; <see cref="Set"/> and <see cref="Reset"/> rewrite it
/// whole through a temporary file in the same folder that then replaces it, so a crash never leaves half a file.
/// </summary>
public sealed class SettingsFile
{
    /// <summary>The settings file's name inside the per-user Denice data folder.</summary>
    public const string FileName = "settings.yaml";

    private readonly ISystemServices _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsFile"/> class, resolving where the file lives for this user
    /// on this OS.
    /// </summary>
    /// <param name="services">The owned services the folder, the environment, and every file operation come from.</param>
    /// <exception cref="InvalidOperationException">The folder's base cannot be resolved (on Windows,
    /// <c>LOCALAPPDATA</c> is not set to an absolute path).</exception>
    public SettingsFile(ISystemServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        FilePath = Path.Combine(ResolveFolder(services), FileName);
    }

    /// <summary>Gets the settings file's absolute path.</summary>
    public string FilePath { get; }

    /// <summary>Lists every setting with its current value and its default, in a fixed order.</summary>
    /// <returns>Every setting.</returns>
    /// <exception cref="InvalidDataException">The settings file is not valid; the message names the line.</exception>
    public IReadOnlyList<SettingState> List()
    {
        IReadOnlyDictionary<string, string> set = ReadSetValues();
        return SettingDefinition.All.Select(setting => State(setting, set)).ToList();
    }

    /// <summary>Reads the typed update settings.</summary>
    /// <returns>The update settings, each set value or else its default.</returns>
    /// <exception cref="InvalidDataException">The settings file is not valid; the message names the line.</exception>
    public UpdateSettings ReadUpdateSettings() => UpdateSettings.From(ReadSetValues());

    /// <summary>Changes one setting, after checking the name and the value.</summary>
    /// <param name="name">The setting's name.</param>
    /// <param name="value">The new value, exactly as the setting accepts it.</param>
    /// <returns>The setting as it now is.</returns>
    /// <exception cref="InvalidSettingException">The name is not a setting or the value is not valid for it; nothing was
    /// written.</exception>
    /// <exception cref="InvalidDataException">The existing settings file is not valid; nothing was written.</exception>
    public SettingState Set(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        SettingDefinition setting = Require(name);
        if (!setting.Accepts(value))
        {
            throw new InvalidSettingException(setting.InvalidValueProblem(value) + ".");
        }

        var set = new Dictionary<string, string>(ReadSetValues(), StringComparer.Ordinal) { [setting.Name] = value };
        Write(set);
        return State(setting, set);
    }

    /// <summary>Returns one setting to its default by removing it from the file.</summary>
    /// <param name="name">The setting's name.</param>
    /// <returns>The setting as it now is.</returns>
    /// <exception cref="InvalidSettingException">The name is not a setting; nothing was written.</exception>
    /// <exception cref="InvalidDataException">The existing settings file is not valid; nothing was written.</exception>
    public SettingState Reset(string name)
    {
        SettingDefinition setting = Require(name);
        var set = new Dictionary<string, string>(ReadSetValues(), StringComparer.Ordinal);
        set.Remove(setting.Name);
        Write(set);
        return State(setting, set);
    }

    /// <summary>
    /// Resolves the per-user Denice data folder: the platform says where it is on this OS (DN0009 keeps the OS knowledge
    /// in the platform layer), and the base it names is read through the owned environment.
    /// </summary>
    /// <param name="services">The owned services the folder and the environment come from.</param>
    /// <returns>The per-user Denice data folder's absolute path.</returns>
    /// <exception cref="InvalidOperationException">The folder's base cannot be resolved.</exception>
    internal static string ResolveFolder(ISystemServices services)
    {
        UserDataFolder folder = services.Platform.FileSystem.UserDataFolder;
        if (folder.EnvironmentVariable is null)
        {
            return Path.Combine(services.Environment.GetHomeDirectory(), folder.RelativePath);
        }

        string? root = services.Environment.GetEnvironmentVariable(folder.EnvironmentVariable);
        if (string.IsNullOrEmpty(root) || !Path.IsPathRooted(root))
        {
            throw new InvalidOperationException(
                $"Denice keeps its settings under the folder the environment variable {folder.EnvironmentVariable} names, but it is not set to an absolute path.");
        }

        return Path.Combine(root, folder.RelativePath);
    }

    private static SettingDefinition Require(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return SettingDefinition.Find(name) ?? throw new InvalidSettingException(SettingDefinition.UnknownNameProblem(name) + ".");
    }

    private static SettingState State(SettingDefinition setting, IReadOnlyDictionary<string, string> set) =>
        new(setting.Name, set.TryGetValue(setting.Name, out string? value) ? value : setting.DefaultValue, setting.DefaultValue);

    private IReadOnlyDictionary<string, string> ReadSetValues()
    {
        IFileReader reader = _services.FileSystem.GetFileReader();
        return reader.Exists(FilePath)
            ? SettingsYaml.Parse(reader.ReadAllText(FilePath), FilePath)
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    // Rewrites the whole file in one step (AtomicTextFile), so a crash never leaves half a file.
    private void Write(IReadOnlyDictionary<string, string> set) =>
        new AtomicTextFile(_services).Write(FilePath, SettingsYaml.Format(set));
}
