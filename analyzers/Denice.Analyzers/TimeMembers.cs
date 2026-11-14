// Copyright (c) 4thWAIV. All rights reserved.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Denice.Analyzers;

/// <summary>
/// The single source of truth for the <c>System.Environment</c> members that are time reads, not environment reads,
/// so the DN0012/DN0015 partition is defined once. <c>Environment.TickCount</c> and <c>TickCount64</c> read the
/// ambient clock, so the time rule (DN0015) claims them and the environment rule (DN0012) excludes them; both rules
/// read this one owner rather than spelling the member set twice.
/// </summary>
internal static class TimeMembers
{
    /// <summary>
    /// The <c>System.Environment</c> member names that read the ambient clock — <c>TickCount</c> and
    /// <c>TickCount64</c> — which DN0015 owns and DN0012 excludes.
    /// </summary>
    private static readonly ImmutableHashSet<string> EnvironmentClockMemberNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "TickCount",
        "TickCount64");

    /// <summary>
    /// Gets a value indicating whether <paramref name="member"/> is a <c>System.Environment</c> clock member
    /// (<c>TickCount</c> or <c>TickCount64</c>) that DN0015 owns and DN0012 excludes.
    /// </summary>
    /// <param name="member">The referenced member.</param>
    /// <returns><see langword="true"/> when the member name is an environment clock member name.</returns>
    internal static bool IsEnvironmentClockMember(ISymbol member) => EnvironmentClockMemberNames.Contains(member.Name);
}
