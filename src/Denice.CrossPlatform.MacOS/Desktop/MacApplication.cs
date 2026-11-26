// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.MacOS.Desktop;

/// <summary>Cocoa application implementing the CefAppProtocol send-event nesting contract.</summary>
[SuppressMessage("Security", "S6640", Justification = "UnmanagedCallersOnly callbacks are registered with the Objective-C runtime using stable unmanaged function pointers.")]
internal static unsafe class MacApplication
{
    private static bool _handlingEvent;
    private static bool _quit;

    /// <summary>Gets a value indicating whether Cocoa requested application termination.</summary>
    internal static bool QuitRequested => _quit;

    /// <summary>Registers the Cocoa application subclass and initializes the main application.</summary>
    /// <returns>The created object or native operation result.</returns>
    internal static nint Create()
    {
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        NativeLibrary.Load("/System/Library/Frameworks/QuartzCore.framework/QuartzCore");
        nint cls = MacNative.AllocateClass(MacNative.Class("NSApplication"), "DeniceApplication", 0);
        if (cls == 0)
        {
            throw new InvalidOperationException("The DENICE Cocoa application class already exists.");
        }

        Add(cls, "isHandlingSendEvent", (nint)(delegate* unmanaged<nint, nint, byte>)&IsHandling, "c@:");
        Add(cls, "setHandlingSendEvent:", (nint)(delegate* unmanaged<nint, nint, byte, void>)&SetHandling, "v@:c");
        Add(cls, "sendEvent:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&SendEvent, "v@:@");
        Add(cls, "terminate:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&Terminate, "v@:@");
        MacNative.RegisterClass(cls);
        nint app = MacNative.Call(cls, "sharedApplication");
        MacNative.Call(app, "setActivationPolicy:", 0);
        MacNative.Call(app, "finishLaunching");
        MacNative.Call(app, "activateIgnoringOtherApps:", 1);
        return app;
    }

    /// <summary>Attaches the CEF protocol after its framework registers the native protocol object.</summary>
    internal static void AttachCefProtocol()
    {
        nint protocol = MacNative.GetProtocol("CefAppProtocol");
        if (protocol != 0)
        {
            _ = MacNative.AddProtocol(MacNative.Class("DeniceApplication"), protocol);
        }
    }

    private static void Add(nint cls, string selector, nint method, string encoding)
    {
        if (!MacNative.AddMethod(cls, MacNative.Selector(selector), method, encoding))
        {
            throw new InvalidOperationException($"Cannot register Cocoa method {selector}.");
        }
    }

    [UnmanagedCallersOnly]
    private static byte IsHandling(nint self, nint command) => _handlingEvent ? (byte)1 : (byte)0;

    [UnmanagedCallersOnly]
    private static void SetHandling(nint self, nint command, byte value) => _handlingEvent = value != 0;

    [UnmanagedCallersOnly]
    private static void Terminate(nint self, nint command, nint sender) => _quit = true;

    [UnmanagedCallersOnly]
    private static void SendEvent(nint self, nint command, nint evt)
    {
        bool previous = _handlingEvent;
        _handlingEvent = true;
        var super = new MacNative.Super { Receiver = self, Superclass = MacNative.Class("NSApplication") };
        MacNative.SendSuper(ref super, command, evt);
        _handlingEvent = previous;
    }
}
