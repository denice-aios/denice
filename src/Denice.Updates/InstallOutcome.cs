// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Updates;

/// <summary>
/// What installing a release did, and for which version.
/// </summary>
/// <param name="Result">Whether the version was installed or was already there.</param>
/// <param name="Version">The version the release carries.</param>
public sealed record InstallOutcome(InstallResult Result, string Version);
