// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Linux.Browser;

/// <summary>
/// The JUMP_SLOT relocations of one loaded ELF image (found with dl_iterate_phdr, read from its dynamic section's
/// DT_JMPREL, DT_SYMTAB and DT_STRTAB), and the patching of a slot even inside the image's read-only GNU_RELRO segment.
/// Only the named image's own imports change; every other image keeps its bindings.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: reads loaded ELF structures and rewrites one image's import slots.")]
internal sealed unsafe partial class ElfJumpSlots
{
    private const string LibC = "libc.so.6";
    private const uint DynamicSegment = 2;
    private const uint RelroSegment = 0x6474e552;
    private const long JumpRelocations = 23;
    private const long JumpRelocationsSize = 2;
    private const long SymbolTable = 6;
    private const long StringTable = 5;
    private const int ReadOnly = 1;
    private const int ReadWrite = 3;
    private static string? wantedSuffix;
    private static ElfJumpSlots? found;
    private readonly Dictionary<string, nint> _slots;

    private ElfJumpSlots(string path, nint baseAddress, Dictionary<string, nint> slots, nint relroStart, nint relroEnd)
    {
        Path = path;
        BaseAddress = baseAddress;
        _slots = slots;
        RelroStart = relroStart;
        RelroEnd = relroEnd;
    }

    /// <summary>Gets the image's path.</summary>
    internal string Path { get; }

    /// <summary>Gets the image's load bias.</summary>
    internal nint BaseAddress { get; }

    /// <summary>Gets the start of the image's GNU_RELRO segment, or zero.</summary>
    internal nint RelroStart { get; }

    /// <summary>Gets the end of the image's GNU_RELRO segment, or zero.</summary>
    internal nint RelroEnd { get; }

    /// <summary>Gets the number of JUMP_SLOT relocations.</summary>
    internal int Count => _slots.Count;

    /// <summary>Gets the imported names that have a JUMP_SLOT.</summary>
    internal IEnumerable<string> Names => _slots.Keys;

    /// <summary>Finds a loaded image by path suffix and reads its JUMP_SLOTs.</summary>
    /// <param name="suffix">The path suffix, such as "/libcef.so".</param>
    /// <returns>The image's slots.</returns>
    internal static ElfJumpSlots Find(string suffix)
    {
        wantedSuffix = suffix;
        found = null;
        _ = DlIteratePhdr(&Visit, 0);
        return Volatile.Read(ref found) ?? throw new InvalidOperationException($"No loaded image ends with {suffix}.");
    }

    /// <summary>Gets one import's slot address.</summary>
    /// <param name="name">The imported symbol.</param>
    /// <returns>The slot, or zero when the image has no JUMP_SLOT for it.</returns>
    internal nint Slot(string name) => _slots.GetValueOrDefault(name);

    /// <summary>Replaces one import's bound target and returns the original target.</summary>
    /// <param name="name">The imported symbol.</param>
    /// <param name="replacement">The new target.</param>
    /// <returns>The target the slot held, which the replacement forwards to.</returns>
    internal nint Patch(string name, nint replacement)
    {
        nint slot = Slot(name);
        if (slot == 0)
        {
            throw new InvalidOperationException($"{Path} has no JUMP_SLOT for {name}.");
        }

        nint page = GetPageSize();
        nint start = slot & ~(page - 1);
        if (MemoryProtect(start, (nuint)page, ReadWrite) != 0)
        {
            throw new InvalidOperationException($"mprotect could not make the {name} slot writable.");
        }

        nint original = *(nint*)slot;
        *(nint*)slot = replacement;
        bool relro = slot >= RelroStart && slot < RelroEnd;
        if (relro)
        {
            _ = MemoryProtect(start, (nuint)page, ReadOnly);
        }

        AudioProofLog.Note($"patched {name} slot 0x{slot:x} (image offset 0x{slot - BaseAddress:x}, in GNU_RELRO={relro}, bound before patch={original != 0}) from 0x{original:x}");
        return original;
    }

    [UnmanagedCallersOnly]
    private static int Visit(nint info, nuint size, nint data)
    {
        string? name = Marshal.PtrToStringUTF8(*(nint*)(info + 8));
        if (found is not null || name is null || wantedSuffix is null || !name.EndsWith(wantedSuffix, StringComparison.Ordinal))
        {
            return 0;
        }

        nint bias = *(nint*)info;
        nint headers = *(nint*)(info + 16);
        int count = *(ushort*)(info + 24);
        nint dynamic = 0;
        nint relroStart = 0;
        nint relroEnd = 0;
        for (int index = 0; index < count; index++)
        {
            nint header = headers + (56 * index);
            uint type = *(uint*)header;
            nint address = bias + (nint)(*(ulong*)(header + 16));
            if (type == DynamicSegment)
            {
                dynamic = address;
            }
            else if (type == RelroSegment)
            {
                relroStart = address;
                relroEnd = address + (nint)(*(ulong*)(header + 40));
            }
        }

        found = new ElfJumpSlots(name, bias, ReadSlots(bias, dynamic), relroStart, relroEnd);
        return 1;
    }

    private static Dictionary<string, nint> ReadSlots(nint bias, nint dynamic)
    {
        nint relocations = 0;
        long relocationBytes = 0;
        nint symbols = 0;
        nint strings = 0;
        for (nint entry = dynamic; *(long*)entry != 0; entry += 16)
        {
            long tag = *(long*)entry;
            nint value = *(nint*)(entry + 8);

            // glibc relocates d_ptr values in place on x86_64 and aarch64; an unrelocated value is image-relative.
            nint pointer = value < bias ? bias + value : value;
            switch (tag)
            {
                case JumpRelocations:
                    relocations = pointer;
                    break;
                case JumpRelocationsSize:
                    relocationBytes = value;
                    break;
                case SymbolTable:
                    symbols = pointer;
                    break;
                case StringTable:
                    strings = pointer;
                    break;
            }
        }

        var slots = new Dictionary<string, nint>(StringComparer.Ordinal);
        for (long offset = 0; offset < relocationBytes; offset += 24)
        {
            nint relocation = relocations + (nint)offset;
            ulong info = *(ulong*)(relocation + 8);
            uint type = (uint)info;
            if (type is not (7 or 1026))
            {
                continue;
            }

            uint nameOffset = *(uint*)(symbols + (nint)((info >> 32) * 24));
            string? symbol = Marshal.PtrToStringUTF8(strings + (nint)nameOffset);
            if (symbol is not null)
            {
                slots[symbol] = bias + (nint)(*(ulong*)relocation);
            }
        }

        return slots;
    }

    [LibraryImport(LibC, EntryPoint = "dl_iterate_phdr")]
    private static partial int DlIteratePhdr(delegate* unmanaged<nint, nuint, nint, int> callback, nint data);

    [LibraryImport(LibC, EntryPoint = "mprotect")]
    private static partial int MemoryProtect(nint address, nuint length, int protection);

    [LibraryImport(LibC, EntryPoint = "getpagesize")]
    private static partial int GetPageSizeNative();

    /// <summary>
    /// The single point that depends on the pending libc getpagesize allowance (not yet approved): every JUMP_SLOT patch
    /// (<see cref="Patch"/>: dlsym and every snd_pcm_* slot) aligns its mprotect call with this page size.
    /// </summary>
    /// <returns>The page size.</returns>
    private static nint GetPageSize() => GetPageSizeNative();
}
