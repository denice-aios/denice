// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.MacOS.Recording;

namespace Denice.CrossPlatform.MacOS.Browser;

/// <summary>
/// Routes every Chromium audio output stream into the browser audio mixer (from the approved interception design,
/// document-0b78de3410fd424d849566aebdb47c67). Before CefRuntime.Initialize it replaces the Chromium Embedded Framework
/// image's GOT entries for its AudioToolbox output calls, so Chromium's HAL output units are configured but never started
/// and their render callbacks go to the mixer, for default and explicitly selected devices alike. The replacements stay
/// installed until the process exits. DENICE's own AudioToolbox imports below are not the CEF image's entries, so they
/// reach the real functions.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio interop: UnmanagedCallersOnly replacements and render callbacks exchange stable unmanaged function pointers and buffers with AudioToolbox and Chromium.")]
internal static unsafe partial class AudioOutputRedirection
{
    /// <summary>kAudioUnitProperty_SetRenderCallback.</summary>
    internal const uint SetRenderCallbackProperty = 23;

    /// <summary>kAudioUnitProperty_StreamFormat.</summary>
    internal const uint StreamFormatProperty = 8;

    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const string CoreMedia = "/System/Library/Frameworks/CoreMedia.framework/CoreMedia";
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";
    private const uint CurrentDeviceProperty = 2000;
    private const uint ChannelLayoutProperty = 19;
    private const uint UseChannelDescriptions = 0;
    private const uint HalOutputSubType = 0x6168616C;
    private const uint SegmentCommand64 = 0x19;
    private const uint SymbolTableCommand = 0x2;
    private const uint DynamicSymbolTableCommand = 0xB;
    private const string CefImageSuffix = "/Chromium Embedded Framework";
    private const uint FloatPackedNonInterleaved = 0x29;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<nint, RedirectedStream> Streams = [];
    private static BrowserAudioMixer? mixer;
    private static int replacedSlots;
    private static int unitsTracked;
    private static int startsRedirected;
    private static int startsForwarded;
    private static int disposals;

    /// <summary>Gets the CEF HAL output units still tracked.</summary>
    internal static int TrackedUnits
    {
        get
        {
            lock (Gate)
            {
                return Streams.Count;
            }
        }
    }

    /// <summary>Gets the redirection counters as text.</summary>
    internal static string Report
    {
        get
        {
            lock (Gate)
            {
                return $"slots replaced={replacedSlots}, CEF HAL units tracked in total={unitsTracked}, still tracked={Streams.Count}, starts redirected={startsRedirected}, real starts of CEF units={startsForwarded}, disposals={disposals}";
            }
        }
    }

    /// <summary>
    /// Replaces the CEF image's GOT entries for the redirected AudioToolbox calls, once per process, while no CEF audio
    /// unit exists.
    /// </summary>
    /// <param name="target">The started mixer that will pull every redirected stream.</param>
    internal static void Install(BrowserAudioMixer target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (mixer is not null)
        {
            throw new InvalidOperationException("Browser audio redirection is already installed in this process.");
        }

        mixer = target;
        var replacements = new Dictionary<string, nint>(StringComparer.Ordinal)
        {
            ["_AudioComponentInstanceNew"] = (nint)(delegate* unmanaged<nint, nint*, int>)&ReplaceAudioComponentInstanceNew,
            ["_AudioUnitSetProperty"] = (nint)(delegate* unmanaged<nint, uint, uint, uint, void*, uint, int>)&ReplaceAudioUnitSetProperty,
            ["_AudioOutputUnitStart"] = (nint)(delegate* unmanaged<nint, int>)&ReplaceAudioOutputUnitStart,
            ["_AudioOutputUnitStop"] = (nint)(delegate* unmanaged<nint, int>)&ReplaceAudioOutputUnitStop,
            ["_AudioComponentInstanceDispose"] = (nint)(delegate* unmanaged<nint, int>)&ReplaceAudioComponentInstanceDispose,
        };
        uint images = DyldImageCount();
        for (uint index = 0; index < images; index++)
        {
            string? name = Marshal.PtrToStringUTF8(DyldGetImageName(index));
            if (name is not null && name.EndsWith(CefImageSuffix, StringComparison.Ordinal))
            {
                RebindImage(DyldGetImageHeader(index), DyldGetImageSlide(index), replacements);
                if (replacedSlots != replacements.Count)
                {
                    throw new InvalidOperationException($"The CEF image had {replacedSlots} of {replacements.Count} redirected GOT entries.");
                }

                return;
            }
        }

        throw new InvalidOperationException("The Chromium Embedded Framework image is not loaded.");
    }

