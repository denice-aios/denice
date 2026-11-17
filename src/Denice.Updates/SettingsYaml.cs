// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Denice.Updates;

/// <summary>
/// Reads and writes <c>settings.yaml</c> in a strict YAML subset, so no YAML library is needed for three temporary
/// settings. The subset is one flat mapping of <c>name: value</c> lines:
/// <list type="bullet">
/// <item>blank lines and <c>#</c> comment lines are allowed, and a value may end with a comment after whitespace;</item>
/// <item>a name is written plain, starts the line, and must be a known setting, at most once;</item>
/// <item>the name is followed by a colon and a space (or the end of the line, which leaves no value and is refused);</item>
/// <item>a value is a plain scalar, a single-quoted scalar (<c>''</c> is a quote), or a double-quoted scalar (only the
/// escapes <c>\"</c> and <c>\\</c>), on one line;</item>
/// <item>everything else is refused with a message naming the line: indentation (nesting), lists, flow style, anchors,
/// aliases and tags, block scalars, complex keys, directives, document markers (so also multiple documents), duplicate
/// names, unknown names, and invalid values.</item>
/// </list>
/// What Denice writes is a header of comments and one <c>name: value</c> line per set setting, all plain scalars that
/// any YAML reader reads the same way.
/// </summary>
internal static class SettingsYaml
{
    private static readonly string[] Header =
    [
        "# Denice settings. Change them through Denice:",
        "#   denice settings                       lists every setting with its value and default",
        "#   denice settings set <name> <value>    changes one",
        "#   denice settings reset <name>          returns one to its default",
        "#   denice settings history               lists earlier settings that loaded, newest first",
        "#   denice settings restore <number>      brings one of them back (0 is every default)",
        "# Only settings that were set are listed here; every other setting has its default.",
        "# Denice rewrites this file on every change, so comments added by hand are not kept.",
    ];

    /// <summary>
    /// Parses the settings file's text into the values it sets, each already validated.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <param name="source">The file's path, named in every error message.</param>
    /// <returns>The value of each setting the file sets, by name.</returns>
    /// <exception cref="InvalidDataException">The text is outside the subset or sets an invalid value; the message
    /// names the file and the line.</exception>
    internal static IReadOnlyDictionary<string, string> Parse(string text, string source)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var lineOfName = new Dictionary<string, int>(StringComparer.Ordinal);
        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            int number = index + 1;
            string line = lines[index].TrimEnd('\r');
            string content = line.TrimStart(' ', '\t');
            if (content.Length == 0 || content[0] == '#')
            {
                continue;
            }

