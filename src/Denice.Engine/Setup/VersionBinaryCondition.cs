// Copyright (c) Denice.ai. All rights reserved.

using System.IO;

namespace Denice.Engine.Setup;

/// <summary>
/// The machine condition that the installed version's binary is present in the version store.
/// </summary>
internal sealed class VersionBinaryCondition : MachineCondition
{
    /// <inheritdoc />
    public override string Name => "machine version binary";

    /// <inheritdoc />
    public override RepairOutcome Repair(SetupContext context) => CreationHelper.EnsureVersionBinary(context);

    /// <inheritdoc />
    protected override ConditionState DetectInstalled(SetupContext context, InstallState state) =>
        context.FileReader.Exists(MachinePaths.VersionBinary(context, state.Version))
            ? ConditionState.Ok()
            : ConditionState.Broken($"versions/{state.Version}/denice is missing");
}