    /// <summary>Opens, configures and starts a default-output unit for the mixer through DENICE's own imports.</summary>
    /// <param name="procedure">The mixer's render callback.</param>
    /// <param name="context">The callback's context.</param>
    /// <param name="sampleRate">The float stereo non-interleaved sample rate.</param>
    /// <returns>The started unit.</returns>
    internal static nint StartOutputUnit(nint procedure, nint context, double sampleRate)
    {
        var description = new ComponentDescription(0x61756F75, 0x64656620, 0x6170706C, 0, 0);
        nint component = AudioComponentFindNext(0, ref description);
        if (component == 0)
        {
            throw new InvalidOperationException("No default output audio unit was found.");
        }

        nint unit;
        Check(AudioComponentInstanceNew(component, &unit), "AudioComponentInstanceNew");
        var format = new StreamDescription(sampleRate, 0x6C70636D, 0x29, 4, 1, 4, 2, 32, 0);
        Check(AudioUnitSetProperty(unit, StreamFormatProperty, 1, 0, &format, (uint)sizeof(StreamDescription)), "set mixer format");
        var callback = new RenderCallbackStruct(procedure, context);
        Check(AudioUnitSetProperty(unit, SetRenderCallbackProperty, 1, 0, &callback, (uint)sizeof(RenderCallbackStruct)), "set mixer callback");
        Check(AudioUnitInitialize(unit), "AudioUnitInitialize");
        Check(AudioOutputUnitStart(unit), "AudioOutputUnitStart");
        return unit;
    }

    /// <summary>Stops and disposes the mixer's own output unit.</summary>
    /// <param name="unit">The unit.</param>
    internal static void StopOutputUnit(nint unit)
    {
        _ = AudioOutputUnitStop(unit);
        _ = AudioUnitUninitialize(unit);
        _ = AudioComponentInstanceDispose(unit);
    }

    /// <summary>Converts a Core Audio host time to nanoseconds on the host clock.</summary>
    /// <param name="hostTime">The host time in system units.</param>
    /// <returns>The nanoseconds.</returns>
    internal static long HostTimeToNanoseconds(ulong hostTime)
    {
        MediaTime time = CMClockMakeHostTimeFromSystemUnits(hostTime);
        return checked((long)((Int128)time.Value * 1_000_000_000 / time.Timescale));
    }

    /// <summary>Finds an audio component matching a description.</summary>
    /// <param name="component">The component to search after, or zero.</param>
    /// <param name="description">The description to match.</param>
    /// <returns>The component, or zero.</returns>
    [LibraryImport(AudioToolbox)]
    internal static partial nint AudioComponentFindNext(nint component, ref ComponentDescription description);

    /// <summary>Creates an audio unit instance; DENICE's own import reaches the real function.</summary>
    /// <param name="component">The component.</param>
    /// <param name="instance">Receives the instance.</param>
    /// <returns>The OSStatus.</returns>
    [LibraryImport(AudioToolbox)]
    internal static partial int AudioComponentInstanceNew(nint component, nint* instance);

    /// <summary>Disposes an audio unit instance.</summary>
    /// <param name="instance">The instance.</param>
    /// <returns>The OSStatus.</returns>
    [LibraryImport(AudioToolbox)]
    internal static partial int AudioComponentInstanceDispose(nint instance);

    /// <summary>Sets an audio unit property.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="property">The property identifier.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="element">The element.</param>
    /// <param name="data">The value.</param>
    /// <param name="size">The value's size.</param>
    /// <returns>The OSStatus.</returns>
    [LibraryImport(AudioToolbox)]
    internal static partial int AudioUnitSetProperty(nint unit, uint property, uint scope, uint element, void* data, uint size);

