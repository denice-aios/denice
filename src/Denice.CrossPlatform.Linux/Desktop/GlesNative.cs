// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Desktop;

/// <summary>Binds the system libGLESv2.so.2 native API.</summary>
internal static partial class GlesNative
{
    /// <summary>Invokes glGetString.</summary>
    /// <param name="name">The native name argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libGLESv2.so.2", EntryPoint = "glGetString")]
    internal static partial nint GetString(uint name);

    /// <summary>Invokes glGetError.</summary>
    /// <returns>The native result.</returns>
    [LibraryImport("libGLESv2.so.2", EntryPoint = "glGetError")]
    internal static partial uint GetError();

    /// <summary>Invokes glGenTextures.</summary>
    /// <param name="count">The native count argument.</param>
    /// <param name="texture">The native texture argument.</param>
    [LibraryImport("libGLESv2.so.2", EntryPoint = "glGenTextures")]
    internal static partial void GenTextures(int count, out uint texture);

    /// <summary>Invokes glDeleteTextures.</summary>
    /// <param name="count">The native count argument.</param>
    /// <param name="texture">The native texture argument.</param>
    [LibraryImport("libGLESv2.so.2", EntryPoint = "glDeleteTextures")]
    internal static partial void DeleteTextures(int count, in uint texture);

    /// <summary>Invokes glBindTexture.</summary>
    /// <param name="target">The native target argument.</param>
    /// <param name="texture">The native texture argument.</param>
    [LibraryImport("libGLESv2.so.2", EntryPoint = "glBindTexture")]
    internal static partial void BindTexture(uint target, uint texture);

    /// <summary>Invokes glTexParameteri.</summary>
    /// <param name="target">The native target argument.</param>
    /// <param name="name">The native name argument.</param>
    /// <param name="value">The native value argument.</param>
    [LibraryImport("libGLESv2.so.2", EntryPoint = "glTexParameteri")]
    internal static partial void TexParameter(uint target, uint name, int value);

    /// <summary>Invokes glFinish.</summary>
    [LibraryImport("libGLESv2.so.2", EntryPoint = "glFinish")]
    internal static partial void Finish();

    /// <summary>Queries a GLES integer.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The returned value.</param>
    [LibraryImport("libGLESv2.so.2", EntryPoint = "glGetIntegerv")]
    internal static partial void GetInteger(uint name, out int value);

    /// <summary>Queries an indexed GLES string.</summary>
    /// <param name="name">The string type.</param>
    /// <param name="index">The element index.</param>
    /// <returns>The borrowed string pointer.</returns>
    [LibraryImport("libGLESv2.so.2", EntryPoint = "glGetStringi")]
    internal static partial nint GetStringAt(uint name, uint index);

    /// <summary>Allocates immutable storage for an owned GPU texture.</summary>
    /// <param name="target">The texture target.</param>
    /// <param name="levels">The mipmap level count.</param>
    /// <param name="format">The sized internal format.</param>
    /// <param name="width">The pixel width.</param>
    /// <param name="height">The pixel height.</param>
    [LibraryImport("libGLESv2.so.2", EntryPoint = "glTexStorage2D")]
    internal static partial void TextureStorage(uint target, int levels, uint format, int width, int height);
}
