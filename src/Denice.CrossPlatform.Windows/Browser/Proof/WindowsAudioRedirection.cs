// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Windows.Browser;

/// <summary>
/// Installs the Windows browser-audio redirection proof after CefRuntime.Load and before CefRuntime.Initialize by
/// patching libcef.dll's own import slots (windows-linux-audio-proof.md, Windows interception boundary):
/// the delay-load CoCreateInstance (MMDeviceEnumerator → <see cref="RedirectingEnumerator"/>; SpVoice → the real voice
/// after ISpVoice::SetOutput to a <see cref="SapiOutputStream"/>), logging pass-throughs for CoGetClassObject,
/// RoActivateInstance and RoGetActivationFactory, the identification of MMDevAPI ordinal 17 (logged, wrapped only if it
/// is ActivateAudioInterfaceAsync), and the WINMM wave-out imports (<see cref="WaveOutRedirection"/>). The real functions
/// are DENICE's own lookups, never the slots' previous values, which may be unresolved delay-load thunks.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: replacements exchange unmanaged COM pointers.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross into Chromium; the first one is recorded in the mixer.")]
internal static unsafe partial class WindowsAudioRedirection
{
    private static readonly Guid DeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid SpVoice = new("96749377-3391-11D2-9EE3-00C04F797396");
    private static readonly Guid SpVoiceInterface = new("6C44DF74-72B9-4992-A1EC-EF996E0422D4");
    private static ProofContext? proof;
    private static nint realCoGetClassObject;
    private static nint realRoActivateInstance;
    private static nint realRoGetActivationFactory;
    private static nint realActivateAudioInterfaceAsync;
    private static nint stringBuffer;

    /// <summary>Patches libcef.dll's audio import slots, once per process.</summary>
    /// <param name="target">The started mixer core.</param>
    /// <param name="time">The injected clock.</param>
    internal static void Install(MixerCore target, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (proof is not null)
        {
            throw new InvalidOperationException("Browser audio redirection is already installed in this process.");
        }

        proof = new ProofContext(target, time);
        WasapiRedirectedClient.Mixer = target;
        WaveOutRedirection.Use(target, time);
        PeImports cef = PeImports.Find("libcef.dll");
        AudioProofLog.Note($"libcef.dll at 0x{cef.Module:x}, size 0x{cef.Size:x}, {cef.Keys.Count()} import slots");
        nint ole32 = NativeLibrary.Load("ole32.dll");
        nint combase = NativeLibrary.Load("combase.dll");
        realCoGetClassObject = NativeLibrary.GetExport(ole32, "CoGetClassObject");
        realRoActivateInstance = NativeLibrary.GetExport(combase, "RoActivateInstance");
        realRoGetActivationFactory = NativeLibrary.GetExport(combase, "RoGetActivationFactory");
        stringBuffer = NativeLibrary.GetExport(combase, "WindowsGetStringRawBuffer");
        _ = cef.Patch("delay:ole32.dll!CoCreateInstance", (nint)(delegate* unmanaged<Guid*, nint, uint, Guid*, nint*, int>)&CoCreateInstance);
        _ = cef.Patch("delay:ole32.dll!CoGetClassObject", (nint)(delegate* unmanaged<Guid*, uint, nint, Guid*, nint*, int>)&CoGetClassObject);
        PatchIfPresent(cef, "delay:api-ms-win-core-winrt-l1-1-0.dll!RoActivateInstance", (nint)(delegate* unmanaged<nint, nint*, int>)&RoActivateInstance);
        PatchIfPresent(cef, "delay:api-ms-win-core-winrt-l1-1-0.dll!RoGetActivationFactory", (nint)(delegate* unmanaged<nint, Guid*, nint*, int>)&RoGetActivationFactory);
        IdentifyOrdinal17(cef);
        foreach (KeyValuePair<string, nint> wave in WaveOutRedirection.Replacements)
        {
            PatchIfPresent(cef, wave.Key, wave.Value);
        }
    }

    private static void PatchIfPresent(PeImports cef, string key, nint replacement)
    {
        if (cef.Slot(key) == 0)
        {
            AudioProofLog.Note("absent import " + key);
            return;
        }

        _ = cef.Patch(key, replacement);
    }

    private static void IdentifyOrdinal17(PeImports cef)
    {
        nint mmdevapi = NativeLibrary.Load("mmdevapi.dll");
        nint function = GetProcAddress(mmdevapi, 17);
        string identity = PeExports.NameAt(mmdevapi, function);
        bool activate = NativeLibrary.TryGetExport(mmdevapi, "ActivateAudioInterfaceAsync", out nint named) && named == function;
        AudioProofLog.Note($"MMDevAPI ordinal 17 = 0x{function:x}: export name '{identity}', equals ActivateAudioInterfaceAsync={activate}");
        if (activate && cef.Slot("delay:mmdevapi.dll!#17") != 0)
        {
            realActivateAudioInterfaceAsync = function;
            _ = cef.Patch("delay:mmdevapi.dll!#17", (nint)(delegate* unmanaged<nint, Guid*, nint, nint, nint*, int>)&ActivateAudioInterfaceAsync);
        }
    }

