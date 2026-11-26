// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.MacOS.Recording;

/// <summary>Owns a retained native media object and releases it on abandonment.</summary>
internal sealed class MediaHandle : SafeHandle
{
    private readonly bool _coreFoundation;

    private MediaHandle(nint value, bool coreFoundation)
        : base(0, ownsHandle: true)
    {
        _coreFoundation = coreFoundation;
        SetHandle(value);
    }

    /// <inheritdoc />
    public override bool IsInvalid => handle == 0;

    /// <summary>Takes ownership of a retained native media reference.</summary>
    /// <param name="value">The retained native reference.</param>
    /// <param name="coreFoundation">Whether the reference uses Core Foundation release.</param>
    /// <returns>The owning safe handle.</returns>
    internal static MediaHandle Own(nint value, bool coreFoundation = false) => new(value, coreFoundation);

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        if (_coreFoundation)
        {
            RecordingNative.CFRelease(handle);
        }
        else
        {
            _ = RecordingNative.Send(handle, "release");
        }

        return true;
    }
}
