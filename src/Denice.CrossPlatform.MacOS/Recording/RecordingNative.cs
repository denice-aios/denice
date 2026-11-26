// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.MacOS.Recording;

/// <summary>Typed Objective-C and Core Video calls used by the native MP4 writer.</summary>
internal static partial class RecordingNative
{
    /// <summary>The system AVFoundation framework binary.</summary>
    internal const string AvFoundation = "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string CoreVideo = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary>Invokes Send with the declared native ABI.</summary>
    /// <param name="receiver">The Objective-C receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    internal static nint Send(nint receiver, string selector) => Message(receiver, Selector(selector));

    /// <summary>Invokes Send with the declared native ABI.</summary>
    /// <param name="receiver">The Objective-C receiver.</param>
    /// <param name="selector">The registered Objective-C selector.</param>
    /// <param name="argument">The native argument.</param>
    /// <returns>The native result.</returns>
    internal static nint Send(nint receiver, string selector, nint argument) => MessageArgument(receiver, Selector(selector), argument);

    /// <summary>Sends a selector while retaining its media handles.</summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    internal static nint Send(MediaHandle receiver, string selector) => Message(receiver, Selector(selector));

    /// <summary>Sends a selector while retaining its media handles.</summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <param name="argument">The argument.</param>
    /// <returns>The native result.</returns>
    internal static nint Send(MediaHandle receiver, string selector, nint argument) => MessageArgument(receiver, Selector(selector), argument);

    /// <summary>Sends a selector while retaining its media handles.</summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <param name="argument">The argument.</param>
    /// <returns>The native result.</returns>
    internal static nint Send(MediaHandle receiver, string selector, MediaHandle argument) => MessageArgument(receiver, Selector(selector), argument);

    /// <summary>Invokes String with the declared native ABI.</summary>
    /// <param name="text">The UTF-8 string.</param>
    /// <returns>The native result.</returns>
    internal static nint String(string text) => StringMessage(GetClass("NSString"), Selector("stringWithUTF8String:"), text);

    /// <summary>Invokes Number with the declared native ABI.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The native result.</returns>
    internal static nint Number(long value) => Send(GetClass("NSNumber"), "numberWithLongLong:", (nint)value);

