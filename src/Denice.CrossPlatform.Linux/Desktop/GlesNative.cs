// Copyright (c) Denice.ai. All rights reserved.

using System.Globalization;
using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Desktop;

/// <summary>
/// The Linux desktop's ten OpenGL ES functions, resolved once per process through EGL's eglGetProcAddress after the EGL
/// display is initialized, instead of linking libGLESv2.so.2 by name (approved: document-e613bd2d…, revision 4bd7edc0…).
/// EGL guarantees core client-API functions through eglGetProcAddress from EGL 1.5, or with
/// EGL_KHR_get_all_proc_addresses / EGL_KHR_client_get_all_proc_addresses; the GLES3 context the desktop requires is the
/// client-API version check that guarantee asks for. Resolved pointers are independent of the display and context.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "GLES entry points resolved through eglGetProcAddress are called through unmanaged function pointers with the platform C convention.")]
internal static unsafe class GlesNative
{
    private const int EglVersion = 0x3054;
    private const int EglExtensions = 0x3055;
    private const string DisplayExtension = "EGL_KHR_get_all_proc_addresses";
    private const string ClientExtension = "EGL_KHR_client_get_all_proc_addresses";
    private static readonly Lock Gate = new();
    private static delegate* unmanaged<uint, nint> getString;
    private static delegate* unmanaged<uint> getError;
    private static delegate* unmanaged<int, uint*, void> genTextures;
    private static delegate* unmanaged<int, uint*, void> deleteTextures;
    private static delegate* unmanaged<uint, uint, void> bindTexture;
    private static delegate* unmanaged<uint, uint, int, void> texParameter;
    private static delegate* unmanaged<void> finish;
    private static delegate* unmanaged<uint, int*, void> getInteger;
    private static delegate* unmanaged<uint, uint, nint> getStringAt;
    private static delegate* unmanaged<uint, int, uint, int, int, void> textureStorage;
    private static volatile bool bound;

    /// <summary>Gets the client EGL version string read when the functions were bound, or empty before.</summary>
    internal static string ClientVersion { get; private set; } = string.Empty;

    /// <summary>Gets the client EGL extension string read when the functions were bound, or empty before.</summary>
    internal static string ClientExtensions { get; private set; } = string.Empty;

    /// <summary>
    /// Resolves the ten functions, once per process, after eglInitialize. Stops start-up with a message naming the EGL
    /// requirement when EGL does not guarantee core functions through eglGetProcAddress or a function is not exposed.
    /// </summary>
    /// <param name="displayExtensions">The initialized display's EGL_EXTENSIONS string.</param>
    internal static void Bind(string displayExtensions)
    {
        ArgumentNullException.ThrowIfNull(displayExtensions);
        lock (Gate)
        {
            if (bound)
            {
                return;
            }

            // EGL_NO_DISPLAY: the client version and, from EGL 1.5 or with EGL_EXT_client_extensions, the client extensions.
            ClientVersion = Marshal.PtrToStringUTF8(EglNative.QueryString(0, EglVersion)) ?? string.Empty;
            ClientExtensions = Marshal.PtrToStringUTF8(EglNative.QueryString(0, EglExtensions)) ?? string.Empty;
            RequireAllProcAddresses(ClientVersion, ClientExtensions, displayExtensions);
            getString = (delegate* unmanaged<uint, nint>)Require("glGetString");
            getError = (delegate* unmanaged<uint>)Require("glGetError");
            genTextures = (delegate* unmanaged<int, uint*, void>)Require("glGenTextures");
            deleteTextures = (delegate* unmanaged<int, uint*, void>)Require("glDeleteTextures");
            bindTexture = (delegate* unmanaged<uint, uint, void>)Require("glBindTexture");
            texParameter = (delegate* unmanaged<uint, uint, int, void>)Require("glTexParameteri");
            finish = (delegate* unmanaged<void>)Require("glFinish");
            getInteger = (delegate* unmanaged<uint, int*, void>)Require("glGetIntegerv");
            getStringAt = (delegate* unmanaged<uint, uint, nint>)Require("glGetStringi");
            textureStorage = (delegate* unmanaged<uint, int, uint, int, int, void>)Require("glTexStorage2D");
            bound = true;
        }
    }

    /// <summary>Whether EGL guarantees core client-API functions through eglGetProcAddress.</summary>
    /// <param name="clientVersion">The EGL_NO_DISPLAY EGL_VERSION string, such as "1.5 libglvnd".</param>
    /// <param name="clientExtensions">The EGL_NO_DISPLAY EGL_EXTENSIONS string, or empty.</param>
    /// <param name="displayExtensions">The display's EGL_EXTENSIONS string.</param>
    /// <returns>Whether EGL 1.5 or either get-all-proc-addresses extension holds.</returns>
    internal static bool GuaranteesAllProcAddresses(string clientVersion, string clientExtensions, string displayExtensions) =>
        AtLeast15(clientVersion) || HasExtension(clientExtensions, ClientExtension) || HasExtension(displayExtensions, DisplayExtension);