    /// <summary>Initializes an audio unit.</summary>
    /// <param name="unit">The unit.</param>
    /// <returns>The OSStatus.</returns>
    [LibraryImport(AudioToolbox)]
    internal static partial int AudioUnitInitialize(nint unit);

    /// <summary>Uninitializes an audio unit.</summary>
    /// <param name="unit">The unit.</param>
    /// <returns>The OSStatus.</returns>
    [LibraryImport(AudioToolbox)]
    internal static partial int AudioUnitUninitialize(nint unit);

    /// <summary>Starts an output unit.</summary>
    /// <param name="unit">The unit.</param>
    /// <returns>The OSStatus.</returns>
    [LibraryImport(AudioToolbox)]
    internal static partial int AudioOutputUnitStart(nint unit);

    /// <summary>Stops an output unit.</summary>
    /// <param name="unit">The unit.</param>
    /// <returns>The OSStatus.</returns>
    [LibraryImport(AudioToolbox)]
    internal static partial int AudioOutputUnitStop(nint unit);

    [LibraryImport(AudioToolbox)]
    private static partial int AudioComponentGetDescription(nint component, out ComponentDescription description);

    [LibraryImport(CoreMedia)]
    private static partial MediaTime CMClockMakeHostTimeFromSystemUnits(ulong hostTime);

    [LibraryImport(LibSystem, EntryPoint = "_dyld_image_count")]
    private static partial uint DyldImageCount();

    [LibraryImport(LibSystem, EntryPoint = "_dyld_get_image_name")]
    private static partial nint DyldGetImageName(uint index);

    [LibraryImport(LibSystem, EntryPoint = "_dyld_get_image_header")]
    private static partial nint DyldGetImageHeader(uint index);

    [LibraryImport(LibSystem, EntryPoint = "_dyld_get_image_vmaddr_slide")]
    private static partial nint DyldGetImageSlide(uint index);

    [LibraryImport(LibSystem, EntryPoint = "vm_protect")]
    private static partial int VmProtect(uint task, nint address, nint size, int setMaximum, int protection);

    private static void RebindImage(nint header, nint slide, Dictionary<string, nint> replacements)
    {
        uint commands = (uint)Marshal.ReadInt32(header + 16);
        nint command = header + 32;
        nint linkEditBase = 0;
        (uint SymbolOffset, uint StringOffset) symbols = default;
        uint indirectOffset = 0;
        var gots = new List<(nint Address, long Size, uint FirstIndirect)>();
        for (uint index = 0; index < commands; index++)
        {
            uint kind = (uint)Marshal.ReadInt32(command);
            uint size = (uint)Marshal.ReadInt32(command + 4);
            if (kind == SegmentCommand64)
            {
                string segment = Marshal.PtrToStringUTF8(command + 8, 16).TrimEnd('\0');
                long address = ReadInt64(command + 24);
                long fileOffset = ReadInt64(command + 40);
                uint sections = (uint)Marshal.ReadInt32(command + 64);
                if (string.Equals(segment, "__LINKEDIT", StringComparison.Ordinal))
                {
                    linkEditBase = slide + (nint)(address - fileOffset);
                }

                for (uint section = 0; section < sections; section++)
                {
                    nint entry = command + 72 + (nint)(80 * section);
                    string name = Marshal.PtrToStringUTF8(entry, 16).TrimEnd('\0');
                    string owner = Marshal.PtrToStringUTF8(entry + 16, 16).TrimEnd('\0');
                    if (string.Equals(name, "__got", StringComparison.Ordinal) && string.Equals(owner, "__DATA_CONST", StringComparison.Ordinal))
                    {
                        gots.Add((slide + (nint)ReadInt64(entry + 32), ReadInt64(entry + 40), (uint)Marshal.ReadInt32(entry + 68)));
                    }
                }
            }
            else if (kind == SymbolTableCommand)
            {
                symbols = ((uint)Marshal.ReadInt32(command + 8), (uint)Marshal.ReadInt32(command + 16));
            }
            else if (kind == DynamicSymbolTableCommand)
            {
                indirectOffset = (uint)Marshal.ReadInt32(command + 56);
            }

            command += (nint)size;
        }

        if (linkEditBase == 0 || gots.Count == 0 || indirectOffset == 0)
        {
            throw new InvalidOperationException("The CEF image has no __DATA_CONST __got section or indirect symbol table.");
        }

        PatchGots(gots, linkEditBase, symbols, indirectOffset, replacements);
    }

