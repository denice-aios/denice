// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Denice.Updates;

/// <summary>
/// A Denice product version, <c>MAJOR.MINOR.COMBINED[-channel]+hash</c>, ordered exactly as the loader orders versions
/// (rust/crates/denice-core/src/version.rs): SemVer precedence, then build metadata (<c>+hash</c>) as a final tie-break
/// so the order is total and stable, never to prefer one build over another of the same version. The identifier
/// comparison mirrors the Rust <c>semver</c> crate the loader uses.
/// </summary>
internal sealed class DeniceVersion
{
    private DeniceVersion(string text, ulong major, ulong minor, ulong patch, string preRelease, string build)
    {
        Text = text;
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
        Build = build;
    }

    /// <summary>Gets the order versions are compared in: SemVer precedence, then build metadata.</summary>
    internal static IComparer<DeniceVersion> Order { get; } = Comparer<DeniceVersion>.Create(Compare);

    /// <summary>Gets the version exactly as it was written.</summary>
    internal string Text { get; }

    private ulong Major { get; }

    private ulong Minor { get; }

    private ulong Patch { get; }

    private string PreRelease { get; }

    private string Build { get; }

    /// <inheritdoc />
    public override string ToString() => Text;

    /// <summary>Reads a SemVer 2.0 version, or returns <see langword="null"/> when the text is not one.</summary>
    /// <param name="text">The version text, e.g. <c>0.1.200-pre-release+bbbbbbb</c>.</param>
    /// <returns>The version, or <see langword="null"/>.</returns>
    internal static DeniceVersion? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int plus = text.IndexOf('+', StringComparison.Ordinal);
        string build = plus < 0 ? string.Empty : text[(plus + 1)..];
        string withoutBuild = plus < 0 ? text : text[..plus];
        int dash = withoutBuild.IndexOf('-', StringComparison.Ordinal);
        string preRelease = dash < 0 ? string.Empty : withoutBuild[(dash + 1)..];
        string[] core = (dash < 0 ? withoutBuild : withoutBuild[..dash]).Split('.');
        bool valid = core.Length == 3 && core.All(IsNumber)
            && (plus < 0 || AreIdentifiers(build, numbersMayHaveLeadingZeros: true))
            && (dash < 0 || AreIdentifiers(preRelease, numbersMayHaveLeadingZeros: false));
        if (!valid
            || !ulong.TryParse(core[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong major)
            || !ulong.TryParse(core[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong minor)
            || !ulong.TryParse(core[2], NumberStyles.None, CultureInfo.InvariantCulture, out ulong patch))
        {
            return null;
        }

        return new DeniceVersion(text, major, minor, patch, preRelease, build);
    }

    private static int Compare(DeniceVersion? left, DeniceVersion? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null || right is null)
        {
            return left is null ? -1 : 1;
        }

        int order = left.Major.CompareTo(right.Major);
        order = order != 0 ? order : left.Minor.CompareTo(right.Minor);
        order = order != 0 ? order : left.Patch.CompareTo(right.Patch);
        order = order != 0 ? order : ComparePreRelease(left.PreRelease, right.PreRelease);
        return order != 0 ? order : CompareIdentifiers(left.Build, right.Build);
    }

    // A release outranks every pre-release of the same MAJOR.MINOR.PATCH.
    private static int ComparePreRelease(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return Math.Sign(right.Length.CompareTo(left.Length));
        }

        return CompareIdentifiers(left, right);
    }

    // The semver crate's identifier order: dot-separated identifiers left to right; all-digit identifiers numerically
    // (then, for build metadata, fewer leading zeros first), all-digit before anything else, others in ASCII order; a
    // longer list of otherwise equal identifiers is greater. Pre-release numbers never have leading zeros, so the same
    // order serves both pre-release and build metadata.
    private static int CompareIdentifiers(string left, string right)
    {
        string[] lefts = left.Split('.');
        string[] rights = right.Split('.');
        for (int i = 0; i < Math.Min(lefts.Length, rights.Length); i++)
        {
            int order = CompareIdentifier(lefts[i], rights[i]);
            if (order != 0)
            {
                return order;
            }
        }

        return lefts.Length.CompareTo(rights.Length);
    }

    private static int CompareIdentifier(string left, string right)
    {
        bool leftNumeric = left.All(char.IsAsciiDigit);
        bool rightNumeric = right.All(char.IsAsciiDigit);
        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? -1 : 1;
        }

        if (!leftNumeric)
        {
            return Math.Sign(string.CompareOrdinal(left, right));
        }

        string leftValue = left.TrimStart('0');
        string rightValue = right.TrimStart('0');
        int order = leftValue.Length.CompareTo(rightValue.Length);
        order = order != 0 ? order : Math.Sign(string.CompareOrdinal(leftValue, rightValue));
        return order != 0 ? order : left.Length.CompareTo(right.Length);
    }

    private static bool IsNumber(string part) =>
        part.Length > 0 && part.All(char.IsAsciiDigit) && (part.Length == 1 || part[0] != '0');

    private static bool AreIdentifiers(string identifiers, bool numbersMayHaveLeadingZeros) =>
        identifiers.Split('.').All(identifier =>
            identifier.Length > 0
            && identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            && (numbersMayHaveLeadingZeros || !identifier.All(char.IsAsciiDigit) || IsNumber(identifier)));
}
