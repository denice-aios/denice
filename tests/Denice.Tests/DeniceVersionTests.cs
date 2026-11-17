// Copyright (c) Denice.ai. All rights reserved.

using Denice.Updates;
using FluentAssertions;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// Denice versions order exactly as the loader orders them (rust/crates/denice-core/src/version.rs): SemVer precedence,
/// then build metadata as a final tie-break, with the Rust <c>semver</c> crate's identifier order.
/// </summary>
public sealed class DeniceVersionTests
{
    [Theory]
    [InlineData("0.1.200-pre-release+aaaaaaa", "0.1.300-pre-release+aaaaaaa")]
    [InlineData("0.1.300-pre-release+fffffff", "0.1.300+aaaaaaa")]
    [InlineData("0.1.300-pre-release+aaaaaaa", "0.1.300-pre-release+bbbbbbb")]
    [InlineData("0.9.999", "0.10.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("1.0.0", "1.0.0+0")]
    [InlineData("1.0.0+9", "1.0.0+10")]
    [InlineData("1.0.0+1", "1.0.0+01")]
    [InlineData("1.0.0+999", "1.0.0+a")]
    [InlineData("1.0.0+a", "1.0.0+a.b")]
    [InlineData("1.0.0-pre-release+aaaaaaa", "1.0.0-pre-release.0+aaaaaaa")]
    public void Order_PutsTheFirstBelowTheSecond(string lower, string higher)
    {
        DeniceVersion low = DeniceVersion.Parse(lower)!;
        DeniceVersion high = DeniceVersion.Parse(higher)!;

        DeniceVersion.Order.Compare(low, high).Should().Be(-1);
        DeniceVersion.Order.Compare(high, low).Should().Be(1);
    }

    [Theory]
    [InlineData("0.1.300-pre-release+aaaaaaa")]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+007")]
    public void Order_OfAVersionWithItself_IsEqual(string text)
    {
        DeniceVersion.Order.Compare(DeniceVersion.Parse(text), DeniceVersion.Parse(text)).Should().Be(0);
        DeniceVersion.Order.Compare(DeniceVersion.Parse(text), null).Should().Be(1);
        DeniceVersion.Order.Compare(null, DeniceVersion.Parse(text)).Should().Be(-1);
        DeniceVersion.Parse(text)!.ToString().Should().Be(text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0-a..b")]
    [InlineData("1.0.0+a_b")]
    [InlineData("v1.0.0")]
    [InlineData("1.0.99999999999999999999")]
    public void Parse_OfSomethingThatIsNotSemVer_IsNull(string text) => DeniceVersion.Parse(text).Should().BeNull();
}