    private static void PatchGots(List<(nint Address, long Size, uint FirstIndirect)> gots, nint linkEditBase, (uint SymbolOffset, uint StringOffset) symbols, uint indirectOffset, Dictionary<string, nint> replacements)
    {
        uint task = (uint)Marshal.ReadInt32(NativeLibrary.GetExport(NativeLibrary.Load(LibSystem), "mach_task_self_"));
        foreach (var got in gots)
        {
            for (long slot = 0; slot < got.Size / 8; slot++)
            {
                uint symbol = (uint)Marshal.ReadInt32(linkEditBase + (nint)indirectOffset + (nint)((got.FirstIndirect + slot) * 4));
                if ((symbol & 0xC0000000) != 0)
                {
                    continue;
                }

                uint nameOffset = (uint)Marshal.ReadInt32(linkEditBase + (nint)symbols.SymbolOffset + (nint)(symbol * 16));
                string? name = Marshal.PtrToStringUTF8(linkEditBase + (nint)symbols.StringOffset + (nint)nameOffset);
                if (name is not null && replacements.TryGetValue(name, out nint replacement))
                {
                    WriteSlot(task, got.Address + (nint)(slot * 8), replacement, name);
                }
            }
        }
    }

    private static long ReadInt64(nint address) => *(long*)address;

    private static void Check(int status, string operation)
    {
        if (status != 0)
        {
            throw new InvalidOperationException($"The browser audio output could not {operation}: OSStatus {status}.");
        }
    }

    private static void WriteSlot(uint task, nint slot, nint replacement, string name)
    {
        nint page = slot & ~(nint)0x3FFF;
        const int ReadWriteCopy = 0x1 | 0x2 | 0x10;
        int status = VmProtect(task, page, 0x4000, 0, ReadWriteCopy);
        if (status != 0)
        {
            throw new InvalidOperationException($"vm_protect failed with {status} for {name}.");
        }

        nint original = Marshal.ReadIntPtr(slot);
        *(nint*)slot = replacement;
        _ = VmProtect(task, page, 0x4000, 0, 0x1);
        replacedSlots++;
        AudioDiagnostics.Note($"replaced {name} at 0x{slot:x} (was 0x{original:x})");
    }

    [System.Diagnostics.Conditional("DENICE_AUDIO_DIAGNOSTICS")]
    private static void Timed(string what, nint unit, RedirectedStream? stream)
    {
#if DENICE_AUDIO_DIAGNOSTICS
        // Diagnostic timing on Chromium's control thread: host time, completed cycles and the cycle in progress, if any.
        BrowserAudioMixer? owner = mixer;
        long completed = owner?.Cycles ?? -1;
        long started = owner?.CycleStarted ?? -1;
        string pulls = stream is null ? string.Empty : $" firstPull={stream.FirstPullCycle} lastPull={stream.LastPullCycle} firstSound={stream.FirstSoundCycle}@{stream.FirstSoundFrame} lastSound={stream.LastSoundCycle}@{stream.LastSoundFrameEnd}";
        AudioDiagnostics.Note($"DENICE-ICPT-EVENT {what} unit=0x{unit:x} device={stream?.Device ?? 0} host={RecordingNative.HostNanoseconds()} completedCycles={completed} cycleInProgress={(started >= completed ? started : -1)}{pulls}");
#endif
    }

