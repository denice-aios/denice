// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.IO;

namespace Denice.Engine.Setup;

/// <summary>
/// The machine condition that the installed binary's hash matches the recorded hash. It is report-only: a
/// mismatch is reported ("re-run <c>denice install</c> from a trusted build"), never regenerated, so tampering is
/// surfaced rather than laundered.
/// </summary>
internal sealed class BinaryHashCondition : MachineCondition
{
    /// <inheritdoc />
    public override string Name => "binary hash";

    /// <inheritdoc />
    public override RepairOutcome Repair(SetupContext context) => RepairOutcome.NotRepairable(
        "a binary hash mismatch is reported, not regenerated; re-run `denice install` from a trusted build");

    /// <inheritdoc />
    protected override ConditionState DetectInstalled(SetupContext context, InstallState state)
    {
        string binary = MachinePaths.VersionBinary(context, state.Version);
        if (!context.FileReader.Exists(binary))
        {
            return ConditionState.CannotVerify($"versions/{state.Version}/denice is not present to hash");
        }

        string actual;
        try
        {
            actual = Hashing.Sha256Hex(context.FileReader.ReadAllBytes(binary));
        }
        catch (IOException exception)
        {
            return ConditionState.CannotVerify($"the binary is unreadable: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return ConditionState.CannotVerify($"the binary is unreadable: {exception.Message}");
        }

        return string.Equals(actual, state.Sha256, StringComparison.Ordinal)
            ? ConditionState.Ok()
            : ConditionState.Broken("binary hash mismatch; re-run `denice install` from a trusted build");
    }
}