            string? structural = LineStartProblem(line);
            if (structural is not null)
            {
                throw Error(source, number, structural);
            }

            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0 || (colon + 1 < line.Length && !IsSpace(line[colon + 1])))
            {
                throw Error(source, number, "expected 'name: value' (a setting name, a colon, a space, then the value)");
            }

            string name = line[..colon].TrimEnd(' ', '\t');
            string rest = line[(colon + 1)..].TrimStart(' ', '\t');
            if (rest.Length == 0 || rest[0] == '#')
            {
                throw Error(source, number, $"{name} has no value; empty values and nested settings are not supported");
            }

            SettingDefinition setting = SettingDefinition.Find(name)
                ?? throw Error(source, number, SettingDefinition.UnknownNameProblem(name));
            if (lineOfName.TryGetValue(name, out int first))
            {
                throw Error(source, number, $"{name} is already set on line {first}; each setting may appear only once");
            }

            string? problem = ValueProblem(rest, out string value);
            if (problem is null && !setting.Accepts(value))
            {
                problem = setting.InvalidValueProblem(value);
            }

            if (problem is not null)
            {
                throw Error(source, number, problem);
            }

            values[name] = value;
            lineOfName[name] = number;
        }

        return values;
    }

    /// <summary>
    /// Writes the settings file's text: the header comments, then one plain <c>name: value</c> line per set setting in
    /// the order the settings are listed.
    /// </summary>
    /// <param name="values">The value of each set setting, by name; every value is already valid.</param>
    /// <returns>The file's text, with <c>\n</c> line endings.</returns>
    internal static string Format(IReadOnlyDictionary<string, string> values)
    {
        var text = new StringBuilder();
        foreach (string line in Header)
        {
            text.Append(line).Append('\n');
        }

        foreach (string name in SettingDefinition.All.Select(setting => setting.Name).Where(values.ContainsKey))
        {
            text.Append(name).Append(": ").Append(values[name]).Append('\n');
        }

        return text.ToString();
    }

    private static InvalidDataException Error(string source, int number, string problem) =>
        new($"{source}, line {number}: {problem}.");

    private static bool IsSpace(char character) => character is ' ' or '\t';

    // What a non-blank, non-comment line may not start with, because it would leave the flat name: value subset.
    private static string? LineStartProblem(string line)
    {
        if (IsSpace(line[0]))
        {
            return "indented lines are not supported; settings are a flat list of 'name: value' lines with no nesting";
        }

        bool documentMarker = line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("...", StringComparison.Ordinal);
        return documentMarker && EndsWord(line, 3)
            ? "document markers ('---', '...') are not supported; the file holds exactly one document"
            : IndicatorProblem(line);
    }

    // What a plain name or a plain value may not start with: a YAML indicator that would make a reader see something
    // other than a plain scalar.
    private static string? IndicatorProblem(string text)
    {
        if (text[0] is '-' or '?' && EndsWord(text, 1))
        {
            return text[0] == '-' ? "lists are not supported; write 'name: value'" : "complex keys ('?') are not supported";
        }

        return text[0] switch
        {
            '[' or '{' => "flow style ('[...]', '{...}') is not supported",
            '&' or '*' or '!' => "anchors, aliases and tags ('&', '*', '!') are not supported",
            '|' or '>' => "block scalars ('|', '>') are not supported; write the value on the same line",
            '%' => "directives ('%') are not supported",
            '\'' or '"' => "quoted setting names are not supported; write the name plain",
            '@' or '`' or ',' or ']' or '}' => $"'{text[0]}' cannot start a name or a value",
            _ => null,
        };
    }

    // Whether the first `length` characters of text are a whole word: the text ends there or whitespace follows.
    private static bool EndsWord(string text, int length) => text.Length == length || IsSpace(text[length]);

    // Reads the non-empty value after "name: " (leading whitespace already removed): a quoted or plain scalar with an
    // optional trailing comment. Returns the problem when it is outside the subset, else null with the value set.
    private static string? ValueProblem(string text, out string value) =>
        text[0] switch
        {
            '"' => QuotedProblem(text, '"', out value),
            '\'' => QuotedProblem(text, '\'', out value),
            _ => PlainProblem(text, out value),
        };

    private static string? PlainProblem(string text, out string value)
    {
        value = string.Empty;
        string? start = IndicatorProblem(text);
        if (start is not null)
        {
            return start;
        }

        int comment = CommentStart(text);
        string plain = (comment < 0 ? text : text[..comment]).TrimEnd(' ', '\t');
        if (plain.EndsWith(':') || plain.Contains(": ", StringComparison.Ordinal) || plain.Contains(":\t", StringComparison.Ordinal))
        {
            return "a value cannot contain ': ' (that would start a nested mapping)";
        }

        value = plain;
        return null;
    }

    // Where a comment starts in a plain value: the first '#' that follows whitespace, or -1 when there is none.
    private static int CommentStart(string text)
    {
        for (int index = 1; index < text.Length; index++)
        {
            if (text[index] == '#' && IsSpace(text[index - 1]))
            {
                return index;
            }
        }

        return -1;
    }

    // A single-line quoted scalar: '' is a quote inside single quotes; \" and \\ are the only escapes inside double
    // quotes. After the closing quote only whitespace and a comment may follow.
    private static string? QuotedProblem(string text, char quote, out string value)
    {
        value = string.Empty;
        var content = new StringBuilder();
        int index = 1;
        while (index < text.Length)
        {
            char character = text[index];
            if (character == quote && quote == '\'' && index + 1 < text.Length && text[index + 1] == '\'')
            {
                content.Append('\'');
                index += 2;
            }
            else if (character == quote)
            {
                string rest = text[(index + 1)..];
                string trimmed = rest.TrimStart(' ', '\t');
                if (trimmed.Length > 0 && (trimmed[0] != '#' || !IsSpace(rest[0])))
                {
                    return "only a comment may follow the closing quote";
                }

                value = content.ToString();
                return null;
            }
            else if (character == '\\' && quote == '"')
            {
                char? escaped = index + 1 < text.Length ? text[index + 1] : null;
                if (escaped is not ('"' or '\\'))
                {
                    return "the only escapes supported in double quotes are \\\" and \\\\";
                }

                content.Append(escaped.Value);
                index += 2;
            }
            else
            {
                content.Append(character);
                index++;
            }
        }

        return "the quoted value is not closed on this line; values spanning lines are not supported";
    }
}
