// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>
/// Where this operating system keeps Denice's per-user data folder, outside the install folder so updates never touch
/// it (<see cref="IPlatformFileSystem.UserDataFolder"/>): Windows <c>%LOCALAPPDATA%\Denice\</c>, macOS
/// <c>~/Library/Application Support/Denice/</c>, Linux <c>~/.config/denice/</c>. It is a description, not a path, so the
/// platform layer stays free of environment reads: an OS-agnostic caller resolves it through its own
/// <see cref="IEnvironment"/>, which keeps the resolution testable for every OS on any OS.
/// </summary>
/// <param name="EnvironmentVariable">The environment variable whose value is the folder's base (<c>LOCALAPPDATA</c> on
/// Windows), or <see langword="null"/> when the base is the user's home directory.</param>
/// <param name="RelativePath">The folder's path under that base, written with this OS's directory separator.</param>
public readonly record struct UserDataFolder(string? EnvironmentVariable, string RelativePath);