    private static void PrepareConverter(RedirectedStream stream)
    {
        // Control thread, stream not being pulled: build the converter for the current format, or reset the existing one.
        if (!StreamRateConverter.IsNeeded(stream))
        {
            stream.Converter?.Dispose();
            stream.Converter = null;
            return;
        }

        if (stream.Converter is { } existing && Math.Abs(existing.SourceRate - stream.Format.SampleRate) < 0.5 && existing.Channels == stream.Format.ChannelsPerFrame)
        {
            existing.Reset();
            return;
        }

        stream.Converter?.Dispose();
        stream.Converter = StreamRateConverter.Create(stream);
        if (stream.Converter.UnknownLayout is { } layout)
        {
            mixer?.Fail($"A browser audio stream's channel layout is not understood ({layout}); its first two channels are played as left and right.");
        }

        AudioDiagnostics.Note($"converter for device {stream.Device}: {stream.Format.SampleRate} Hz, {stream.Format.ChannelsPerFrame} channels, resamples={stream.Converter.Resamples}, prime leading/trailing={stream.Converter.PrimeLeadingFrames}/{stream.Converter.PrimeTrailingFrames}");
    }

    private static uint[] ReadChannelLabels(void* data, uint size)
    {
        // AudioChannelLayout: tag, bitmap, description count, then 20-byte descriptions starting with the label.
        uint tag = *(uint*)data;
        uint count = *((uint*)data + 2);
        if (tag != UseChannelDescriptions || size < 12 + (count * 20))
        {
            return [];
        }

        var labels = new uint[count];
        for (int index = 0; index < count; index++)
        {
            labels[index] = *(uint*)((byte*)data + 12 + (index * 20));
        }

        return labels;
    }

    private static RedirectedStream? Tracked(nint unit)
    {
        lock (Gate)
        {
            return Streams.GetValueOrDefault(unit);
        }
    }

    private static void Fail(Exception error) => mixer?.Fail("Browser audio redirection failed: " + error.Message);

    [UnmanagedCallersOnly]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross into Chromium; the first one is reported through the browser audio sink.")]
    private static int ReplaceAudioComponentInstanceNew(nint component, nint* instance)
    {
        int status = AudioComponentInstanceNew(component, instance);
        try
        {
            if (status == 0 && AudioComponentGetDescription(component, out ComponentDescription description) == 0 && description.SubType == HalOutputSubType)
            {
                lock (Gate)
                {
                    Streams[*instance] = RedirectedStream.Create(*instance);
                    unitsTracked++;
                }

                AudioDiagnostics.Note($"tracked CEF HAL output unit 0x{*instance:x}");
            }
        }
        catch (Exception error)
        {
            Fail(error);
        }

        return status;
    }

    [UnmanagedCallersOnly]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross into Chromium; the first one is reported through the browser audio sink.")]
    private static int ReplaceAudioUnitSetProperty(nint unit, uint property, uint scope, uint element, void* data, uint size)
    {
        try
        {
            RedirectedStream? stream = Tracked(unit);
            if (stream is not null && property == SetRenderCallbackProperty)
            {
                var callback = *(RenderCallbackStruct*)data;
                if (callback.Procedure == 0 && stream.State == StreamState.Registered)
                {
                    mixer?.Unregister(stream);
                }

                stream.RenderCallback = new RedirectedStream.RenderCallbackTarget(callback.Procedure, callback.Context);
                if (stream.State == StreamState.Created)
                {
                    stream.State = StreamState.Configured;
                }

                return 0;
            }

            if (stream is not null && property == ChannelLayoutProperty && scope == 1 && data != null && size >= 12)
            {
                stream.ChannelLabels = ReadChannelLabels(data, size);
                AudioDiagnostics.Note($"unit 0x{unit:x} channel labels [{string.Join(", ", stream.ChannelLabels)}]");
            }
            else if (stream is not null && property == StreamFormatProperty && scope == 1 && data != null)
            {
                var format = *(StreamDescription*)data;
                bool restart = stream.State == StreamState.Registered && mixer is not null;
                if (restart)
                {
                    // A format change on a playing stream: stop pulling it, rebuild its converter, then pull it again.
                    mixer!.Unregister(stream);
                }

                stream.Format = new RedirectedStream.StreamFormat(format.SampleRate, format.Flags, format.ChannelsPerFrame, format.BitsPerChannel);
                if (restart)
                {
                    PrepareConverter(stream);
                    mixer!.Register(stream);
                }

                AudioDiagnostics.Note($"unit 0x{unit:x} format {format.SampleRate} Hz, flags 0x{format.Flags:x}, {format.ChannelsPerFrame} channels, {format.BitsPerChannel} bits");
            }
            else if (stream is not null && property == CurrentDeviceProperty && data != null)
            {
                stream.Device = *(uint*)data;
                AudioDiagnostics.Note($"unit 0x{unit:x} device {stream.Device}");
            }
        }
        catch (Exception error)
        {
            Fail(error);
        }

        return AudioUnitSetProperty(unit, property, scope, element, data, size);
    }

