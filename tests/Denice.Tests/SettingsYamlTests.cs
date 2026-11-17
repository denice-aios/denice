// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using Denice.Updates;
using FluentAssertions;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// The strict YAML subset <c>settings.yaml</c> is read and written in: a flat mapping of <c>name: value</c> lines with
/// comments and quoting allowed, and everything else refused with a message naming the line.
/// </summary>
public sealed class SettingsYamlTests
{
    private const string Source = "settings.yaml";

    [Fact]
    public void Parse_OfNothing_SetsNothing()
    {
        SettingsYaml.Parse(string.Empty, Source).Should().BeEmpty();
    }

    [Fact]
    public void Parse_AcceptsCommentsBlankLinesQuotingAndWindowsLineEndings()
    {
        string text = string.Join(
            "\r\n",
            "# Denice settings",
            string.Empty,
            "   ",
            "  # an indented comment",
            "updates.enabled: false   # a trailing comment",
            "updates.channel: 'stable' # after a quote",
            "updates.checkIntervalHours :\t\"12\"",
            string.Empty);

        IReadOnlyDictionary<string, string> values = SettingsYaml.Parse(text, Source);

        values.Should().BeEquivalentTo(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["updates.enabled"] = "false",
            ["updates.channel"] = "stable",
            ["updates.checkIntervalHours"] = "12",
        });
    }

    [Fact]
    public void Parse_KeepsAHashInsideAPlainValueThatDoesNotFollowWhitespace()
    {
        Action act = () => SettingsYaml.Parse("updates.channel: sta#ble", Source);

        act.Should().Throw<InvalidDataException>().WithMessage("*'sta#ble' is not a valid value for updates.channel*");
    }

    [Theory]
    [InlineData("updates.channel: 'it''s'", "'it's'")]
    [InlineData("updates.channel: \"a\\\"b\\\\c\"", "'a\"b\\c'")]
    public void Parse_UnescapesQuotedValuesBeforeCheckingThem(string line, string unescaped)
    {
        Action act = () => SettingsYaml.Parse(line, Source);

        act.Should().Throw<InvalidDataException>().WithMessage($"*{unescaped} is not a valid value*");
    }

    [Theory]
    [InlineData("updates.enabled", "true")]
    [InlineData("updates.enabled", "false")]
    [InlineData("updates.channel", "pre-release")]
    [InlineData("updates.channel", "stable")]
    [InlineData("updates.checkIntervalHours", "1")]
    [InlineData("updates.checkIntervalHours", "6")]
    [InlineData("updates.checkIntervalHours", "720")]
    public void Parse_AcceptsEveryValidValue(string name, string value)
    {
        SettingsYaml.Parse($"{name}: {value}\n", Source).Should().Contain(name, value);
    }

    [Theory]
    [InlineData("updates.enabled", "yes", "true or false")]
    [InlineData("updates.enabled", "True", "true or false")]
    [InlineData("updates.enabled", "1", "true or false")]
    [InlineData("updates.channel", "beta", "pre-release or stable")]
    [InlineData("updates.channel", "Stable", "pre-release or stable")]
    [InlineData("updates.checkIntervalHours", "0", "a whole number of hours from 1 to 720")]
    [InlineData("updates.checkIntervalHours", "721", "a whole number of hours from 1 to 720")]
    [InlineData("updates.checkIntervalHours", "1000", "a whole number of hours from 1 to 720")]
    [InlineData("updates.checkIntervalHours", "06", "a whole number of hours from 1 to 720")]
    [InlineData("updates.checkIntervalHours", "-1", "a whole number of hours from 1 to 720")]
    [InlineData("updates.checkIntervalHours", "+6", "a whole number of hours from 1 to 720")]
    [InlineData("updates.checkIntervalHours", "1.5", "a whole number of hours from 1 to 720")]
    [InlineData("updates.checkIntervalHours", "six", "a whole number of hours from 1 to 720")]
    public void Parse_RefusesAnInvalidValueNamingTheLineAndWhatIsAllowed(string name, string value, string allowed)
    {
        Action act = () => SettingsYaml.Parse($"# header\n{name}: {value}\n", Source);

        act.Should().Throw<InvalidDataException>()
            .WithMessage($"settings.yaml, line 2: '{value}' is not a valid value for {name}; use {allowed}.");
    }

    [Theory]
    [InlineData("updates:\n  enabled: true", 1, "updates has no value; empty values and nested settings are not supported.")]
    [InlineData("updates.enabled: true\n  updates.channel: stable", 2, "indented lines are not supported*")]
    [InlineData("updates.enabled:", 1, "updates.enabled has no value*")]
    [InlineData("updates.enabled: # nothing", 1, "updates.enabled has no value*")]
    [InlineData("- updates.enabled: true", 1, "lists are not supported*")]
    [InlineData("updates.enabled: - true", 1, "lists are not supported*")]
    [InlineData("updates.enabled: [true]", 1, "flow style*")]
    [InlineData("{updates.enabled: true}", 1, "flow style*")]
    [InlineData("updates.enabled: &on true", 1, "anchors, aliases and tags*")]
    [InlineData("updates.enabled: *on", 1, "anchors, aliases and tags*")]
    [InlineData("updates.enabled: !!bool true", 1, "anchors, aliases and tags*")]
    [InlineData("updates.enabled: |\n  true", 1, "block scalars*")]
    [InlineData("updates.enabled: >", 1, "block scalars*")]
    [InlineData("updates.enabled: true\n---\nupdates.enabled: false", 2, "document markers*")]
    [InlineData("---", 1, "document markers*")]
    [InlineData("...", 1, "document markers*")]
    [InlineData("? updates.enabled\n: true", 1, "complex keys*")]
    [InlineData("%YAML 1.2", 1, "directives*")]
    [InlineData("'updates.enabled': true", 1, "quoted setting names are not supported*")]
    [InlineData("updates.enabled: @on", 1, "'@' cannot start a name or a value.")]
    [InlineData("updates.enabled: true\nupdates.channel: stable\nupdates.enabled: false", 3, "updates.enabled is already set on line 1*")]
    [InlineData("updates.colour: red", 1, "'updates.colour' is not a setting; the settings are updates.enabled, updates.channel, updates.checkIntervalHours.")]
    [InlineData("Updates.Enabled: true", 1, "'Updates.Enabled' is not a setting*")]
    [InlineData("updates.enabled:true", 1, "expected 'name: value'*")]
    [InlineData("updates.enabled", 1, "expected 'name: value'*")]
    [InlineData("updates.enabled: a: b", 1, "a value cannot contain ': '*")]
    [InlineData("updates.enabled: true:", 1, "a value cannot contain ': '*")]
    [InlineData("updates.enabled: 'true", 1, "the quoted value is not closed on this line*")]
    [InlineData("updates.enabled: \"true\"x", 1, "only a comment may follow the closing quote.")]
    [InlineData("updates.enabled: \"true\"#x", 1, "only a comment may follow the closing quote.")]
    [InlineData("updates.enabled: \"tr\\ue\"", 1, "the only escapes supported in double quotes*")]
    [InlineData("updates.enabled: \"true\\", 1, "the only escapes supported in double quotes*")]
    public void Parse_RefusesAnythingOutsideTheSubsetNamingTheLine(string text, int line, string problem)
    {
        Action act = () => SettingsYaml.Parse(text, Source);

        act.Should().Throw<InvalidDataException>().WithMessage($"settings.yaml, line {line}: {problem}");
    }

    [Fact]
    public void Format_WritesHeaderCommentsThenOnePlainLinePerSetSettingInListOrder()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["updates.checkIntervalHours"] = "12",
            ["updates.enabled"] = "false",
        };

        string text = SettingsYaml.Format(values);

        text.Should().EndWith("\nupdates.enabled: false\nupdates.checkIntervalHours: 12\n");
        text.Should().NotContain("\r");
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Should().OnlyContain(line => line.StartsWith('#') || line.StartsWith("updates.", StringComparison.Ordinal));
        SettingsYaml.Parse(text, Source).Should().BeEquivalentTo(values);
    }

    [Fact]
    public void Format_OfNothing_IsOnlyComments()
    {
        string text = SettingsYaml.Format(new Dictionary<string, string>(StringComparer.Ordinal));

        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().OnlyContain(line => line.StartsWith('#'));
        SettingsYaml.Parse(text, Source).Should().BeEmpty();
    }
}
