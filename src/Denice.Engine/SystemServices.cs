// Copyright (c) 4thWAIV. All rights reserved.

using System;
using Denice.Abstractions.Contracts;
using Denice.Boundaries;
using Denice.CrossPlatform;

namespace Denice.Engine;

/// <summary>
/// The single concrete <see cref="ISystemServices"/> container AND the one composition factory that builds it
/// (single-construction-point). It holds the owned OS/CLR services and nothing else, arriving by constructor
/// injection at the one composition point; its constructor is <c>private</c> (Wall 1,
/// containers-are-locked-classes-not-records), so no second container can be built inside the assembly to bypass the
/// one factory. <see cref="Create"/> is pinned by DN0017 to the two legal composition callers (the CLI's
/// <c>Program</c> method and the test <c>SystemServicesBuilder</c>); it reads <c>TimeProvider.System</c> itself
/// (legal only here and in the builder, DN0015) and builds every owned service by hand.
/// </summary>
/// <remarks>
/// <para>
/// The container and its factory live in <c>Denice.Engine</c>; the four adapters it assembles —
/// <c>EnvironmentAdapter</c>, <c>ConsoleAdapter</c>, <c>Ed25519SignatureService</c>, and <c>BuildInfoReader</c> —
/// stay in <c>Denice.Boundaries</c>, which grants its internals to this assembly alone.
/// <see cref="Create"/> is therefore the one method in Engine that may reach into <c>Denice.Boundaries</c>
/// (DN0040, and only through those four factory identities), into the core <c>Denice.CrossPlatform</c> assembly
/// (DN0023), and into a per-OS implementation assembly (DN0029); it is also the only Engine internal the CLI and the
/// test <c>SystemServicesBuilder</c> may reach through the grants this assembly declares to them (DN0041).
/// </para>
/// <para>
/// This is the one mandated container shape (container-is-one-class-with-its-own-create): ONE locked class that
/// implements its contract interface, has a <c>private</c> constructor, and exposes its own
/// <c>public static ISystemServices Create()</c> build point — never a separate factory type and never a passed-in
/// service. The earlier two-type sketch (a separate <c>SystemServicesContainer</c> impl plus a <c>SystemServices</c>
/// factory) is superseded: a separate private-constructor impl cannot be constructed by a separate factory under the
/// frozen rules (a factory returning <see cref="ISystemServices"/> is pinned to <c>Program</c>/the builder, DN0017; a
/// factory returning the concrete container exposes it, DN0004/DN0006; a service-typed factory parameter is forbidden,
/// DN0031; a private constructor is unreachable from a sibling class), so the one locked class IS the pattern.
/// </para>
/// </remarks>
internal sealed class SystemServices : ISystemServices
{
    // Private constructor (DN0003, Wall 1): every service arrives by constructor injection at the one composition
    // point; nothing else constructs an OS-service container.
    private SystemServices(
        IFileSystem fileSystem,
        IEnvironment environment,
        IRandomGenerator random,
        IConsole console,
        IPlatformServices platform,
        ISignatureService signatures,
        IBuildInfo buildInfo,
        TimeProvider clock)
    {
        FileSystem = fileSystem;
        Environment = environment;
        Random = random;
        Console = console;
        Platform = platform;
        Signatures = signatures;
        BuildInfo = buildInfo;
        Clock = clock;
    }

    /// <inheritdoc />
    public IFileSystem FileSystem { get; }

    /// <inheritdoc />
    public IEnvironment Environment { get; }

    /// <inheritdoc />
    public IRandomGenerator Random { get; }

    /// <inheritdoc />
    public IConsole Console { get; }

    /// <inheritdoc />
    public IPlatformServices Platform { get; }

    /// <inheritdoc />
    public ISignatureService Signatures { get; }

    /// <inheritdoc />
    public IBuildInfo BuildInfo { get; }

    /// <inheritdoc />
    public TimeProvider Clock { get; }

    /// <summary>
    /// Builds the OS/CLR service container once. It reads the clock itself (a direct <c>TimeProvider.System</c>
    /// acquisition is legal only here and in the test builder, DN0015), obtains the OS-uniform filesystem entry point
    /// and GUID factory from the one <see cref="CrossPlatformAdapters"/> factory (the single allowed
    /// Engine → CrossPlatform reach, DN0023) and the OS-divergent platform from the one per-OS
    /// <c>PlatformServices.Create()</c> (the single allowed Engine → per-OS reach, DN0029), builds the
    /// Boundaries-owned environment, console, signature, and build-info owners through the four permitted adapter
    /// factories (the single allowed Engine → Boundaries call, DN0040), and assembles the single container by
    /// constructor injection. Every one of those reaches is permitted from THIS method and nowhere else in Engine.
    /// </summary>
    /// <returns>The single, fully-assembled service container.</returns>
    public static ISystemServices Create()
    {
        TimeProvider clock = TimeProvider.System;

        CrossPlatformAdapters adapters = CrossPlatformAdapters.Create();
        IPlatformServices platform = global::Denice.CrossPlatform.PlatformServices.Create();

        IEnvironment environment = EnvironmentAdapter.Create();
        IConsole console = ConsoleAdapter.Create();
        ISignatureService signatures = Ed25519SignatureService.Create();
        IBuildInfo buildInfo = BuildInfoReader.Create();

        return new SystemServices(
            adapters.FileSystem,
            environment,
            adapters.Random,
            console,
            platform,
            signatures,
            buildInfo,
            clock);
    }
}