    /// <summary>Invokes Constant with the declared native ABI.</summary>
    /// <param name="library">The loaded framework handle.</param>
    /// <param name="symbol">The symbol.</param>
    /// <returns>The native result.</returns>
    internal static nint Constant(nint library, string symbol) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, symbol));

    /// <summary>Invokes GetClass with the declared native ABI.</summary>
    /// <param name="name">The runtime name.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial nint GetClass(string name);

    /// <summary>Invokes Selector with the declared native ABI.</summary>
    /// <param name="name">The runtime name.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial nint Selector(string name);

    /// <summary>Invokes Message with the declared native ABI.</summary>
    /// <param name="receiver">The Objective-C receiver.</param>
    /// <param name="selector">The registered Objective-C selector.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial nint Message(nint receiver, nint selector);

    /// <summary>Invokes Objective-C while retaining the owning media handles.</summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial nint Message(MediaHandle receiver, nint selector);

    /// <summary>Invokes MessageArgument with the declared native ABI.</summary>
    /// <param name="receiver">The Objective-C receiver.</param>
    /// <param name="selector">The registered Objective-C selector.</param>
    /// <param name="argument">The native argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial nint MessageArgument(nint receiver, nint selector, nint argument);

    /// <summary>Invokes Objective-C while retaining the owning media handles.</summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <param name="argument">The argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial nint MessageArgument(MediaHandle receiver, nint selector, nint argument);

    /// <summary>Invokes Objective-C while retaining the owning media handles.</summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <param name="argument">The argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial nint MessageArgument(MediaHandle receiver, nint selector, MediaHandle argument);

    /// <summary>Invokes StringMessage with the declared native ABI.</summary>
    /// <param name="receiver">The Objective-C receiver.</param>
    /// <param name="selector">The registered Objective-C selector.</param>
    /// <param name="text">The UTF-8 string.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial nint StringMessage(nint receiver, nint selector, string text);

    /// <summary>Invokes MessageTwo with the declared native ABI.</summary>
    /// <param name="receiver">The Objective-C receiver.</param>
    /// <param name="selector">The registered Objective-C selector.</param>
    /// <param name="first">The first native argument.</param>
    /// <param name="second">The second native argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial nint MessageTwo(nint receiver, nint selector, nint first, nint second);

    /// <summary>Invokes Objective-C while retaining the owning media handles.</summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <param name="first">The first.</param>
    /// <param name="second">The second.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial nint MessageTwo(nint receiver, nint selector, MediaHandle first, nint second);

    /// <summary>Invokes InitializeWriter with the declared native ABI.</summary>
    /// <param name="receiver">The Objective-C receiver.</param>
    /// <param name="selector">The registered Objective-C selector.</param>
    /// <param name="url">The destination file URL.</param>
    /// <param name="fileType">The exported MP4 file type.</param>
    /// <param name="error">The returned native error.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial nint InitializeWriter(nint receiver, nint selector, nint url, nint fileType, out nint error);

    /// <summary>Invokes BoolMessage with the declared native ABI.</summary>
    /// <param name="receiver">The Objective-C receiver.</param>
    /// <param name="selector">The registered Objective-C selector.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial byte BoolMessage(MediaHandle receiver, nint selector);

    /// <summary>Invokes BoolArgument with the declared native ABI.</summary>
    /// <param name="receiver">The Objective-C receiver.</param>
    /// <param name="selector">The registered Objective-C selector.</param>
    /// <param name="argument">The native argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial byte BoolArgument(MediaHandle receiver, nint selector, nint argument);

    /// <summary>Invokes Objective-C while retaining the owning media handles.</summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <param name="argument">The argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial byte BoolArgument(MediaHandle receiver, nint selector, MediaHandle argument);

    /// <summary>Invokes TimeMessage with the declared native ABI.</summary>
    /// <param name="receiver">The Objective-C receiver.</param>
    /// <param name="selector">The registered Objective-C selector.</param>
    /// <param name="time">The media timestamp.</param>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial void TimeMessage(MediaHandle receiver, nint selector, MediaTime time);

    /// <summary>Invokes AppendPixelBuffer with the declared native ABI.</summary>
    /// <param name="receiver">The Objective-C receiver.</param>
    /// <param name="selector">The registered Objective-C selector.</param>
    /// <param name="buffer">The pixel buffer.</param>
    /// <param name="time">The media timestamp.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial byte AppendPixelBuffer(MediaHandle receiver, nint selector, nint buffer, MediaTime time);

    /// <summary>Invokes CVPixelBufferCreate with the declared native ABI.</summary>
    /// <param name="allocator">The Core Foundation allocator, or zero for default.</param>
    /// <param name="width">The pixel width.</param>
    /// <param name="height">The pixel height.</param>
    /// <param name="format">The native media format.</param>
    /// <param name="attributes">The optional pixel buffer attributes.</param>
    /// <param name="buffer">The pixel buffer.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(CoreVideo)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int CVPixelBufferCreate(nint allocator, nuint width, nuint height, uint format, nint attributes, out nint buffer);

    /// <summary>Invokes CVPixelBufferLockBaseAddress with the declared native ABI.</summary>
    /// <param name="buffer">The pixel buffer.</param>
    /// <param name="flags">The native operation flags.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(CoreVideo)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int CVPixelBufferLockBaseAddress(nint buffer, nuint flags);

    /// <summary>Invokes CVPixelBufferUnlockBaseAddress with the declared native ABI.</summary>
    /// <param name="buffer">The pixel buffer.</param>
    /// <param name="flags">The native operation flags.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(CoreVideo)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int CVPixelBufferUnlockBaseAddress(nint buffer, nuint flags);

    /// <summary>Invokes CVPixelBufferGetBaseAddress with the declared native ABI.</summary>
    /// <param name="buffer">The pixel buffer.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(CoreVideo)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial nint CVPixelBufferGetBaseAddress(nint buffer);

    /// <summary>Invokes CVPixelBufferGetBytesPerRow with the declared native ABI.</summary>
    /// <param name="buffer">The pixel buffer.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(CoreVideo)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial nuint CVPixelBufferGetBytesPerRow(nint buffer);

    /// <summary>Invokes CFRelease with the declared native ABI.</summary>
    /// <param name="value">The native value.</param>
    [LibraryImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial void CFRelease(nint value);

    /// <summary>Invokes CopyMemory with the declared native ABI.</summary>
    /// <param name="destination">The destination address.</param>
    /// <param name="source">The source address.</param>
    /// <param name="length">The byte length.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "memmove")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial nint CopyMemory(nint destination, nint source, nuint length);
}
