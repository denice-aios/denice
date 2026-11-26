// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Denice.Abstractions.Contracts;

namespace Denice.Updates;

/// <summary>
/// The settings file Denice owns and changes through Denice: <c>settings.yaml</c> in the per-user Denice data folder
/// (Windows <c>%LOCALAPPDATA%\Denice\</c>, macOS <c>~/Library/Application Support/Denice/</c>, Linux
/// <c>~/.config/denice/</c>), outside the install folder so updates never touch it. A missing file means every default.
/// The file holds only the settings that were set and not reset; <see cref="Set"/> and <see cref="Reset"/> rewrite it
/// whole through a temporary file in the same folder that then replaces it, so a crash never leaves half a file.
/// <para>
/// A broken file is fixed through Denice, never by hand (rule 7, "Nothing ships in this project unless it can be fixed in
/// Settings."). Tim, pkg-d4e90c3ce176 broken-settings: "Restore from last known good. If last known good is original
/// (reset) then that is just version 0. Upon successful loads, the file is backed up (or checked to see if already is
/// then added). A rolling list is held (say 20) and we can revert back to the last setting that worked and was still
/// close to user preferene." So every successful load adds the file's text to the <see cref="SettingsHistory"/>; a file
/// that cannot be read as valid is kept as <c>settings.yaml.broken</c> and replaced by the newest saved copy (or by
/// version 0, every default, when there is none), with one line on standard error saying so; and
/// <see cref="History"/> and <see cref="Restore"/> bring back any saved version by choice.
/// </para>
/// </summary>
public sealed class SettingsFile
{
    /// <summary>The settings file's name inside the per-user Denice data folder.</summary>
    public const string FileName = "settings.yaml";

    private const string BrokenSuffix = ".broken";

    // Loading may restore a broken file and adds to the history, so the update check at start (in the background) and
    // the command in the foreground take turns: a broken file is restored once and its broken copy is never replaced by
    // the restored text.
    private static readonly Lock Gate = new();

