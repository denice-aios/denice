// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.CrossPlatform.Browser;

/// <summary>
/// The lifetime of one redirected Chromium output stream. Calls for units the redirection did not create pass straight
/// through.
/// </summary>
internal enum StreamState
{
    /// <summary>The HAL output unit exists and is tracked.</summary>
    Created,

    /// <summary>Its render callback and format have been kept.</summary>
    Configured,

    /// <summary>The mixer pulls it on every render cycle.</summary>
    Registered,

    /// <summary>The mixer no longer calls it and no render cycle is inside it.</summary>
    Unregistered,

    /// <summary>The real unit has been disposed through the original.</summary>
    Disposed,
}
