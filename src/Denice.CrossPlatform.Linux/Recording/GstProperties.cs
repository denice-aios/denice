// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Recording;

/// <summary>Checks native property types, ranges and applied values before media processing begins.</summary>
internal static class GstProperties
{
    private delegate void ValueSetter<in T>(ref GObjectNative.Value value, T item);

    /// <summary>Sets and checks an integer property.</summary>
    /// <param name="instance">The native object.</param>
    /// <param name="name">The property.</param>
    /// <param name="item">The requested value.</param>
    internal static void SetInt(nint instance, string name, int item) => Set(instance, name, item, 24, GObjectNative.SetInt);

    /// <summary>Sets and checks an unsigned integer property.</summary>
    /// <param name="instance">The native object.</param>
    /// <param name="name">The property.</param>
    /// <param name="item">The requested value.</param>
    internal static void SetUInt(nint instance, string name, uint item) => Set(instance, name, item, 28, GObjectNative.SetUInt);

    /// <summary>Sets and checks an unsigned 64-bit property.</summary>
    /// <param name="instance">The native object.</param>
    /// <param name="name">The property.</param>
    /// <param name="item">The requested value.</param>
    internal static void SetUInt64(nint instance, string name, ulong item) => Set(instance, name, item, 44, GObjectNative.SetUInt64);

    /// <summary>Sets and checks a boolean property.</summary>
    /// <param name="instance">The native object.</param>
    /// <param name="name">The property.</param>
    /// <param name="item">The requested value.</param>
    internal static void SetBoolean(nint instance, string name, bool item) => Set(instance, name, item ? 1 : 0, 20, GObjectNative.SetBoolean);

    /// <summary>Sets and checks an enum property using its actual registered GType.</summary>
    /// <param name="instance">The native object.</param>
    /// <param name="name">The property.</param>
    /// <param name="item">The requested enum value.</param>
    internal static void SetEnum(nint instance, string name, int item) => Set(instance, name, item, 48, GObjectNative.SetEnum);

    /// <summary>Reads an unsigned property, optionally requiring a fixed device association.</summary>
    /// <param name="instance">The native object.</param>
    /// <param name="name">The property.</param>
    /// <param name="requireReadOnly">Whether writable device selectors are rejected.</param>
    /// <returns>The property value, or null for an incompatible property.</returns>
    internal static uint? ReadUInt(nint instance, string name, bool requireReadOnly)
    {
        nint parameter = Find(instance, name);
        if (parameter == 0)
        {
            return null;
        }

        var header = Marshal.PtrToStructure<GObjectNative.ParamSpecHeader>(parameter);
        if (header.ValueType != 28 || (header.Flags & 1) == 0 || (requireReadOnly && (header.Flags & 2) != 0))
        {
            return null;
        }

        GObjectNative.Value value = default;
        _ = GObjectNative.InitializeValue(ref value, header.ValueType);
        try
        {
            GObjectNative.GetProperty(instance, name, ref value);
            return GObjectNative.GetUInt(in value);
        }
        finally
        {
            GObjectNative.UnsetValue(ref value);
        }
    }

    private static nint Find(nint instance, string name) => GObjectNative.FindProperty(Marshal.ReadIntPtr(instance), name);

    private static void Set<T>(nint instance, string name, T item, nuint fundamental, ValueSetter<T> setter)
    {
        nint parameter = Find(instance, name);
        if (parameter == 0)
        {
            throw new NotSupportedException("The selected media element has no " + name + " property.");
        }

        var header = Marshal.PtrToStructure<GObjectNative.ParamSpecHeader>(parameter);
        if ((header.Flags & 3) != 3 || (header.Flags & 8) != 0 || GObjectNative.FundamentalType(header.ValueType) != fundamental)
        {
            throw new NotSupportedException("The media property has incompatible type or access: " + name);
        }

        GObjectNative.Value value = default;
        GObjectNative.Value actual = default;
        _ = GObjectNative.InitializeValue(ref value, header.ValueType);
        _ = GObjectNative.InitializeValue(ref actual, header.ValueType);
        try
        {
            setter(ref value, item);
            if (GObjectNative.ValidateValue(parameter, ref value) != 0)
            {
                throw new NotSupportedException("The requested media property is out of range: " + name);
            }

            GObjectNative.SetProperty(instance, name, in value);
            GObjectNative.GetProperty(instance, name, ref actual);
            if (GObjectNative.CompareValues(parameter, in value, in actual) != 0)
            {
                throw new InvalidOperationException("The selected media element did not apply " + name + ".");
            }
        }
        finally
        {
            GObjectNative.UnsetValue(ref actual);
            GObjectNative.UnsetValue(ref value);
        }
    }
}