    private readonly ISystemServices _services;
    private readonly SettingsHistory _history;

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
        string folder = ResolveFolder(services);
        FilePath = Path.Combine(folder, FileName);
        _history = new SettingsHistory(services, folder);
    }

    /// <summary>Gets the settings file's absolute path.</summary>
    public string FilePath { get; }

    /// <summary>Gets where a broken settings file is kept when it is replaced: <c>settings.yaml.broken</c> beside it.</summary>
    public string BrokenFilePath => FilePath + BrokenSuffix;

    /// <summary>Lists every setting with its current value and its default, in a fixed order.</summary>
    /// <returns>Every setting.</returns>
    public IReadOnlyList<SettingState> List()
    {
        IReadOnlyDictionary<string, string> set = Locked(Load);
        return SettingDefinition.All.Select(setting => State(setting, set)).ToList();
    }

    /// <summary>Resolves the selected browser profile beneath DENICE's per-user settings folder.</summary>
    /// <returns>The absolute Chromium root cache directory.</returns>
    public string ReadBrowserCachePath()
    {
        IReadOnlyDictionary<string, string> values = Locked(Load);
        string profile = values.TryGetValue(SettingDefinition.BrowserProfileName, out string? selected)
            ? selected
            : SettingDefinition.BrowserProfileDefault;
        return Path.Combine(Path.GetDirectoryName(FilePath)!, "Browser", profile);
    }

    /// <summary>Reads the optional localhost browser debugging port, disabled by default.</summary>
    /// <returns>Zero to disable debugging, or the selected port.</returns>
    public int ReadBrowserDebugPort()
    {
        IReadOnlyDictionary<string, string> values = Locked(Load);
        return values.TryGetValue(SettingDefinition.BrowserDebugPortName, out string? port)
            ? int.Parse(port, CultureInfo.InvariantCulture)
            : 0;
    }

    /// <summary>Reads the typed update settings.</summary>
    /// <returns>The update settings, each set value or else its default.</returns>
    public UpdateSettings ReadUpdateSettings() => UpdateSettings.From(Locked(Load));

    /// <summary>Changes one setting, after checking the name and the value.</summary>
    /// <param name="name">The setting's name.</param>
    /// <param name="value">The new value, exactly as the setting accepts it.</param>
    /// <returns>The setting as it now is.</returns>
    /// <exception cref="InvalidSettingException">The name is not a setting or the value is not valid for it; nothing was
    /// written.</exception>
    public SettingState Set(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        SettingDefinition setting = Require(name);
        if (!setting.Accepts(value))
        {
            throw new InvalidSettingException(setting.InvalidValueProblem(value) + ".");
        }

        return Locked(() =>
        {
            var set = new Dictionary<string, string>(Load(), StringComparer.Ordinal) { [setting.Name] = value };
            Write(set);
            return State(setting, set);
        });
    }

    /// <summary>Returns one setting to its default by removing it from the file.</summary>
    /// <param name="name">The setting's name.</param>
    /// <returns>The setting as it now is.</returns>
    /// <exception cref="InvalidSettingException">The name is not a setting; nothing was written.</exception>
    public SettingState Reset(string name)
    {
        SettingDefinition setting = Require(name);
        return Locked(() =>
        {
            var set = new Dictionary<string, string>(Load(), StringComparer.Ordinal);
            set.Remove(setting.Name);
            Write(set);
            return State(setting, set);
        });
    }

    /// <summary>
    /// Lists the settings versions that can be restored, after loading the current file (which adds it to the history):
    /// the saved copies newest first, numbered from 1, then version 0, every default.
    /// </summary>
    /// <returns>Every version, newest first, ending with version 0.</returns>
    public IReadOnlyList<SettingsVersion> History() =>
        Locked(() =>
        {
            Load();
            return Versions().Select(candidate => candidate.Version).ToList();
        });

    /// <summary>
    /// Makes one version from <see cref="History"/> the current settings file, after loading the current file (which
    /// adds it to the history, so it can be restored in turn). The restored text becomes the newest saved copy on its
    /// next successful load.
    /// </summary>
    /// <param name="number">The version's number, as <see cref="History"/> lists it; 0 is every default.</param>
    /// <returns>The version restored.</returns>
    /// <exception cref="InvalidSettingException">There is no version with that number; nothing was written.</exception>
    public SettingsVersion Restore(int number) =>
        Locked(() =>
        {
            Load();
            List<SettingsCandidate> versions = Versions();
            SettingsCandidate chosen = versions.FirstOrDefault(candidate => candidate.Version.Number == number)
                ?? throw new InvalidSettingException(
                    $"There is no settings version {number}; the versions are 0 to {versions.Count - 1} (see 'denice settings history').");
            new AtomicTextFile(_services).Write(FilePath, chosen.Text);
            return chosen.Version;
        });

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

    private static List<SettingState> SetStates(IReadOnlyDictionary<string, string> set) =>
        SettingDefinition.All.Where(setting => set.ContainsKey(setting.Name)).Select(setting => State(setting, set)).ToList();

    private static T Locked<T>(Func<T> action)
    {
        lock (Gate)
        {
            return action();
        }
    }

    // Reads the values the file sets. A file that loads is added to the history; a file that cannot be read as valid is
    // replaced by the last known good version. A missing file is every default and is not saved: that is version 0.
    private IReadOnlyDictionary<string, string> Load()
    {
        IFileReader reader = _services.FileSystem.GetFileReader();
        if (!reader.Exists(FilePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        string text = reader.ReadAllText(FilePath);
        IReadOnlyDictionary<string, string> set;
        try
        {
            set = SettingsYaml.Parse(text, FilePath);
        }
        catch (InvalidDataException broken)
        {
            return RestoreLastKnownGood(text, broken);
        }

        _history.Add(text);
        return set;
    }

    // Keeps the broken text as settings.yaml.broken (so nothing the user wrote is lost), puts the newest saved copy (or
    // version 0) in its place, and says so in one line on standard error.
    private IReadOnlyDictionary<string, string> RestoreLastKnownGood(string brokenText, InvalidDataException broken)
    {
        SettingsCandidate lastKnownGood = Versions()[0];
        var file = new AtomicTextFile(_services);
        file.Write(BrokenFilePath, brokenText);
        file.Write(FilePath, lastKnownGood.Text);
        _services.Console.ErrorWriteLine(
            $"The settings file was broken: {broken.Message} Denice restored settings {lastKnownGood.Version.Description}; the broken file is kept at {BrokenFilePath}.");
        return lastKnownGood.Values;
    }

    // Every version that can be restored, newest first: each saved copy that still reads as valid settings (numbered
    // from 1), then version 0, every default.
    private List<SettingsCandidate> Versions()
    {
        var versions = new List<SettingsCandidate>();
        foreach (SettingsHistoryEntry entry in _history.Entries())
        {
            string text = _history.Read(entry);
            IReadOnlyDictionary<string, string> set;
            try
            {
                set = SettingsYaml.Parse(text, entry.Path);
            }
            catch (InvalidDataException)
            {
                // A copy that no longer reads as valid settings (changed by hand, or a setting that no longer exists)
                // is not offered.
                continue;
            }

            versions.Add(new SettingsCandidate(new SettingsVersion(versions.Count + 1, entry.Saved, SetStates(set)), text, set));
        }

        IReadOnlyDictionary<string, string> none = new Dictionary<string, string>(StringComparer.Ordinal);
        versions.Add(new SettingsCandidate(new SettingsVersion(SettingsVersion.Original, null, []), SettingsYaml.Format(none), none));
        return versions;
    }

    // Rewrites the whole file in one step (AtomicTextFile), so a crash never leaves half a file.
    private void Write(IReadOnlyDictionary<string, string> set) =>
        new AtomicTextFile(_services).Write(FilePath, SettingsYaml.Format(set));
}
