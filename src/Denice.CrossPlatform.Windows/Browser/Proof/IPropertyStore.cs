// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>IPropertyStore, in vtable order.</summary>
[GeneratedComInterface]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: the COM signature takes unmanaged pointers.")]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
internal unsafe partial interface IPropertyStore
{
    /// <summary>GetCount.</summary>
    /// <param name="count">Receives the count.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetCount(uint* count);

    /// <summary>GetAt.</summary>
    /// <param name="index">The index.</param>
    /// <param name="key">Receives the PROPERTYKEY.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetAt(uint index, void* key);

    /// <summary>GetValue.</summary>
    /// <param name="key">The PROPERTYKEY.</param>
    /// <param name="value">Receives the PROPVARIANT.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int GetValue(void* key, void* value);

    /// <summary>SetValue.</summary>
    /// <param name="key">The PROPERTYKEY.</param>
    /// <param name="value">The PROPVARIANT.</param>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int SetValue(void* key, void* value);

    /// <summary>Commit.</summary>
    /// <returns>The HRESULT.</returns>
    [PreserveSig]
    int Commit();
}
