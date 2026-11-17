// Copyright (c) Denice.ai. All rights reserved.

using System;

namespace Denice.Updates;

/// <summary>
/// A setting change was refused before anything was written: the name is not a setting, or the value is not valid for
/// it. The message says which, and what the setting accepts.
/// </summary>
public sealed class InvalidSettingException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="InvalidSettingException"/> class.</summary>
    public InvalidSettingException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidSettingException"/> class with a message.</summary>
    /// <param name="message">What was refused and why.</param>
    public InvalidSettingException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidSettingException"/> class with a message and the exception
    /// that caused it.
    /// </summary>
    /// <param name="message">What was refused and why.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public InvalidSettingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