    private static string ClassName(nint name)
    {
        uint length = 0;
        char* text = ((delegate* unmanaged<nint, uint*, char*>)stringBuffer)(name, &length);
        return text == null ? string.Empty : new string(text, 0, (int)length);
    }

    [UnmanagedCallersOnly]
    private static int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* instance)
    {
        Guid created = *clsid;
        AudioProofLog.Count("libcef CoCreateInstance " + created);
        try
        {
            if (created == DeviceEnumerator && proof is not null)
            {
                return CreateEnumerator(clsid, context, iid, instance);
            }

            int status = ComNative.CoCreateInstance(clsid, outer, context, iid, instance);
            if (created == SpVoice && status == ComNative.Ok && proof is not null)
            {
                RedirectVoice(*instance);
            }

            return status;
        }
        catch (Exception error)
        {
            proof?.Mixer.Fail("Browser audio redirection CoCreateInstance failed: " + error.Message);
            AudioProofLog.Note("FAILED CoCreateInstance: " + error);
            return ComNative.Failed;
        }
    }

    private static int CreateEnumerator(Guid* clsid, uint context, Guid* iid, nint* instance)
    {
        Guid enumerator = typeof(IMMDeviceEnumerator).GUID;
        nint real = 0;
        int status = ComNative.CoCreateInstance(clsid, 0, context, &enumerator, &real);
        if (status != ComNative.Ok)
        {
            return status;
        }

        var wrapper = new RedirectingEnumerator(ComNative.Wrap<IMMDeviceEnumerator>(real), proof!.Clock);
        _ = ComNative.Release(real);
        *instance = ComNative.Expose(wrapper, *iid);
        AudioProofLog.Count("MMDeviceEnumerator wrapped");
        AudioProofLog.Note($"CoCreateInstance(MMDeviceEnumerator, {*iid}) -> DENICE enumerator");
        return *instance == 0 ? ComNative.NoInterface : ComNative.Ok;
    }

    private static void RedirectVoice(nint created)
    {
        nint voice = ComNative.QueryInterface(created, SpVoiceInterface);
        if (voice == 0)
        {
            proof!.Mixer.Fail("Chromium's SAPI voice has no ISpVoice interface.");
            return;
        }

        nint stream = ComNative.Expose(new SapiOutputStream(proof!.Mixer, proof.Clock), typeof(ISpStreamFormat).GUID);

        // ISpVoice::SetOutput: IUnknown (3) + ISpNotifySource (7) + ISpEventSource (3), then SetOutput at slot 13.
        int status = ComNative.Call(voice, 13, stream, 0);
        _ = ComNative.Release(stream);
        _ = ComNative.Release(voice);
        AudioProofLog.Count("SpVoice SetOutput");
        AudioProofLog.Note($"CoCreateInstance(SpVoice): ISpVoice::SetOutput(DENICE 48 kHz 16-bit mono stream, FALSE) -> 0x{status:x8}");
        if (status != ComNative.Ok)
        {
            proof!.Mixer.Fail($"ISpVoice::SetOutput failed with 0x{status:x8}; page speech would play outside the mix.");
        }
    }

    [UnmanagedCallersOnly]
    private static int CoGetClassObject(Guid* clsid, uint context, nint server, Guid* iid, nint* instance)
    {
        AudioProofLog.Count("libcef CoGetClassObject " + *clsid);
        if (*clsid == DeviceEnumerator || *clsid == SpVoice)
        {
            proof?.Mixer.Fail($"Chromium asked CoGetClassObject for {*clsid}; that path is not redirected by the proof.");
        }

        return ((delegate* unmanaged<Guid*, uint, nint, Guid*, nint*, int>)realCoGetClassObject)(clsid, context, server, iid, instance);
    }

    [UnmanagedCallersOnly]
    private static int RoActivateInstance(nint classId, nint* instance)
    {
        AudioProofLog.Count("libcef RoActivateInstance " + ClassName(classId));
        return ((delegate* unmanaged<nint, nint*, int>)realRoActivateInstance)(classId, instance);
    }

    [UnmanagedCallersOnly]
    private static int RoGetActivationFactory(nint classId, Guid* iid, nint* factory)
    {
        AudioProofLog.Count("libcef RoGetActivationFactory " + ClassName(classId));
        return ((delegate* unmanaged<nint, Guid*, nint*, int>)realRoGetActivationFactory)(classId, iid, factory);
    }

    [UnmanagedCallersOnly]
    private static int ActivateAudioInterfaceAsync(nint path, Guid* iid, nint parameters, nint handler, nint* operation)
    {
        string device = path == 0 ? string.Empty : new string((char*)path);
        AudioProofLog.Count("libcef ActivateAudioInterfaceAsync " + *iid);
        AudioProofLog.Note($"ActivateAudioInterfaceAsync {device} {*iid} passed through (not redirected by the proof)");
        return ((delegate* unmanaged<nint, Guid*, nint, nint, nint*, int>)realActivateAudioInterfaceAsync)(path, iid, parameters, handler, operation);
    }

    [LibraryImport("kernel32", EntryPoint = "GetProcAddress")]
    private static partial nint GetProcAddress(nint module, nint ordinal);
}
