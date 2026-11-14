// Copyright (c) Denice.ai. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Denice.Analyzers;

/// <summary>
/// The single source of truth for the bounded-timeout members the two timeout rules share — <c>Task.Delay</c>,
/// <c>Task.WaitAsync</c>, a <c>CancellationTokenSource</c> constructor, and <c>CancellationTokenSource.CancelAfter</c> —
/// so their type-and-name identities are spelled once (LESSON 1, DRY) rather than duplicated across the analyzers, the
/// same way <see cref="TimeMembers"/> owns the DN0012/DN0015 environment-clock split. The timeout-must-use-TimeProvider rule (DN0038) recognizes its timeout members through these
/// predicates and applies its own policy on top: it bans only the overload that omits a <c>TimeProvider</c>. The
/// recognition of "this member is a bounded timeout" lives here once; DN0038's <c>Thread.Sleep</c> blocking sleep stays
/// in its own analyzer, since it is not a bounded-timeout member.
/// </summary>
internal static class TimeoutMembers
{
    /// <summary>
    /// Gets a value indicating whether the member use is <c>Task.Delay</c> — a delay wait. Recognized by DN0038 (which
    /// requires its <c>TimeProvider</c> overload).
    /// </summary>
    /// <param name="member">The referenced member.</param>
    /// <param name="type">The type that declares it.</param>
    /// <returns><see langword="true"/> when the member is <c>System.Threading.Tasks.Task.Delay</c>.</returns>
    internal static bool IsTaskDelay(ISymbol member, INamedTypeSymbol type) =>
        WellKnownType.Is(type, KnownNamespaces.SystemThreadingTasks, "Task")
        && string.Equals(member.Name, "Delay", StringComparison.Ordinal);

    /// <summary>
    /// Gets a value indicating whether the member use is <c>Task.WaitAsync</c> — a bounded await. Recognized by DN0038
    /// (which requires its <c>TimeProvider</c> overload).
    /// </summary>
    /// <param name="member">The referenced member.</param>
    /// <param name="type">The type that declares it.</param>
    /// <returns><see langword="true"/> when the member is <c>System.Threading.Tasks.Task.WaitAsync</c>.</returns>
    internal static bool IsTaskWaitAsync(ISymbol member, INamedTypeSymbol type) =>
        WellKnownType.Is(type, KnownNamespaces.SystemThreadingTasks, "Task")
        && string.Equals(member.Name, "WaitAsync", StringComparison.Ordinal);

    /// <summary>
    /// Gets a value indicating whether the member use is a <c>CancellationTokenSource</c> constructor (any overload).
    /// DN0038 layers on its own "carries a timeout AND omits a <c>TimeProvider</c>" policy.
    /// </summary>
    /// <param name="member">The referenced member.</param>
    /// <param name="type">The type that declares it.</param>
    /// <returns><see langword="true"/> when the member is a <c>System.Threading.CancellationTokenSource</c> constructor.</returns>
    internal static bool IsCancellationTokenSourceConstructor(ISymbol member, INamedTypeSymbol type) =>
        WellKnownType.Is(type, KnownNamespaces.SystemThreading, "CancellationTokenSource")
        && member is IMethodSymbol { MethodKind: MethodKind.Constructor };
}
