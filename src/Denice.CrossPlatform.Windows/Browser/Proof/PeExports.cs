// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>Reads a loaded module's export table to name a function address (MMDevAPI ordinal 17).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: reads the loaded PE export directory.")]
internal static unsafe class PeExports
{
    /// <summary>Names the exports at a function's address.</summary>
    /// <param name="module">The module base.</param>
    /// <param name="function">The function address.</param>
    /// <returns>Every export name at that address, or "(unnamed, rva …)".</returns>
    internal static string NameAt(nint module, nint function)
    {
        byte* image = (byte*)module;
        byte* headers = image + *(int*)(image + 0x3C);
        uint directory = *(uint*)(headers + 24 + 112);
        byte* exports = image + directory;
        uint count = *(uint*)(exports + 24);
        uint* functions = (uint*)(image + *(uint*)(exports + 28));
        uint* names = (uint*)(image + *(uint*)(exports + 32));
        ushort* ordinals = (ushort*)(image + *(uint*)(exports + 36));
        var found = new List<string>();
        for (uint index = 0; index < count; index++)
        {
            if (image + functions[ordinals[index]] == (byte*)function)
            {
                found.Add(Marshal.PtrToStringUTF8((nint)(image + names[index])) ?? string.Empty);
            }
        }

        return found.Count > 0 ? string.Join(", ", found) : $"(unnamed, rva 0x{function - module:x})";
    }
}
