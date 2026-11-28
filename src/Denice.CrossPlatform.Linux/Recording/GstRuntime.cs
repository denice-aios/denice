// Copyright (c) Denice.ai. All rights reserved.

using System.Reflection;
using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Recording;

/// <summary>Initializes the published media prefix through native APIs.</summary>
internal static class GstRuntime
{
    private static readonly System.Threading.Lock Gate = new();
    private static string _baseDirectory = string.Empty;
    private static bool _initialized;
    private static bool _resolverInstalled;

    /// <summary>Loads the packaged media libraries and discovers their plugins.</summary>
    /// <param name="baseDirectory">The published directory supplied by the environment owner.</param>
    internal static void Initialize(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDirectory);
        if (!baseDirectory.StartsWith('/'))
        {
            throw new ArgumentException("The published Linux directory must be absolute.", nameof(baseDirectory));
        }

        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            _baseDirectory = baseDirectory;
            if (!_resolverInstalled)
            {
                NativeLibrary.SetDllImportResolver(typeof(GstRuntime).Assembly, Resolve);
                _resolverInstalled = true;
            }

            // The media prefix carries no subprocess scanner. Scan its plugins
            // inside this process, with the same relocated library closure.
            GstNative.SetRegistryFork(0);
            if (InitializeNative(out nint error) == 0)
            {
                throw ReadError(error, "GStreamer initialization failed.");
            }

            string plugins = Path.Combine(_baseDirectory, "media", "lib", "gstreamer-1.0");

            // False means unchanged, including an already-current registry.
            _ = GstNative.ScanRegistry(GstNative.GetRegistry(), plugins);

            _initialized = true;
        }
    }

    /// <summary>Reads and frees a GLib error returned by a native operation.</summary>
    /// <param name="error">The owned GError pointer, or zero.</param>
    /// <param name="fallback">The message when native details are unavailable.</param>
    /// <returns>The operation failure.</returns>
    internal static InvalidOperationException ReadError(nint error, string fallback)
    {
        if (error == 0)
        {
            return new InvalidOperationException(fallback);
        }

        try
        {
            // GError: GQuark (uint32), code (int32), message (pointer).
            string? message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8));
            return new InvalidOperationException(message ?? fallback);
        }
        finally
        {
            GstNative.FreeError(error);
        }
    }

    /// <summary>Creates an element only from its expected packaged plugin.</summary>
    /// <param name="factoryName">The exact selected element factory.</param>
    /// <param name="pluginFilename">The expected plugin basename in the packaged prefix.</param>
    /// <param name="instanceName">The pipeline instance name.</param>
    /// <returns>The owned floating element reference.</returns>
    internal static nint CreateElement(string factoryName, string pluginFilename, string instanceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(factoryName);
        ArgumentException.ThrowIfNullOrEmpty(pluginFilename);
        ArgumentException.ThrowIfNullOrEmpty(instanceName);
        if (!string.Equals(Path.GetFileName(pluginFilename), pluginFilename, StringComparison.Ordinal))
        {
            throw new ArgumentException("The expected plugin must be a basename.", nameof(pluginFilename));
        }

        lock (Gate)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("Initialize packaged GStreamer before creating elements.");
            }

            nint factory = GstNative.FindElementFactory(factoryName);
            if (factory == 0)
            {
                throw new NotSupportedException("The packaged GStreamer runtime has no usable " + factoryName + " factory.");
            }

            nint loaded = 0;
            try
            {
                RequirePackagedPlugin(factory, factoryName, pluginFilename);
                loaded = GstNative.LoadFeature(factory);
                if (loaded == 0)
                {
                    throw new NotSupportedException("GStreamer could not load the packaged " + factoryName + " factory.");
                }

                RequirePackagedPlugin(loaded, factoryName, pluginFilename);
                nint element = GstNative.CreateElement(loaded, instanceName);
                return element != 0 ? element : throw new NotSupportedException("GStreamer could not create " + factoryName + ".");
            }
            finally
            {
                if (loaded != 0)
                {
                    GstNative.Unref(loaded);
                }

                GstNative.Unref(factory);
            }
        }
    }

    private static void RequirePackagedPlugin(nint factory, string factoryName, string pluginFilename)
    {
        nint plugin = GstNative.GetFeaturePlugin(factory);
        if (plugin == 0)
        {
            throw new NotSupportedException("GStreamer cannot identify the plugin for " + factoryName + ".");
        }

        try
        {
            string? actual = Marshal.PtrToStringUTF8(GstNative.GetPluginFilename(plugin));
            string expected = Path.Combine(_baseDirectory, "media", "lib", "gstreamer-1.0", pluginFilename);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new NotSupportedException("GStreamer selected " + factoryName + " outside its expected packaged plugin: " + actual);
            }
        }
        finally
        {
            GstNative.Unref(plugin);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "GStreamer consumes a bounded, null-terminated argv through its published initialization ABI.")]
    private static unsafe int InitializeNative(out nint error)
    {
        ReadOnlySpan<byte> executable = "denice\0"u8;
        ReadOnlySpan<byte> noUpdate = "--gst-disable-registry-update\0"u8;
        ReadOnlySpan<byte> noFork = "--gst-disable-registry-fork\0"u8;
        fixed (byte* name = executable)
        {
            fixed (byte* update = noUpdate)
            {
                fixed (byte* fork = noFork)
                {
                    nint* values = stackalloc nint[4] { (nint)name, (nint)update, (nint)fork, 0 };
                    nint argv = (nint)values;
                    int argc = 3;
                    return GstNative.Initialize((nint)(&argc), (nint)(&argv), out error);
                }
            }
        }
    }

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name is "libgstreamer-1.0.so.0" or "libgstapp-1.0.so.0" or "libgstvideo-1.0.so.0"
            or "libgstgl-1.0.so.0" or "libgstcuda-1.0.so.0" or "libglib-2.0.so.0" or "libgobject-2.0.so.0")
        {
            return NativeLibrary.Load(Path.Combine(_baseDirectory, "media", "lib", name), assembly, searchPath);
        }

        return 0;
    }
}
