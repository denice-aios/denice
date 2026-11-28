// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using System.Text;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.MacOS.Browser;

/// <summary>
/// Routes Chromium's page speech into the browser audio mixer (from the approved interception design,
/// document-0b78de3410fd424d849566aebdb47c67). It replaces AVSpeechSynthesizer's speaking methods in DENICE's process, so
/// speech is rendered with writeUtterance:toBufferCallback:toMarkerCallback: into the mixer instead of the speaker, while
/// Chromium's delegate still receives its start, word-range and finish events on the main thread when the mixer plays them.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio interop: Objective-C method replacements and global blocks exchange stable unmanaged function pointers with AVFoundation and Chromium.")]
internal static unsafe class SpeechRedirection
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";
    private const int BlockIsGlobal = 1 << 28;
    private const int BlockHasSignature = 1 << 30;
    private const nint WordMark = 1;
    private const string ReleaseSelector = "release";
    private static readonly Lock Gate = new();
    private static readonly List<WordMarker> Markers = [];
    private static readonly Queue<(nint Synthesizer, nint Utterance)> Pending = new();
    private static readonly HashSet<nuint> WordLocations = [];
    private static BrowserAudioMixer? mixer;
    private static nint send;
    private static nint bufferBlock;
    private static nint markerBlock;
    private static nint synthesizer;
    private static nint utterance;
    private static bool rendering;
    private static bool dropRender;
    private static bool startSent;
    private static long utteranceBase;
    private static long utteranceFrames;
    private static long finishTarget = -1;
    private static double sourceRate;
    private static int sourceBytesPerFrame;
    private static long sourceFrames;
    private static LinearResampler? resampler;
    private static int utterances;
    private static int words;
    private static nint speechClass;
    private static nint originalSpeak;
    [ThreadStatic]
    private static bool writing;
    private static (string Selector, nint Implementation)[] installed = [];
    private static nint listener;
    private static nint proxy;

    /// <summary>Gets the speech counters as text.</summary>
    internal static string Report
    {
        get
        {
            lock (Gate)
            {
                return $"utterances={utterances}, word events={words}, dispatch reaches replacements={DispatchReachesReplacements()}";
            }
        }
    }

    /// <summary>Replaces AVSpeechSynthesizer's speaking methods, once per process, before CefRuntime.Initialize.</summary>
    /// <param name="target">The started mixer that plays the speech.</param>
    internal static void Install(BrowserAudioMixer target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (mixer is not null)
        {
            throw new InvalidOperationException("Browser speech redirection is already installed in this process.");
        }

        mixer = target;
        mixer.MainThreadTick = Tick;
        _ = NativeLibrary.Load("/System/Library/Frameworks/AVFoundation.framework/AVFoundation");
        nint objc = NativeLibrary.Load(ObjC);
        send = NativeLibrary.GetExport(objc, "objc_msgSend");
        var getClass = (delegate* unmanaged<byte*, nint>)NativeLibrary.GetExport(objc, "objc_getClass");
        var getMethod = (delegate* unmanaged<nint, nint, nint>)NativeLibrary.GetExport(objc, "class_getInstanceMethod");
        nint cls;
        fixed (byte* name = "AVSpeechSynthesizer\0"u8)
        {
            cls = getClass(name);
        }

        if (cls == 0)
        {
            throw new InvalidOperationException("AVSpeechSynthesizer is not available.");
        }

        speechClass = cls;
        var replacements = new (string Selector, nint Implementation)[]
        {
            ("speakUtterance:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&SpeakUtterance),
            ("stopSpeakingAtBoundary:", (nint)(delegate* unmanaged<nint, nint, nint, byte>)&StopSpeaking),
            ("pauseSpeakingAtBoundary:", (nint)(delegate* unmanaged<nint, nint, nint, byte>)&PauseSpeaking),
            ("continueSpeaking", (nint)(delegate* unmanaged<nint, nint, byte>)&ContinueSpeaking),
            ("isSpeaking", (nint)(delegate* unmanaged<nint, nint, byte>)&Speaking),
        };
        if (getMethod(cls, Selector("speakUtterance:")) == 0)
        {
            throw new InvalidOperationException("AVSpeechSynthesizer has no speakUtterance: method.");
        }

        // The private TextToSpeech framework adds a category with its own implementations, which dispatch reaches, and it
        // attaches lazily; creating one synthesizer loads it before every listed entry is replaced.
        nint probe = Send(Send(cls, "alloc"), "init");
        _ = Send(probe, ReleaseSelector);
        installed = replacements;
        var lookup = (delegate* unmanaged<nint, nint, nint>)NativeLibrary.GetExport(objc, "class_getMethodImplementation");
        originalSpeak = lookup(cls, Selector("speakUtterance:"));
        int replaced = ReplaceAll();
        if (!DispatchReachesReplacements())
        {
            throw new InvalidOperationException("AVSpeechSynthesizer dispatch does not reach the speech replacement.");
        }

        AudioDiagnostics.Note($"replaced {replaced} AVSpeechSynthesizer method entries");
        proxy = CreateProxyDelegate();
        bufferBlock = GlobalBlock((nint)(delegate* unmanaged<nint, nint, void>)&OnBuffer);
        markerBlock = GlobalBlock((nint)(delegate* unmanaged<nint, nint, void>)&OnMarkers);
    }

    private static nint CreateProxyDelegate()
    {
        // While the system renders an utterance, this object is the synthesizer's delegate, so the renderer's own start,
        // word and finish calls do not reach Chromium early; word ranges are kept against the mixer position instead.
        nint objc = NativeLibrary.Load(ObjC);
        var allocate = (delegate* unmanaged<nint, byte*, nuint, nint>)NativeLibrary.GetExport(objc, "objc_allocateClassPair");
        var addMethod = (delegate* unmanaged<nint, nint, nint, byte*, byte>)NativeLibrary.GetExport(objc, "class_addMethod");
        var register = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(objc, "objc_registerClassPair");
        var getClass = (delegate* unmanaged<byte*, nint>)NativeLibrary.GetExport(objc, "objc_getClass");
        nint parent;
        fixed (byte* name = "NSObject\0"u8)
        {
            parent = getClass(name);
        }

        nint cls;
        fixed (byte* name = "DeniceSpeechRenderDelegate\0"u8)
        {
            cls = allocate(parent, name, 0);
        }

        fixed (byte* types = "v@:@{_NSRange=QQ}@\0"u8)
        {
            _ = addMethod(cls, Selector("speechSynthesizer:willSpeakRangeOfSpeechString:utterance:"), (nint)(delegate* unmanaged<nint, nint, nint, nuint, nuint, nint, void>)&OnWillSpeak, types);
        }

        register(cls);
        return Send(Send(cls, "alloc"), "init");
    }

    [UnmanagedCallersOnly]
    private static void OnWillSpeak(nint self, nint command, nint speaker, nuint location, nuint length, nint spoken)
    {
        lock (Gate)
        {
            if (utterance != 0 && !dropRender)
            {
                AddWord(utteranceBase + utteranceFrames, new TextRange(location, length));
            }
        }
    }

    private static void AddWord(long target, TextRange range)
    {
        // Called with Gate held; one event per character range, whichever source reports it first.
        if (WordLocations.Add(range.Location))
        {
            Markers.Add(new WordMarker(target, range));
        }
    }

    private static void RestoreDelegate(nint speaker)
    {
        if (speaker != 0 && listener != 0)
        {
            _ = Send(speaker, "setDelegate:", listener);
        }
    }

    private static int ReplaceAll()
    {
        // Replaces every listed entry of each selector, including entries added by categories.
        nint objc = NativeLibrary.Load(ObjC);
        var copyList = (delegate* unmanaged<nint, uint*, nint*>)NativeLibrary.GetExport(objc, "class_copyMethodList");
        var methodName = (delegate* unmanaged<nint, nint>)NativeLibrary.GetExport(objc, "method_getName");
        var getImplementation = (delegate* unmanaged<nint, nint>)NativeLibrary.GetExport(objc, "method_getImplementation");
        var setImplementation = (delegate* unmanaged<nint, nint, nint>)NativeLibrary.GetExport(objc, "method_setImplementation");
        var free = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(NativeLibrary.Load(LibSystem), "free");
        uint count;
        nint* methods = copyList(speechClass, &count);
        int replaced = 0;
        foreach ((string selector, nint implementation) in installed)
        {
            nint sel = Selector(selector);
            for (uint index = 0; index < count; index++)
            {
                if (methodName(methods[index]) == sel && getImplementation(methods[index]) != implementation)
                {
                    _ = setImplementation(methods[index], implementation);
                    replaced++;
                }
            }
        }

        free((nint)methods);
        return replaced;
    }

    private static bool DispatchReachesReplacements()
    {
        // Whether objc dispatch reaches every replacement; a category attached later can hide one.
        var lookup = (delegate* unmanaged<nint, nint, nint>)NativeLibrary.GetExport(NativeLibrary.Load(ObjC), "class_getMethodImplementation");
        return installed.Length > 0 && installed.All(entry => lookup(speechClass, Selector(entry.Selector)) == entry.Implementation);
    }

    private static nint Selector(string name)
    {
        var register = (delegate* unmanaged<byte*, nint>)NativeLibrary.GetExport(NativeLibrary.Load(ObjC), "sel_registerName");
        byte[] bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* text = bytes)
        {
            return register(text);
        }
    }

    private static nint Send(nint receiver, string selector) => ((delegate* unmanaged<nint, nint, nint>)send)(receiver, Selector(selector));

    private static nint Send(nint receiver, string selector, nint argument) => ((delegate* unmanaged<nint, nint, nint, nint>)send)(receiver, Selector(selector), argument);

    private static bool Responds(nint receiver, string selector) => receiver != 0 && ((delegate* unmanaged<nint, nint, nint, byte>)send)(receiver, Selector("respondsToSelector:"), Selector(selector)) != 0;

    private static nint GlobalBlock(nint invoke)
    {
        // A global block: isa, flags, reserved, invoke, descriptor {reserved, size, signature}.
        nint descriptor = Marshal.AllocHGlobal(24);
        byte[] text = "v16@?0@8\0"u8.ToArray();
        nint signature = Marshal.AllocHGlobal(text.Length);
        text.CopyTo(new Span<byte>((void*)signature, text.Length));
        *(nint*)descriptor = 0;
        *(nint*)(descriptor + 8) = 32;
        *(nint*)(descriptor + 16) = signature;
        nint block = Marshal.AllocHGlobal(32);
        *(nint*)block = NativeLibrary.GetExport(NativeLibrary.Load(LibSystem), "_NSConcreteGlobalBlock");
        *(int*)(block + 8) = BlockIsGlobal | BlockHasSignature;
        *(int*)(block + 12) = 0;
        *(nint*)(block + 16) = invoke;
        *(nint*)(block + 24) = descriptor;
        return block;
    }

    [System.Diagnostics.Conditional("DENICE_AUDIO_DIAGNOSTICS")]
    private static void Mark(string point) => AudioDiagnostics.Note($"timing {point} host={Recording.RecordingNative.HostNanoseconds()}");

    private static void Fail(string what, Exception error) => mixer?.Fail($"Browser speech {what} failed: {error.Message}");

    private static void Begin(nint speaker, nint spoken)
    {
        // Called with Gate held.
        synthesizer = speaker;
        utterance = spoken;
        rendering = true;
        startSent = false;
        utteranceBase = mixer!.SpeechFramesQueued;
        utteranceFrames = 0;
        finishTarget = -1;
        sourceRate = 0;
        sourceBytesPerFrame = 0;
        sourceFrames = 0;
        resampler = null;
        utterances++;
        WordLocations.Clear();
        nint current = Send(speaker, "delegate");
        if (current != proxy)
        {
            listener = current;
        }

        _ = Send(speaker, "setDelegate:", proxy);

        // writeUtterance calls speakUtterance: on the same thread to start rendering; that call goes to the original.
        writing = true;
        try
        {
            ((delegate* unmanaged<nint, nint, nint, nint, nint, void>)send)(speaker, Selector("writeUtterance:toBufferCallback:toMarkerCallback:"), spoken, bufferBlock, markerBlock);
        }
        finally
        {
            writing = false;
        }

        Mark("write-returned");
    }

    [UnmanagedCallersOnly]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross into Chromium; the first one is reported through the browser audio sink.")]
    private static void SpeakUtterance(nint self, nint command, nint spoken)
    {
        if (writing)
        {
            ((delegate* unmanaged<nint, nint, nint, void>)originalSpeak)(self, command, spoken);
            return;
        }

        try
        {
            Mark("speak-request");
            _ = Send(spoken, "retain");
            lock (Gate)
            {
                if (rendering || finishTarget >= 0)
                {
                    Pending.Enqueue((self, spoken));
                    return;
                }

                Begin(self, spoken);
            }
        }
        catch (Exception error)
        {
            Fail("speak", error);
        }
    }

    [UnmanagedCallersOnly]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross into Chromium; the first one is reported through the browser audio sink.")]
    private static byte StopSpeaking(nint self, nint command, nint boundary)
    {
        try
        {
            nint cancelled;
            lock (Gate)
            {
                cancelled = utterance;
                dropRender = rendering;
                utterance = 0;
                finishTarget = -1;
                Markers.Clear();
                while (Pending.TryDequeue(out var queued))
                {
                    _ = Send(queued.Utterance, ReleaseSelector);
                }

                mixer?.ClearSpeech();
                mixer?.HoldSpeech(false);
            }

            RestoreDelegate(self);
            if (cancelled != 0)
            {
                if (Responds(listener, "speechSynthesizer:didCancelSpeechUtterance:"))
                {
                    ((delegate* unmanaged<nint, nint, nint, nint, void>)send)(listener, Selector("speechSynthesizer:didCancelSpeechUtterance:"), self, cancelled);
                }

                _ = Send(cancelled, ReleaseSelector);
                AudioDiagnostics.Note("speech stopped");
            }

            return 1;
        }
        catch (Exception error)
        {
            Fail("stop", error);
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static byte PauseSpeaking(nint self, nint command, nint boundary)
    {
        mixer?.HoldSpeech(true);
        Mark("paused");
        return 1;
    }

    [UnmanagedCallersOnly]
    private static byte ContinueSpeaking(nint self, nint command)
    {
        mixer?.HoldSpeech(false);
        Mark("continued");
        return 1;
    }

    [UnmanagedCallersOnly]
    private static byte Speaking(nint self, nint command)
    {
        lock (Gate)
        {
            return (byte)(rendering || finishTarget >= 0 || Pending.Count > 0 ? 1 : 0);
        }
    }

    [UnmanagedCallersOnly]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross into AVFoundation; the first one is reported through the browser audio sink.")]
    private static void OnBuffer(nint block, nint buffer)
    {
        try
        {
            uint frames = ((delegate* unmanaged<nint, nint, uint>)send)(buffer, Selector("frameLength"));
            lock (Gate)
            {
                if (frames == 0)
                {
                    EndRender();
                    return;
                }

                if (dropRender)
                {
                    return;
                }

                float[] source = ReadChannel(buffer, frames);
                float[] converted = Resample(source);
                utteranceFrames += converted.Length;
                mixer!.QueueSpeech(converted);
            }
        }
        catch (Exception error)
        {
            Fail("buffer", error);
        }
    }

    private static void EndRender()
    {
        // Called with Gate held, on AVFoundation's first empty buffer; later empty buffers of the same render are ignored.
        if (!rendering)
        {
            return;
        }

        rendering = false;
        if (dropRender)
        {
            dropRender = false;
        }
        else if (utterance != 0)
        {
            finishTarget = utteranceBase + utteranceFrames;
            AudioDiagnostics.Note($"rendered {sourceFrames} speech frames at {sourceRate} Hz into {utteranceFrames} mixer frames");
        }

        if (finishTarget < 0 && Pending.TryDequeue(out var next))
        {
            Begin(next.Synthesizer, next.Utterance);
        }
    }

    private static float[] ReadChannel(nint buffer, uint frames)
    {
        nint format = Send(buffer, "format");
        double rate = ((delegate* unmanaged<nint, nint, double>)send)(format, Selector("sampleRate"));
        nint common = Send(format, "commonFormat");
        nint description = Send(format, "streamDescription");
        bool first = sourceRate == 0;
        if (first)
        {
            sourceRate = rate;
            sourceBytesPerFrame = *(int*)(description + 24);
        }

        sourceFrames += frames;
        var samples = new float[frames];
        if (common == 1)
        {
            float* channel = *(float**)Send(buffer, "floatChannelData");
            new Span<float>(channel, (int)frames).CopyTo(samples);
        }
        else if (common == 3)
        {
            short* channel = *(short**)Send(buffer, "int16ChannelData");
            for (int index = 0; index < frames; index++)
            {
                samples[index] = channel[index] / 32768f;
            }
        }
        else
        {
            throw new InvalidOperationException($"Speech buffer common format {common} is not supported.");
        }

        if (first)
        {
            Mark("first-buffer");
        }

        return samples;
    }

    private static float[] Resample(float[] source)
    {
        resampler ??= new LinearResampler(sourceRate, BrowserAudioMixer.SampleRate);
        return resampler.Process(source);
    }

    [UnmanagedCallersOnly]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross into AVFoundation; the first one is reported through the browser audio sink.")]
    private static void OnMarkers(nint block, nint markers)
    {
        try
        {
            nint count = Send(markers, "count");
            lock (Gate)
            {
                for (nint index = 0; index < count; index++)
                {
                    nint marker = Send(markers, "objectAtIndex:", index);
                    if (Send(marker, "mark") != WordMark || dropRender)
                    {
                        continue;
                    }

                    TextRange range = ((delegate* unmanaged<nint, nint, TextRange>)send)(marker, Selector("textRange"));
                    nint offset = Send(marker, "byteSampleOffset");
                    double rate = sourceRate > 0 ? sourceRate : 22050;
                    int bytes = sourceBytesPerFrame > 0 ? sourceBytesPerFrame : 4;
                    AddWord(utteranceBase + (long)(offset / bytes * BrowserAudioMixer.SampleRate / rate), range);
                }
            }
        }
        catch (Exception error)
        {
            Fail("marker", error);
        }
    }

    private static void Tick()
    {
        // Main thread: a category attached after Install would hide the replacement again; re-apply and record it.
        int late = ReplaceAll();
        if (late > 0)
        {
            AudioDiagnostics.Note($"re-applied {late} AVSpeechSynthesizer entries after a late category");
        }

        // Deliver Chromium's delegate events once the mixer has played up to them.
        nint speaker;
        nint spoken;
        bool start = false;
        bool finish = false;
        var reached = new List<TextRange>();
        lock (Gate)
        {
            if (utterance == 0 || mixer is null)
            {
                return;
            }

            speaker = synthesizer;
            spoken = utterance;
            long mixed = mixer.SpeechFramesDone;
            if (!startSent && mixed > utteranceBase)
            {
                startSent = true;
                start = true;
            }

            foreach (WordMarker marker in Markers.Where(marker => marker.Target <= mixed).ToList())
            {
                reached.Add(marker.Range);
                Markers.Remove(marker);
            }

            if (finishTarget >= 0 && mixed >= finishTarget)
            {
                finish = true;
                utterance = 0;
                finishTarget = -1;
                Markers.Clear();
            }
        }

        Deliver(speaker, spoken, start, reached, finish);
    }

    private static void Deliver(nint speaker, nint spoken, bool start, List<TextRange> reached, bool finish)
    {
        if (reached.Count > 0 && !Responds(listener, "speechSynthesizer:willSpeakRangeOfSpeechString:utterance:"))
        {
            reached.Clear();
        }

        if (start && Responds(listener, "speechSynthesizer:didStartSpeechUtterance:"))
        {
            ((delegate* unmanaged<nint, nint, nint, nint, void>)send)(listener, Selector("speechSynthesizer:didStartSpeechUtterance:"), speaker, spoken);
            Mark("first-mixed-and-start-delivered");
        }

        foreach (TextRange range in reached)
        {
            ((delegate* unmanaged<nint, nint, nint, nuint, nuint, nint, void>)send)(listener, Selector("speechSynthesizer:willSpeakRangeOfSpeechString:utterance:"), speaker, range.Location, range.Length, spoken);
            lock (Gate)
            {
                words++;
            }
        }

        if (finish)
        {
            if (Responds(listener, "speechSynthesizer:didFinishSpeechUtterance:"))
            {
                ((delegate* unmanaged<nint, nint, nint, nint, void>)send)(listener, Selector("speechSynthesizer:didFinishSpeechUtterance:"), speaker, spoken);
            }

            Mark("finish-delivered");
            RestoreDelegate(speaker);
            _ = Send(spoken, ReleaseSelector);
            lock (Gate)
            {
                if (!rendering && Pending.TryDequeue(out var next))
                {
                    Begin(next.Synthesizer, next.Utterance);
                }
            }
        }
    }

    /// <summary>NSRange.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct TextRange(nuint Location, nuint Length);

    /// <summary>A word marker from writeUtterance with its offset in the rendered speech.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct WordMarker(long Target, TextRange Range);
}
