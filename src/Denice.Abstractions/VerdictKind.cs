// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions;

/// <summary>
/// The three judgments a Guard can return for a tool call.
/// </summary>
public enum VerdictKind
{
    /// <summary>
    /// The call is permitted to proceed.
    /// </summary>
    Allow,

    /// <summary>
    /// The call is blocked; the accompanying message states why.
    /// </summary>
    Deny,

    /// <summary>
    /// The call proceeds, but an advisory message is surfaced.
    /// </summary>
    Warn,
}