    /// <summary>Invokes glGetString.</summary>
    /// <param name="name">The native name argument.</param>
    /// <returns>The native result.</returns>
    internal static nint GetString(uint name)
    {
        EnsureBound();
        return getString(name);
    }

    /// <summary>Invokes glGetError.</summary>
    /// <returns>The native result.</returns>
    internal static uint GetError()
    {
        EnsureBound();
        return getError();
    }

    /// <summary>Invokes glGenTextures for one texture.</summary>
    /// <param name="count">The texture count, which must be one.</param>
    /// <param name="texture">The generated texture.</param>
    internal static void GenTextures(int count, out uint texture)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(count, 1);
        uint value = 0;
        EnsureBound();
        genTextures(count, &value);
        texture = value;
    }

    /// <summary>Invokes glDeleteTextures for one texture.</summary>
    /// <param name="count">The texture count, which must be one.</param>
    /// <param name="texture">The texture to delete.</param>
    internal static void DeleteTextures(int count, in uint texture)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(count, 1);
        uint value = texture;
        EnsureBound();
        deleteTextures(count, &value);
    }

    /// <summary>Invokes glBindTexture.</summary>
    /// <param name="target">The native target argument.</param>
    /// <param name="texture">The native texture argument.</param>
    internal static void BindTexture(uint target, uint texture)
    {
        EnsureBound();
        bindTexture(target, texture);
    }

    /// <summary>Invokes glTexParameteri.</summary>
    /// <param name="target">The native target argument.</param>
    /// <param name="name">The native name argument.</param>
    /// <param name="value">The native value argument.</param>
    internal static void TexParameter(uint target, uint name, int value)
    {
        EnsureBound();
        texParameter(target, name, value);
    }

    /// <summary>Invokes glFinish.</summary>
    internal static void Finish()
    {
        EnsureBound();
        finish();
    }

    /// <summary>Queries a GLES integer.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The returned value.</param>
    internal static void GetInteger(uint name, out int value)
    {
        int result = 0;
        EnsureBound();
        getInteger(name, &result);
        value = result;
    }

    /// <summary>Queries an indexed GLES string (OpenGL ES 3.0).</summary>
    /// <param name="name">The string type.</param>
    /// <param name="index">The element index.</param>
    /// <returns>The borrowed string pointer.</returns>
    internal static nint GetStringAt(uint name, uint index)
    {
        EnsureBound();
        return getStringAt(name, index);
    }

    /// <summary>Allocates immutable storage for an owned GPU texture (OpenGL ES 3.0).</summary>
    /// <param name="target">The texture target.</param>
    /// <param name="levels">The mipmap level count.</param>
    /// <param name="format">The sized internal format.</param>
    /// <param name="width">The pixel width.</param>
    /// <param name="height">The pixel height.</param>
    internal static void TextureStorage(uint target, int levels, uint format, int width, int height)
    {
        EnsureBound();
        textureStorage(target, levels, format, width, height);
    }

    private static void RequireAllProcAddresses(string clientVersion, string clientExtensions, string displayExtensions)
    {
        if (!GuaranteesAllProcAddresses(clientVersion, clientExtensions, displayExtensions))
        {
            throw new NotSupportedException(string.Create(CultureInfo.InvariantCulture, $"DENICE requires EGL 1.5, {ClientExtension} or {DisplayExtension} to resolve OpenGL ES functions through eglGetProcAddress; this EGL reports client version \"{clientVersion}\" without either extension."));
        }
    }

    private static bool AtLeast15(string version)
    {
        // "major.minor" followed by vendor text, per EGL section 3.3.
        string[] parts = version.Split(' ', 2)[0].Split('.');
        return parts.Length >= 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
            && (major > 1 || (major == 1 && minor >= 5));
    }

    private static bool HasExtension(string extensions, string name) =>
        extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal);

    private static nint Require(string name)
    {
        nint pointer = EglNative.GetProcAddress(name);
        return pointer != 0 ? pointer : throw new NotSupportedException("EGL did not expose " + name + ".");
    }

    private static void EnsureBound()
    {
        // A GLES call before LinuxGpuContext has initialized EGL is a programming error, not a missing library.
        if (!bound)
        {
            throw new InvalidOperationException("OpenGL ES functions were used before EGL initialization bound them.");
        }
    }
}
