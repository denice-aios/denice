// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Desktop;

/// <summary>Captures startup protocol errors before CEF initializes, then restores the original Xlib handler.</summary>
[SuppressMessage("Design", "CA2216", Justification = "The startup thread must restore the global Xlib callback and release its monitor; a finalizer cannot do either safely.")]
[SuppressMessage("Security", "S6640", Justification = "The Xlib error callback uses the published LP64 XErrorEvent ABI.")]
internal sealed unsafe class X11StartupErrors : IDisposable
{
    private static readonly object Gate = new();
    private static X11StartupErrors? _active;
    private readonly nint _display;
    private nint _previous;
    private int _error;
    private bool _disposed;

    private X11StartupErrors(nint display)
    {
        _display = display;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            End(this);
            _disposed = true;
        }
    }

    /// <summary>Begins a startup scope before other Xlib clients start.</summary>
    /// <param name="display">The startup connection.</param>
    /// <returns>The scope restoring the prior handler.</returns>
    internal static X11StartupErrors Begin(nint display)
    {
        Monitor.Enter(Gate);
        if (_active is not null)
        {
            Monitor.Exit(Gate);
            throw new InvalidOperationException("X11 startup error scopes cannot be nested.");
        }

        var scope = new X11StartupErrors(display);
        try
        {
            _active = scope;
            scope._previous = X11Native.SetErrorHandler((nint)(delegate* unmanaged<nint, nint, int>)&OnError);
            return scope;
        }
        catch
        {
            _active = null;
            Monitor.Exit(Gate);
            throw;
        }
    }

    /// <summary>Drains asynchronous protocol errors while the startup callback is installed.</summary>
    internal void Complete()
    {
        _ = X11Native.Sync(_display, 0);
        if (_error != 0)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"X11 rejected desktop startup with protocol error {_error}."));
        }
    }

    private static void End(X11StartupErrors scope)
    {
        _ = X11Native.SetErrorHandler(scope._previous);
        _active = null;
        Monitor.Exit(Gate);
    }

    [UnmanagedCallersOnly]
    private static int OnError(nint display, nint error)
    {
        X11StartupErrors? scope = _active;
        if (scope is null)
        {
            return 0;
        }

        if (display == scope._display)
        {
            // XErrorEvent.error_code follows display, resource ID and serial.
            scope._error = *((byte*)error + 32);
            return 0;
        }

        return scope._previous != 0 ? ((delegate* unmanaged<nint, nint, int>)scope._previous)(display, error) : 0;
    }
}
