// Copyright (c) Denice.ai. All rights reserved.

using System;

namespace Denice.TestHelpers;

/// <summary>Thrown by <see cref="FakeEnvironment.FailFast"/> where the real environment would end the process at once.</summary>
public sealed class FakeFailFastException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="FakeFailFastException"/> class.</summary>
    public FakeFailFastException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FakeFailFastException"/> class.</summary>
    /// <param name="message">The fail-fast message.</param>
    public FakeFailFastException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FakeFailFastException"/> class.</summary>
    /// <param name="message">The fail-fast message.</param>
    /// <param name="innerException">The cause.</param>
    public FakeFailFastException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
