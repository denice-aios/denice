// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions;
using Denice.CrossPlatform.MacOS;
using FluentAssertions;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// The macOS pure-mapper table test (contract "What to do" #7): <c>MacOsPresenceCheck.Map</c> translates EVERY native
/// <see cref="LaResult"/> to the shared <see cref="ApprovalReason"/> the gate expects — the seam where the native detail
/// becomes a decision. Compiled only on the macOS CI leg (it reaches the internal macOS mapper).
/// </summary>
public sealed class MacOsPresenceMapperTests
{
    // LaResult is internal (reached here through the macOS IVT grant), so it cannot appear in a public test-method
    // signature; the outcome is passed as its underlying int constant and cast back inside. The outcome->reason table is
    // owned once by MacOsPresenceOutcomes and shared with the check-calls-the-port test (DRY).
    [Theory]
    [MemberData(nameof(MacOsPresenceOutcomes.Cases), MemberType = typeof(MacOsPresenceOutcomes))]
    public void Map_TranslatesEveryNativeOutcome_ToItsApprovalReason(int nativeOutcome, ApprovalReason expected)
    {
        MacOsPresenceCheck.Map((LaResult)nativeOutcome).Should().Be(expected);
    }
}
