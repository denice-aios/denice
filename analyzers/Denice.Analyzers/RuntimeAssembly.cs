// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Analyzers;

/// <summary>
/// Identifies Denice Runtime, the .NET DLL the Rust entry hosts, whose single <c>Program</c> composition method is the
/// one place <c>SystemServices.Create()</c> may be called (DN0017). The match has two halves that are checked
/// separately: the <c>Program</c> TYPE by its namespace (<see cref="RootNamespace"/>) + name, and the ASSEMBLY by its
/// real compiled name (<see cref="CompiledName"/>, what <c>Program.ContainingAssembly.Name</c> reports). Requiring both
/// stops a class merely named <c>Program</c> in another namespace or another assembly from self-granting the right to
/// reconstruct the container. The single owner of both values, mirroring <see cref="BoundaryAssembly"/> and
/// <see cref="TestAssembly"/>.
/// </summary>
internal static class RuntimeAssembly
{
    /// <summary>
    /// Denice Runtime's root namespace, <c>Denice.Runtime</c>, in which the <c>Program</c> composition type is
    /// declared. The single owner of this value.
    /// </summary>
    internal const string RootNamespace = "Denice.Runtime";

    /// <summary>
    /// Denice Runtime's compiled assembly name, <c>Denice.Runtime</c> (the file <c>Denice.Runtime.dll</c> the Rust
    /// entry runs, eng/loader-protocol.json). It equals the root namespace, but the two are still checked as separate
    /// halves so a <c>Program</c> in the right namespace of the wrong assembly is never exempt. The single owner of
    /// this value.
    /// </summary>
    internal const string CompiledName = "Denice.Runtime";
}
