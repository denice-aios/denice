// Copyright (c) Denice.ai. All rights reserved.

using Microsoft.Win32.SafeHandles;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>
/// Signals an event handle that Chromium owns (IAudioClient::SetEventHandle, waveOutOpen CALLBACK_EVENT) through the
/// managed <see cref="EventWaitHandle"/>; the borrowed handle is never closed by DENICE.
/// </summary>
internal sealed class BorrowedEvent : IDisposable
{
    private readonly EventWaitHandle _event;

    private BorrowedEvent(nint handle)
    {
        _event = new EventWaitHandle(false, EventResetMode.AutoReset);

        // Replace the event's own handle with the borrowed one; ownsHandle false keeps Chromium's handle open.
        _event.SafeWaitHandle.Dispose();
        _event.SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: false);
    }

    /// <inheritdoc />
    public void Dispose() => _event.Dispose();

    /// <summary>Wraps a borrowed event handle, or returns null for a null handle.</summary>
    /// <param name="handle">The handle Chromium supplied.</param>
    /// <returns>The wrapper, or null.</returns>
    internal static BorrowedEvent? Create(nint handle) => handle == 0 ? null : new BorrowedEvent(handle);

    /// <summary>Signals the event.</summary>
    internal void Set() => _event.Set();
}
