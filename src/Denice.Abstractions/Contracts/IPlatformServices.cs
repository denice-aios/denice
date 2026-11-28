// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>
/// The container of the platform capabilities the engine depends on, obtained once from the per-OS
/// <c>PlatformServices.Create()</c> factory. It exposes one capability today, <see cref="FileSystem"/>, and is designed to
/// grow — future capabilities (for example a biometric/keychain surface) become additional properties here with no
/// restructuring of the factory or its callers.
/// </summary>
public interface IPlatformServices
{
    /// <summary>
    /// Gets the platform's file-system capability.
    /// </summary>
    IPlatformFileSystem FileSystem { get; }

    /// <summary>Gets the native desktop capability.</summary>
    IPlatformDesktop Desktop { get; }
}
