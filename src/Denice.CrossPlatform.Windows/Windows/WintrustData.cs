// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Windows;

/// <summary>
/// The blittable <c>WINTRUST_DATA</c> passed to <c>WinVerifyTrust</c>. Field order and types mirror the Windows SDK
/// header (<c>wintrust.h</c>) exactly — the anonymous union is its <c>pFile</c> member, the only one used — and
/// sequential layout reproduces its native alignment on 32- and 64-bit Windows.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WintrustData
{
    /// <summary>cbStruct: the size of this structure in bytes.</summary>
    public uint StructSize;

    /// <summary>pPolicyCallbackData: unused (<see cref="IntPtr.Zero"/>).</summary>
    public IntPtr PolicyCallbackData;

    /// <summary>pSIPClientData: unused (<see cref="IntPtr.Zero"/>).</summary>
    public IntPtr SipClientData;

    /// <summary>dwUIChoice: <c>WTD_UI_NONE</c>.</summary>
    public uint UiChoice;

    /// <summary>fdwRevocationChecks: <c>WTD_REVOKE_NONE</c>.</summary>
    public uint RevocationChecks;

    /// <summary>dwUnionChoice: <c>WTD_CHOICE_FILE</c>.</summary>
    public uint UnionChoice;

    /// <summary>pFile: the <see cref="WintrustFileInfo"/> naming the file.</summary>
    public IntPtr File;

    /// <summary>dwStateAction: <c>WTD_STATEACTION_VERIFY</c>, then <c>WTD_STATEACTION_CLOSE</c>.</summary>
    public uint StateAction;

    /// <summary>hWVTStateData: the verification state kept between the verify and close calls.</summary>
    public IntPtr StateData;

    /// <summary>pwszURLReference: unused (<see cref="IntPtr.Zero"/>).</summary>
    public IntPtr UrlReference;

    /// <summary>dwProvFlags: the provider flags.</summary>
    public uint ProviderFlags;

    /// <summary>dwUIContext: unused (0).</summary>
    public uint UiContext;

    /// <summary>pSignatureSettings: unused (<see cref="IntPtr.Zero"/>).</summary>
    public IntPtr SignatureSettings;
}
