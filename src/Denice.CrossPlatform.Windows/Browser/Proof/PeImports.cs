// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>
/// The ordinary and delay-load import slots of one loaded PE image (libcef.dll), read from its import and delay-import
/// directories, and the patching of one slot. A delay-load slot still holding an address inside the image is the
/// unresolved delay-load thunk; replacing it means the delay-load helper never runs for that import.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: reads the loaded PE image and rewrites its import slots.")]
internal sealed unsafe partial class PeImports
{
    private const uint ReadWrite = 0x04;
    private readonly Dictionary<string, nint> _slots;

    private PeImports(nint module, uint size, Dictionary<string, nint> slots)
    {
        Module = module;
        Size = size;
        _slots = slots;
    }

    /// <summary>Gets the module base.</summary>
    internal nint Module { get; }

    /// <summary>Gets the image size.</summary>
    internal uint Size { get; }

    /// <summary>Gets every import key ("dll!name" or "dll!#ordinal", any case, "delay:" prefix for delay imports).</summary>
    internal IEnumerable<string> Keys => _slots.Keys;

    /// <summary>Reads a loaded module's import slots.</summary>
    /// <param name="moduleName">The module, such as "libcef.dll".</param>
    /// <returns>The slots.</returns>
    internal static PeImports Find(string moduleName)
    {
        nint module = GetModuleHandle(moduleName);
        if (module == 0)
        {
            throw new InvalidOperationException(moduleName + " is not loaded.");
        }

        byte* image = (byte*)module;
        byte* headers = image + *(int*)(image + 0x3C);
        uint size = *(uint*)(headers + 24 + 56);

        // PE32+ optional header: data directories start at 112; import is entry 1, delay import is entry 13.
        byte* directories = headers + 24 + 112;
        var slots = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);
        ReadImports(image, *(uint*)(directories + 8), slots);
        ReadDelayImports(image, *(uint*)(directories + (13 * 8)), slots);
        return new PeImports(module, size, slots);
    }

    /// <summary>Gets a slot.</summary>
    /// <param name="key">The import key.</param>
    /// <returns>The slot address, or zero.</returns>
    internal nint Slot(string key) => _slots.GetValueOrDefault(key);

    /// <summary>Gets a value indicating whether a slot still points into the image (an unresolved delay-load thunk).</summary>
    /// <param name="key">The import key.</param>
    /// <returns>Whether it is unresolved.</returns>
    internal bool IsUnresolved(string key)
    {
        nint value = *(nint*)Slot(key);
        return value >= Module && value < Module + (nint)Size;
    }

    /// <summary>Replaces a slot's target and returns what it held.</summary>
    /// <param name="key">The import key.</param>
    /// <param name="replacement">The new target.</param>
    /// <returns>The previous target.</returns>
    internal nint Patch(string key, nint replacement)
    {
        nint slot = Slot(key);
        if (slot == 0)
        {
            throw new InvalidOperationException("libcef.dll has no import slot " + key + ".");
        }

        uint previous;
        if (VirtualProtect(slot, (nuint)sizeof(nint), ReadWrite, &previous) == 0)
        {
            throw new InvalidOperationException("VirtualProtect failed for " + key + ".");
        }

        nint original = *(nint*)slot;
        bool unresolved = original >= Module && original < Module + (nint)Size;
        *(nint*)slot = replacement;
        _ = VirtualProtect(slot, (nuint)sizeof(nint), previous, &previous);
        AudioProofLog.Note($"patched {key} slot 0x{slot:x} (was 0x{original:x}, unresolved delay-load thunk={unresolved})");
        return original;
    }

    private static void ReadImports(byte* image, uint directory, Dictionary<string, nint> slots)
    {
        for (byte* descriptor = image + directory; directory != 0 && *(uint*)(descriptor + 12) != 0; descriptor += 20)
        {
            string dll = Marshal.PtrToStringUTF8((nint)(image + *(uint*)(descriptor + 12))) ?? string.Empty;
            uint names = *(uint*)descriptor;
            ReadThunks(image, dll, names != 0 ? names : *(uint*)(descriptor + 16), *(uint*)(descriptor + 16), string.Empty, slots);
        }
    }

    private static void ReadDelayImports(byte* image, uint directory, Dictionary<string, nint> slots)
    {
        for (byte* descriptor = image + directory; directory != 0 && *(uint*)(descriptor + 4) != 0; descriptor += 32)
        {
            string dll = Marshal.PtrToStringUTF8((nint)(image + *(uint*)(descriptor + 4))) ?? string.Empty;
            ReadThunks(image, dll, *(uint*)(descriptor + 16), *(uint*)(descriptor + 12), "delay:", slots);
        }
    }

    private static void ReadThunks(byte* image, string dll, uint names, uint addresses, string prefix, Dictionary<string, nint> slots)
    {
        int index = 0;
        for (ulong thunk = *(ulong*)(image + names); thunk != 0; thunk = *(ulong*)(image + names + (++index * 8)))
        {
            string name = (thunk & (1UL << 63)) != 0
                ? "#" + (thunk & 0xFFFF).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : Marshal.PtrToStringUTF8((nint)(image + (uint)thunk + 2)) ?? string.Empty;
            slots[prefix + dll + "!" + name] = (nint)(image + addresses + (index * 8));
        }
    }

    [LibraryImport("kernel32", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string name);

    [LibraryImport("kernel32", EntryPoint = "VirtualProtect")]
    private static partial int VirtualProtect(nint address, nuint size, uint protection, uint* previous);
}
