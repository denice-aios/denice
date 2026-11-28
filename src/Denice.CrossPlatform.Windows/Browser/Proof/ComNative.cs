// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>
/// DENICE's own COM and kernel32 calls for the Windows redirection proof: its own ole32 CoCreateInstance (not libcef.dll's
/// patched slot), CoTaskMem allocation, IUnknown calls through the vtable, and source-generated COM wrappers that expose
/// DENICE objects to Chromium and wrap the real objects DENICE delegates to.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: COM vtable calls take unmanaged pointers.")]
internal static unsafe partial class ComNative
{
    /// <summary>S_OK.</summary>
    internal const int Ok = 0;

    /// <summary>E_NOINTERFACE.</summary>
    internal const int NoInterface = unchecked((int)0x80004002);

    /// <summary>E_NOTIMPL.</summary>
    internal const int NotImplemented = unchecked((int)0x80004001);

    /// <summary>E_POINTER.</summary>
    internal const int InvalidPointer = unchecked((int)0x80004003);

    /// <summary>E_FAIL.</summary>
    internal const int Failed = unchecked((int)0x80004005);

    /// <summary>HRESULT_FROM_WIN32(ERROR_NOT_FOUND), what GetDefaultAudioEndpoint returns with no endpoint.</summary>
    internal const int NotFound = unchecked((int)0x80070490);

    /// <summary>CLSCTX_ALL.</summary>
    internal const uint AllContexts = 0x17;

    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>Gets the COM interface pointer of a DENICE object, with one reference for the caller.</summary>
    /// <param name="instance">The object.</param>
    /// <param name="iid">The interface.</param>
    /// <returns>The pointer, or zero if the object does not implement it.</returns>
    internal static nint Expose(object instance, Guid iid)
    {
        nint unknown = Wrappers.GetOrCreateComInterfaceForObject(instance, CreateComInterfaceFlags.None);
        nint result = QueryInterface(unknown, iid);
        _ = Release(unknown);
        return result;
    }

    /// <summary>Wraps a real COM object for managed calls; the wrapper takes its own reference.</summary>
    /// <typeparam name="T">The generated COM interface.</typeparam>
    /// <param name="pointer">The object.</param>
    /// <returns>The wrapper.</returns>
    internal static T Wrap<T>(nint pointer)
        where T : class => (T)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);

    /// <summary>Calls IUnknown::QueryInterface.</summary>
    /// <param name="unknown">The object.</param>
    /// <param name="iid">The interface.</param>
    /// <returns>The interface with one reference, or zero.</returns>
    internal static nint QueryInterface(nint unknown, Guid iid)
    {
        nint result = 0;
        int status = ((delegate* unmanaged[MemberFunction]<nint, Guid*, nint*, int>)(*(nint**)unknown)[0])(unknown, &iid, &result);
        return status == Ok ? result : 0;
    }

    /// <summary>Calls IUnknown::Release.</summary>
    /// <param name="unknown">The object.</param>
    /// <returns>The remaining count.</returns>
    internal static uint Release(nint unknown) => unknown == 0 ? 0 : ((delegate* unmanaged[MemberFunction]<nint, uint>)(*(nint**)unknown)[2])(unknown);

    /// <summary>Calls a vtable method that takes one pointer and one int and returns an HRESULT.</summary>
    /// <param name="instance">The object.</param>
    /// <param name="slot">The vtable slot.</param>
    /// <param name="pointer">The pointer argument.</param>
    /// <param name="value">The int argument.</param>
    /// <returns>The HRESULT.</returns>
    internal static int Call(nint instance, int slot, nint pointer, int value) =>
        ((delegate* unmanaged[MemberFunction]<nint, nint, int, int>)(*(nint**)instance)[slot])(instance, pointer, value);

    /// <summary>Copies a string into CoTaskMem, as GetId returns it.</summary>
    /// <param name="text">The string.</param>
    /// <returns>The CoTaskMem copy.</returns>
    internal static nint TaskString(string text)
    {
        nint memory = AllocateTaskMemory((nuint)((text.Length + 1) * 2));
        fixed (char* source = text)
        {
            new ReadOnlySpan<char>(source, text.Length).CopyTo(new Span<char>((void*)memory, text.Length));
        }

        ((char*)memory)[text.Length] = '\0';
        return memory;
    }

    /// <summary>Writes a WAVEFORMATEXTENSIBLE into CoTaskMem.</summary>
    /// <param name="rate">The rate.</param>
    /// <param name="channels">The channels.</param>
    /// <param name="bits">The bits per sample.</param>
    /// <param name="floating">Whether the samples are IEEE float.</param>
    /// <returns>The CoTaskMem format.</returns>
    internal static nint TaskFormat(int rate, int channels, int bits, bool floating)
    {
        nint memory = AllocateTaskMemory(40);
        byte* format = (byte*)memory;
        new Span<byte>(format, 40).Clear();
        *(ushort*)format = 0xFFFE;
        *(ushort*)(format + 2) = (ushort)channels;
        *(uint*)(format + 4) = (uint)rate;
        *(uint*)(format + 8) = (uint)(rate * channels * bits / 8);
        *(ushort*)(format + 12) = (ushort)(channels * bits / 8);
        *(ushort*)(format + 14) = (ushort)bits;
        *(ushort*)(format + 16) = 22;
        *(ushort*)(format + 18) = (ushort)bits;
        *(uint*)(format + 20) = channels == 1 ? 4u : 3u;
        *(Guid*)(format + 24) = floating ? new Guid("00000003-0000-0010-8000-00aa00389b71") : new Guid("00000001-0000-0010-8000-00aa00389b71");
        return memory;
    }

    /// <summary>Gets the QPC time in 100 ns units from the injected clock (on Windows its timestamps are QPC ticks).</summary>
    /// <param name="clock">The clock.</param>
    /// <returns>The time.</returns>
    internal static ulong Counter100ns(TimeProvider clock) => (ulong)(clock.GetTimestamp() * (10_000_000.0 / clock.TimestampFrequency));

    /// <summary>DENICE's own CoCreateInstance; libcef.dll's patched delay-load slot is never involved.</summary>
    /// <param name="clsid">The class.</param>
    /// <param name="outer">The aggregating object.</param>
    /// <param name="context">The CLSCTX.</param>
    /// <param name="iid">The interface.</param>
    /// <param name="instance">Receives the object.</param>
    /// <returns>The HRESULT.</returns>
    [LibraryImport("ole32", EntryPoint = "CoCreateInstance")]
    internal static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* instance);

    /// <summary>
    /// The single point that depends on the pending ole32 CoTaskMemAlloc allowance (not yet approved): COM callers free
    /// the GetId, GetMixFormat and ISpStreamFormat::GetFormat results with CoTaskMemFree, so they must come from the COM
    /// task allocator. Every caller goes through <see cref="TaskString"/> or <see cref="TaskFormat"/>.
    /// </summary>
    /// <param name="bytes">The size.</param>
    /// <returns>The memory.</returns>
    private static nint AllocateTaskMemory(nuint bytes)
    {
        nint memory = CoTaskMemAlloc(bytes);
        return memory != 0 ? memory : throw new InvalidOperationException("CoTaskMemAlloc returned no memory for a COM out parameter.");
    }

    [LibraryImport("ole32", EntryPoint = "CoTaskMemAlloc")]
    private static partial nint CoTaskMemAlloc(nuint bytes);
}