    [UnmanagedCallersOnly]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross into Chromium; the first one is reported through the browser audio sink.")]
    private static int ReplaceAudioOutputUnitStart(nint unit)
    {
        try
        {
            RedirectedStream? stream = Tracked(unit);
            if (stream is not null && mixer is not null)
            {
                if (stream.Format.Flags != FloatPackedNonInterleaved)
                {
                    mixer.Fail($"A browser audio stream was not played or recorded: its format flags 0x{stream.Format.Flags:x} are not non-interleaved float.");
                    return 0;
                }

                PrepareConverter(stream);

                Timed("start-before-register", unit, stream);
                mixer.Register(stream);
                Timed("start-after-register", unit, stream);
                lock (Gate)
                {
                    startsRedirected++;
                }

                AudioDiagnostics.Note($"redirected start of unit 0x{unit:x} (device {stream.Device}, {stream.Format.SampleRate} Hz)");
                return 0;
            }
        }
        catch (Exception error)
        {
            Fail(error);
            return -50;
        }

        if (Tracked(unit) is not null)
        {
            lock (Gate)
            {
                startsForwarded++;
            }
        }

        return AudioOutputUnitStart(unit);
    }

    [UnmanagedCallersOnly]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross into Chromium; the first one is reported through the browser audio sink.")]
    private static int ReplaceAudioOutputUnitStop(nint unit)
    {
        try
        {
            RedirectedStream? stream = Tracked(unit);
            if (stream is not null)
            {
                Timed("stop-before-unregister", unit, stream);
                mixer?.Unregister(stream);
                Timed("stop-after-unregister", unit, stream);
                AudioDiagnostics.Note($"redirected stop of unit 0x{unit:x}");
                return 0;
            }
        }
        catch (Exception error)
        {
            Fail(error);
            return -50;
        }

        return AudioOutputUnitStop(unit);
    }

    [UnmanagedCallersOnly]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross into Chromium; the first one is reported through the browser audio sink.")]
    private static int ReplaceAudioComponentInstanceDispose(nint unit)
    {
        try
        {
            RedirectedStream? stream = Tracked(unit);
            if (stream is not null)
            {
                mixer?.Unregister(stream);
                Timed("dispose", unit, stream);
                lock (Gate)
                {
                    Streams.Remove(unit);
                    disposals++;
                }

                // Unregister has returned, so the render thread is not inside this stream's callback and never enters it again.
                stream.State = StreamState.Disposed;
                stream.Dispose();
            }
        }
        catch (Exception error)
        {
            Fail(error);
        }

        return AudioComponentInstanceDispose(unit);
    }

    /// <summary>AudioComponentDescription.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct ComponentDescription(uint Type, uint SubType, uint Manufacturer, uint Flags, uint FlagsMask);

    /// <summary>AudioStreamBasicDescription.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct StreamDescription(double SampleRate, uint FormatId, uint Flags, uint BytesPerPacket, uint FramesPerPacket, uint BytesPerFrame, uint ChannelsPerFrame, uint BitsPerChannel, uint Reserved);

    /// <summary>AURenderCallbackStruct.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct RenderCallbackStruct(nint Procedure, nint Context);
}
