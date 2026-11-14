// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions;

/// <summary>
/// Identifies which host lifecycle event a hook invocation corresponds to.
/// </summary>
public enum HookEvent
{
    /// <summary>
    /// The event fired before a tool call runs, where a write can still be blocked.
    /// </summary>
    PreToolUse,

    /// <summary>
    /// The event fired after a tool call runs, where a landed change can be reverted.
    /// </summary>
    PostToolUse,
}
