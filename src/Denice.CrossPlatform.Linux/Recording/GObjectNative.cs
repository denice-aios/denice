// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Recording;

/// <summary>Binds GLib object metadata and typed property values.</summary>
internal static partial class GObjectNative
{
    /// <summary>Invokes g_type_check_instance_is_a.</summary>
    /// <param name="instance">The native instance argument.</param>
    /// <param name="type">The native type argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_type_check_instance_is_a")]
    internal static partial int IsInstance(nint instance, nuint type);

    /// <summary>Invokes g_object_class_find_property.</summary>
    /// <param name="objectClass">The native objectClass argument.</param>
    /// <param name="name">The native name argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_class_find_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint FindProperty(nint objectClass, string name);

    /// <summary>Invokes g_value_init.</summary>
    /// <param name="value">The native value argument.</param>
    /// <param name="type">The native type argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_init")]
    internal static partial nint InitializeValue(ref Value value, nuint type);

    /// <summary>Invokes g_value_unset.</summary>
    /// <param name="value">The native value argument.</param>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_unset")]
    internal static partial void UnsetValue(ref Value value);

    /// <summary>Invokes g_object_get_property.</summary>
    /// <param name="instance">The native instance argument.</param>
    /// <param name="name">The native name argument.</param>
    /// <param name="value">The native value argument.</param>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_get_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void GetProperty(nint instance, string name, ref Value value);

    /// <summary>Invokes g_value_get_uint.</summary>
    /// <param name="value">The native value argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_get_uint")]
    internal static partial uint GetUInt(in Value value);

    /// <summary>Invokes g_value_get_object.</summary>
    /// <param name="value">The native value argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_get_object")]
    internal static partial nint GetObject(nint value);

    /// <summary>Invokes g_type_fundamental.</summary>
    /// <param name="type">The native type argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_type_fundamental")]
    internal static partial nuint FundamentalType(nuint type);

    /// <summary>Invokes g_object_set_property.</summary>
    /// <param name="instance">The native instance argument.</param>
    /// <param name="name">The native name argument.</param>
    /// <param name="value">The native value argument.</param>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_set_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void SetProperty(nint instance, string name, in Value value);

    /// <summary>Invokes g_value_set_int.</summary>
    /// <param name="value">The native value argument.</param>
    /// <param name="item">The native item argument.</param>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_set_int")]
    internal static partial void SetInt(ref Value value, int item);

    /// <summary>Invokes g_value_set_uint.</summary>
    /// <param name="value">The native value argument.</param>
    /// <param name="item">The native item argument.</param>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_set_uint")]
    internal static partial void SetUInt(ref Value value, uint item);

    /// <summary>Invokes g_value_set_uint64.</summary>
    /// <param name="value">The native value argument.</param>
    /// <param name="item">The native item argument.</param>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_set_uint64")]
    internal static partial void SetUInt64(ref Value value, ulong item);

    /// <summary>Invokes g_value_set_boolean.</summary>
    /// <param name="value">The native value argument.</param>
    /// <param name="item">The native item argument.</param>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_set_boolean")]
    internal static partial void SetBoolean(ref Value value, int item);

    /// <summary>Invokes g_value_set_enum.</summary>
    /// <param name="value">The native value argument.</param>
    /// <param name="item">The native item argument.</param>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_value_set_enum")]
    internal static partial void SetEnum(ref Value value, int item);

    /// <summary>Invokes g_param_value_validate.</summary>
    /// <param name="parameter">The native parameter argument.</param>
    /// <param name="value">The native value argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_param_value_validate")]
    internal static partial int ValidateValue(nint parameter, ref Value value);

    /// <summary>Invokes g_param_values_cmp.</summary>
    /// <param name="parameter">The native parameter argument.</param>
    /// <param name="first">The native first argument.</param>
    /// <param name="second">The native second argument.</param>
    /// <returns>The native result.</returns>
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_param_values_cmp")]
    internal static partial int CompareValues(nint parameter, in Value first, in Value second);

    /// <summary>Provides the public GValue storage layout without inspecting its opaque payload.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Value
    {
        /// <summary>The value's GType.</summary>
        internal nuint Type;

        /// <summary>The first opaque payload union.</summary>
        internal ulong First;

        /// <summary>The second opaque payload union.</summary>
        internal ulong Second;
    }

    /// <summary>Matches the public GParamSpec header preceding private fields.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ParamSpecHeader
    {
        /// <summary>The GTypeInstance class pointer.</summary>
        internal nint Class;

        /// <summary>The interned property name.</summary>
        internal nint Name;

        /// <summary>The GParamFlags.</summary>
        internal uint Flags;

        /// <summary>The property's GType.</summary>
        internal nuint ValueType;

        /// <summary>The owner's GType.</summary>
        internal nuint OwnerType;
    }
}
