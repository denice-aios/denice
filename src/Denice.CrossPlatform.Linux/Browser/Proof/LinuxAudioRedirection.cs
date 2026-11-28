// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Linux.Browser;

/// <summary>
/// Installs the Linux browser-audio redirection proof after CefRuntime.Load and before CefRuntime.Initialize: libcef.so's
/// dlsym JUMP_SLOT (inside its full-RELRO GNU_RELRO segment) is replaced, so libcef's own lookups of the redirected
/// PulseAudio names return <see cref="PulseRedirection"/> implementations and every other lookup goes to the real
/// dlsym; its snd_pcm_* JUMP_SLOTs redirect playback handles into the mixer (<see cref="AlsaRedirection"/>). Only libcef.so's
/// own imports change.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: the dlsym replacement exchanges unmanaged symbol names and function pointers.")]
internal static unsafe class LinuxAudioRedirection
{
    private const nint RtldNext = -1;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, nint> RealFunctions = new(StringComparer.Ordinal);
    private static nint realDlsym;
    private static nint libc;

    /// <summary>Replaces libcef.so's dlsym and snd_pcm_* slots, once per process.</summary>
    /// <param name="mixer">The started mixer core that pulls every redirected stream.</param>
    internal static void Install(MixerCore mixer)
    {
        ArgumentNullException.ThrowIfNull(mixer);
        if (realDlsym != 0)
        {
            throw new InvalidOperationException("Browser audio redirection is already installed in this process.");
        }

        ElfJumpSlots cef = ElfJumpSlots.Find("/libcef.so");
        AudioProofLog.Note($"{cef.Path}: {cef.Count} JUMP_SLOTs, load bias 0x{cef.BaseAddress:x}, GNU_RELRO 0x{cef.RelroStart:x}-0x{cef.RelroEnd:x}, snd_* slots {cef.Names.Count(name => name.StartsWith("snd_", StringComparison.Ordinal))}");
        PulseRedirection.UseMixer(mixer);
        libc = NativeLibrary.Load("libc.so.6");
        realDlsym = cef.Patch("dlsym", (nint)(delegate* unmanaged<nint, byte*, nint>)&Dlsym);
        AlsaRedirection.Install(cef, mixer);
    }

    /// <summary>Gets the real function libcef.so looked up for a redirected name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The real function.</returns>
    internal static nint Real(string name)
    {
        lock (Gate)
        {
            return RealFunctions.TryGetValue(name, out nint function) ? function : throw new InvalidOperationException($"libcef.so never looked up {name}.");
        }
    }

    [UnmanagedCallersOnly]
    private static nint Dlsym(nint handle, byte* symbol)
    {
        string? name = Marshal.PtrToStringUTF8((nint)symbol);
        AudioProofLog.Count("dlsym lookups by libcef");
        nint real;
        if (handle == RtldNext)
        {
            // The real dlsym resolves RTLD_NEXT relative to its caller, which is now this replacement rather than libcef.so,
            // so it would find nothing. libcef's RTLD_NEXT lookups name libc functions ("close"); they are looked up in libc,
            // then in the global scope.
            real = ((delegate* unmanaged<nint, byte*, nint>)realDlsym)(libc, symbol);
            real = real != 0 ? real : ((delegate* unmanaged<nint, byte*, nint>)realDlsym)(0, symbol);
            AudioProofLog.Count("dlsym RTLD_NEXT lookups by libcef: " + name);
        }
        else
        {
            real = ((delegate* unmanaged<nint, byte*, nint>)realDlsym)(handle, symbol);
        }

        if (name is null)
        {
            return real;
        }

        if (name.StartsWith("pa_", StringComparison.Ordinal) || name.StartsWith("spd_", StringComparison.Ordinal) || name.StartsWith("snd_", StringComparison.Ordinal))
        {
            AudioProofLog.Count("dlsym " + name[..name.IndexOf('_', StringComparison.Ordinal)] + "_* lookups");
        }

        if (real == 0 || !PulseRedirection.Replacements.TryGetValue(name, out nint replacement))
        {
            return real;
        }

        lock (Gate)
        {
            RealFunctions[name] = real;
        }

        AudioProofLog.Count("dlsym pa_* lookups redirected");
        AudioProofLog.Note($"dlsym {name} redirected (real 0x{real:x})");
        return replacement;
    }
}
