// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.MacOS.Desktop;

/// <summary>Objective-C message signatures used by the Cocoa window and Metal presenter.</summary>
internal static partial class MacNative
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    // x86_64 returns a 32-byte CGRect through hidden memory and requires objc_msgSend_stret; arm64 has no
    // objc_msgSend_stret and returns CGRect, a homogeneous aggregate of four doubles, in registers through objc_msgSend.
    // The runtime exports objc_msgSend_stret exactly where the indirect convention applies.
    private static readonly bool RectReturnedIndirectly = NativeLibrary.TryGetExport(NativeLibrary.Load(ObjC), "objc_msgSend_stret", out _);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="name">The native runtime name.</param>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_getProtocol", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetProtocol(string name);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="cls">The Objective-C class pointer.</param>
    /// <param name="protocol">The registered protocol pointer.</param>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport(ObjC, EntryPoint = "class_addProtocol")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool AddProtocol(nint cls, nint protocol);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="name">The native runtime name.</param>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetClass(string name);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="name">The native runtime name.</param>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Selector(string name);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="parent">The superclass pointer.</param>
    /// <param name="name">The native runtime name.</param>
    /// <param name="extra">The additional class storage bytes.</param>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_allocateClassPair", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint AllocateClass(nint parent, string name, nuint extra);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="cls">The Objective-C class pointer.</param>
    [LibraryImport(ObjC, EntryPoint = "objc_registerClassPair")]
    internal static partial void RegisterClass(nint cls);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="cls">The Objective-C class pointer.</param>
    /// <param name="selector">The Objective-C selector name.</param>
    /// <param name="implementation">The unmanaged callback pointer.</param>
    /// <param name="types">The Objective-C method encoding.</param>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport(ObjC, EntryPoint = "class_addMethod", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool AddMethod(nint cls, nint selector, nint implementation, string types);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="obj">The native receiver.</param>
    /// <param name="sel">The registered Objective-C selector.</param>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint obj, nint sel);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="obj">The native receiver.</param>
    /// <param name="sel">The registered Objective-C selector.</param>
    /// <param name="arg">The native message argument.</param>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint obj, nint sel, nint arg);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="obj">The native receiver.</param>
    /// <param name="sel">The registered Objective-C selector.</param>
    /// <param name="a">The first native argument.</param>
    /// <param name="b">The second native argument.</param>
    /// <param name="c">The third native argument.</param>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint obj, nint sel, nint a, nint b, nint c);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="obj">The native receiver.</param>
    /// <param name="sel">The registered Objective-C selector.</param>
    /// <param name="rect">The native rectangle.</param>
    /// <param name="style">The Cocoa window style mask.</param>
    /// <param name="backing">The Cocoa backing store type.</param>
    /// <param name="defer">Whether window creation is deferred.</param>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint Window(nint obj, nint sel, Rect rect, nuint style, nuint backing, byte defer);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="obj">The native receiver.</param>
    /// <param name="sel">The registered Objective-C selector.</param>
    /// <param name="size">The drawable dimensions.</param>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static partial void SetSize(nint obj, nint sel, Size size);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="obj">The native receiver.</param>
    /// <param name="sel">The registered Objective-C selector.</param>
    /// <param name="format">The native texture pixel format.</param>
    /// <param name="width">The pixel width.</param>
    /// <param name="height">The pixel height.</param>
    /// <param name="mipmapped">Whether to allocate mipmap levels.</param>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint TextureDescriptor(nint obj, nint sel, nuint format, nuint width, nuint height, byte mipmapped);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="obj">The native receiver.</param>
    /// <param name="sel">The registered Objective-C selector.</param>
    /// <param name="mask">The Cocoa event mask.</param>
    /// <param name="date">The event wait deadline.</param>
    /// <param name="mode">The Cocoa run-loop mode.</param>
    /// <param name="dequeue">Whether to dequeue the event.</param>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static partial nint NextEvent(nint obj, nint sel, nuint mask, nint date, nint mode, byte dequeue);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="obj">The native receiver.</param>
    /// <param name="sel">The registered Objective-C selector.</param>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static partial Point GetPoint(nint obj, nint sel);

    /// <summary>Reads an Objective-C floating-point property.</summary>
    /// <param name="obj">The native receiver.</param>
    /// <param name="sel">The registered property selector.</param>
    /// <returns>The property value.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static partial double GetDouble(nint obj, nint sel);

    /// <summary>Reads an Objective-C BOOL without interpreting unused return-register bits.</summary>
    /// <param name="obj">The native receiver.</param>
    /// <param name="sel">The registered property selector.</param>
    /// <returns>The native boolean byte.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static partial byte GetBool(nint obj, nint sel);

    /// <summary>Reads an Objective-C property that returns a rectangle, using the struct-return convention of the running architecture.</summary>
    /// <param name="rect">The native rectangle.</param>
    /// <param name="obj">The native receiver.</param>
    /// <param name="sel">The registered Objective-C selector.</param>
    internal static void GetRect(out Rect rect, nint obj, nint sel)
    {
        if (RectReturnedIndirectly)
        {
            GetRectIndirect(out rect, obj, sel);
        }
        else
        {
            rect = GetRectDirect(obj, sel);
        }
    }

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="super">The Objective-C superclass dispatch record.</param>
    /// <param name="sel">The registered Objective-C selector.</param>
    /// <param name="arg">The native message argument.</param>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSendSuper")]
    internal static partial void SendSuper(ref Super super, nint sel, nint arg);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="obj">The native receiver.</param>
    /// <param name="sel">The registered Objective-C selector.</param>
    /// <param name="utf8">The UTF-8 string.</param>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint String(nint obj, nint sel, string utf8);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <returns>The created object or native operation result.</returns>
    [LibraryImport("/System/Library/Frameworks/Metal.framework/Metal")]
    internal static partial nint MTLCreateSystemDefaultDevice();

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="obj">The native receiver.</param>
    /// <param name="selector">The Objective-C selector name.</param>
    /// <returns>The created object or native operation result.</returns>
    internal static nint Call(nint obj, string selector) => Send(obj, Selector(selector));

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="obj">The native receiver.</param>
    /// <param name="selector">The Objective-C selector name.</param>
    /// <param name="arg">The native message argument.</param>
    /// <returns>The created object or native operation result.</returns>
    internal static nint Call(nint obj, string selector, nint arg) => Send(obj, Selector(selector), arg);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="name">The native runtime name.</param>
    /// <returns>The created object or native operation result.</returns>
    internal static nint Class(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return GetClass(name);
    }

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="value">The managed text.</param>
    /// <returns>The created object or native operation result.</returns>
    internal static nint Text(string value) => String(Class("NSString"), Selector("stringWithUTF8String:"), value);

    /// <summary>Calls the native Cocoa or Metal operation with its ABI-compatible signature.</summary>
    /// <param name="obj">The native receiver.</param>
    internal static void Release(nint obj)
    {
        if (obj != 0)
        {
            Call(obj, "release");
        }
    }

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend_stret")]
    private static partial void GetRectIndirect(out Rect rect, nint obj, nint sel);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial Rect GetRectDirect(nint obj, nint sel);

    /// <summary>Native Cocoa value layout.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Point(double X, double Y);

    /// <summary>Native Cocoa value layout.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Size(double Width, double Height);

    /// <summary>Native Cocoa value layout.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Rect(Point Origin, Size Size);

    /// <summary>Native Cocoa value layout.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Super
    {
        /// <summary>The Objective-C dispatch pointer.</summary>
        internal nint Receiver;

        /// <summary>The Objective-C dispatch pointer.</summary>
        internal nint Superclass;
    }
}
