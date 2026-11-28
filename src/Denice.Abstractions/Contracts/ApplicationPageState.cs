// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>One immutable snapshot of the application server, read atomically; no I/O or waiting.</summary>
/// <param name="Revision">Increases on every start attempt and published transition, including a restart at the same address.</param>
/// <param name="Address">The ready server address, or null while unavailable.</param>
/// <param name="Error">A user-safe description of the latest failure, or null.</param>
public sealed record ApplicationPageState(long Revision, Uri? Address, string? Error);
