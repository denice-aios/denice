// Copyright (c) Denice.ai. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Denice.Analyzers;

/// <summary>
/// The single source of truth for the bounded-timeout members the two timeout rules share — <c>Task.Delay</c>,
/// <c>Task.WaitAsync</c>, a <c>CancellationTokenSource</c> constructor, and <c>CancellationTokenSource.CancelAfter</c> —
/// so their type-and-name identities are spelled once (LESSON 1, DRY) rather than duplicated across the analyzers, the
/// same way <see cref="TimeMembers"/> owns the DN0012/DN0015 environment-clock split. Both the timeout-injection ban
/// inside a presence impl (DN0107) and the timeout-must-use-TimeProvider rule (DN0038) recognize their timeout members
/// through these predicates and then apply their OWN policy on top: DN0107 bans the member outright inside a presence
/// impl, DN0038 bans only the overload that omits a <c>TimeProvider</c>. Which policy applies is the caller's; the
/// recognition of "this member is a bounded timeout" lives here once. The members each rule does NOT share — DN0107's
/// <c>Timer</c>/<c>PeriodicTimer</c> construction and DN0038's <c>Thread.Sleep</c> blocking sleep — stay in their own
/// analyzers, since neither is a bounded-timeout member both rules key off.
/// </summary>
internal static class TimeoutMembers
{
    /// <summary>
    /// Gets a value indicating whether the member use is <c>Task.Delay</c> — a delay wait. Recognized by DN0038 (which
    /// requires its <c>TimeProvider</c> overload) and DN0107 (which bans it inside a presence impl).
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
    /// DN0107 bans every such construction inside a presence impl; DN0038 layers on its own "carries a timeout AND omits
    /// a <c>TimeProvider</c>" policy.
    /// </summary>
    /// <param name="member">The referenced member.</param>
    /// <param name="type">The type that declares it.</param>
    /// <returns><see langword="true"/> when the member is a <c>System.Threading.CancellationTokenSource</c> constructor.</returns>
    internal static bool IsCancellationTokenSourceConstructor(ISymbol member, INamedTypeSymbol type) =>
        WellKnownType.Is(type, KnownNamespaces.SystemThreading, "CancellationTokenSource")
        && member is IMethodSymbol { MethodKind: MethodKind.Constructor };

    /// <summary>
    /// Gets a value indicating whether the member use is <c>CancellationTokenSource.CancelAfter</c> — arming a timeout
    /// on an existing source. Recognized by DN0107 (which bans it inside a presence impl).
    /// </summary>
    /// <param name="member">The referenced member.</param>
    /// <param name="type">The type that declares it.</param>
    /// <returns><see langword="true"/> when the member is <c>System.Threading.CancellationTokenSource.CancelAfter</c>.</returns>
    internal static bool IsCancelAfter(ISymbol member, INamedTypeSymbol type) =>
        WellKnownType.Is(type, KnownNamespaces.SystemThreading, "CancellationTokenSource")
        && string.Equals(member.Name, "CancelAfter", StringComparison.Ordinal);

    /// <summary>
    /// Gets a value indicating whether the member use is <c>TimeProvider.CreateTimer</c> — minting a timer off a clock.
    /// Recognized by DN0107 ONLY, which bans it inside a presence impl (a presence impl may create no timeout at all,
    /// even off the injected clock). DN0038 deliberately does NOT key off this: <c>TimeProvider.CreateTimer</c> IS the
    /// injected-clock timer path DN0038 mandates as correct, so flagging it there would contradict DN0038's own remedy.
    /// </summary>
    /// <param name="member">The referenced member.</param>
    /// <param name="type">The type that declares it.</param>
    /// <returns><see langword="true"/> when the member is <c>System.TimeProvider.CreateTimer</c>.</returns>
    internal static bool IsTimeProviderCreateTimer(ISymbol member, INamedTypeSymbol type) =>
        WellKnownType.Is(type, KnownNamespaces.System, "TimeProvider")
        && string.Equals(member.Name, "CreateTimer", StringComparison.Ordinal);
}
