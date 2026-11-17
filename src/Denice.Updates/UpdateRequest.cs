// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Updates;

/// <summary>Why an update check runs.</summary>
public enum UpdateRequest
{
    /// <summary>
    /// The background check at launch: only when updates are on and the check interval has passed; stopped when a
    /// one-shot command ends and retried at the next launch (pkg-79dc8dd1e15d, "retry-next-launch").
    /// </summary>
    Scheduled,

    /// <summary>
    /// <c>denice update</c>: checks now whatever the interval and the off switch say. Tim (pkg-79dc8dd1e15d): "We'll also
    /// add an "update" command to allow the user to manually override.".
    /// </summary>
    Manual,
}
