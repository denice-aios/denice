// Copyright (c) 4thWAIV. All rights reserved.

using System.IO;

namespace Denice.Setup;

/// <summary>
/// The project condition that <c>.denice/config.json</c> exists and parses. This build does not judge whether
/// a provider is "enabled" — nothing consumes the file yet — so the check is structural only.
/// </summary>
internal sealed class ConfigJsonCondition : ISetupCondition
{
    /// <inheritdoc />
    public string Name => "project config.json";

    /// <inheritdoc />
    public SetupScope Scope => SetupScope.Project;

    /// <inheritdoc />
    public ConditionState Detect(SetupContext context)
    {
        string path = ProjectPaths.ConfigFile(context);
        if (!context.FileReader.Exists(path))
        {
            return ConditionState.Broken(".denice/config.json is missing");
        }

        if (!SafeRead.For(context).TryReadText(path, out string content, out string error))
        {
            return ConditionState.CannotVerify($".denice/config.json is unreadable: {error}");
        }

        if (SetupJson.TryParseObject(content, out string? parseError))
        {
            return ConditionState.Ok();
        }

        return parseError is null
            ? ConditionState.Broken(".denice/config.json is not a JSON object")
            : ConditionState.Broken($".denice/config.json does not parse: {parseError}");
    }

    /// <inheritdoc />
    public RepairOutcome Repair(SetupContext context) => CreationHelper.EnsureConfig(context);
}
