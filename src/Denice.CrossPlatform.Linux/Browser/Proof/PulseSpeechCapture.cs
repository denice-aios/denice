// Copyright (c) Denice.ai. All rights reserved.

using System.Globalization;
using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Linux.Browser;

/// <summary>
/// Page speech on Linux: a DENICE null sink, denice_speech_&lt;pid&gt;, that a DENICE-private speech-dispatcher plays to
/// (its configuration names the sink in AudioPulseDevice), and a DENICE record stream on that sink's monitor whose
/// 48 kHz mono samples the mixer adds to both channels. Stale denice_speech_* sinks from earlier runs are unloaded
/// first; the sink is unloaded on disposal. SPEECHD_ADDRESS is set with libc setenv, so Chromium's libspeechd, which
/// reads the native environment, connects to the private daemon.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: libpulse callbacks exchange unmanaged buffers.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross the libpulse callback.")]
internal sealed unsafe partial class PulseSpeechCapture : IRedirectedSource, IDisposable
{
    private const string SinkPrefix = "denice_speech_";
    private readonly RedirectedRing _ring = new(MixerCore.SampleRate * 2) { PrebufferFrames = 960 };
    private readonly float[] _scratch = new float[MixerCore.SampleRate];
    private readonly PulseConnection _connection;
    private readonly TimeProvider _clock;
    private readonly List<uint> _modules = [];
    private GCHandle _self;
    private nint _stream;
    private uint _module = uint.MaxValue;
    private long _framesCaptured;
    private long _soundingFrames;
    private volatile bool _armed;

    private PulseSpeechCapture(PulseConnection connection, TimeProvider clock, string sink)
    {
        _connection = connection;
        _clock = clock;
        Sink = sink;
        _self = GCHandle.Alloc(this);
    }

    /// <summary>Gets the null sink's name.</summary>
    internal string Sink { get; }

    /// <summary>Gets the frames captured from the sink's monitor.</summary>
    internal long FramesCaptured => Interlocked.Read(ref _framesCaptured);

    /// <summary>Gets the captured frames that were not silent.</summary>
    internal long SoundingFrames => Interlocked.Read(ref _soundingFrames);

    /// <summary>Gets the frames the mixer took from the capture.</summary>
    internal long FramesMixed => _ring.Mixed;

    /// <inheritdoc />
    public void MixInto(Span<float> left, Span<float> right, long hostNanoseconds, long cycle) => _ = _ring.MixInto(left, right);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_stream != 0)
        {
            _connection.Locked(() =>
            {
                _ = PulseNative.StreamDisconnect(_stream);
                PulseNative.StreamUnref(_stream);
            });
            _stream = 0;
        }

        if (_module != uint.MaxValue)
        {
            Unload(_module);
            AudioProofLog.Note($"unloaded speech sink {Sink} (module {_module})");
            _module = uint.MaxValue;
        }

        _connection.Dispose();
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    /// <summary>Loads the speech sink and starts capturing its monitor.</summary>
    /// <param name="processId">This process's id, which names the sink.</param>
    /// <param name="clock">The injected clock.</param>
    /// <returns>The capture.</returns>
    internal static PulseSpeechCapture Start(int processId, TimeProvider clock)
    {
        PulseConnection connection = PulseConnection.Open("DENICE browser speech", clock);
        var capture = new PulseSpeechCapture(connection, clock, SinkPrefix + processId.ToString(CultureInfo.InvariantCulture));
        capture.RemoveStaleSinks();
        capture.LoadSink();
        capture.StartMonitor();
        return capture;
    }

    /// <summary>Points libspeechd at the private daemon through the native environment.</summary>
    /// <param name="socketPath">The daemon's unix socket.</param>
    internal static void UseSpeechAddress(string socketPath)
    {
        if (SetEnvironment("SPEECHD_ADDRESS", "unix_socket:" + socketPath, 1) != 0)
        {
            throw new InvalidOperationException("setenv SPEECHD_ADDRESS failed.");
        }

        AudioProofLog.Note("SPEECHD_ADDRESS=unix_socket:" + socketPath);
    }

    /// <summary>
    /// Starts queuing the monitor's samples for the mixer. Until then they are counted and dropped, so the mix receives no
    /// backlog captured before the source joined it (a full queue of earlier silence measured as a 2.8 s speech lag).
    /// </summary>
    internal void Arm() => _armed = true;

    [LibraryImport("libc.so.6", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetEnvironment(string name, string value, int overwrite);

    [UnmanagedCallersOnly]
    private static void ModuleListed(nint context, nint info, int last, nint userdata)
    {
        if (GCHandle.FromIntPtr(userdata).Target is not PulseSpeechCapture capture)
        {
            return;
        }

        if (last == 0 && info != 0)
        {
            string? name = Marshal.PtrToStringUTF8(*(nint*)(info + 8));
            string? argument = Marshal.PtrToStringUTF8(*(nint*)(info + 16));
            if (string.Equals(name, "module-null-sink", StringComparison.Ordinal) && argument is not null && argument.Contains("sink_name=" + SinkPrefix, StringComparison.Ordinal))
            {
                capture._modules.Add(*(uint*)info);
            }
        }

        PulseNative.MainloopSignal(capture._connection.Mainloop, 0);
    }

    [UnmanagedCallersOnly]
    private static void ModuleLoaded(nint context, uint index, nint userdata)
    {
        if (GCHandle.FromIntPtr(userdata).Target is PulseSpeechCapture capture)
        {
            capture._module = index;
            PulseNative.MainloopSignal(capture._connection.Mainloop, 0);
        }
    }

    [UnmanagedCallersOnly]
    private static void Captured(nint stream, nuint bytes, nint userdata)
    {
        try
        {
            if (GCHandle.FromIntPtr(userdata).Target is PulseSpeechCapture capture)
            {
                capture.Drain(stream);
            }
        }
        catch (Exception error)
        {
            AudioProofLog.Note("FAILED speech capture: " + error.Message);
        }
    }

    private void RemoveStaleSinks()
    {
        nint operation = 0;
        _connection.Locked(() => operation = PulseNative.ContextGetModuleInfoList(_connection.Context, (nint)(delegate* unmanaged<nint, nint, int, nint, void>)&ModuleListed, GCHandle.ToIntPtr(_self)));
        _connection.Await(operation, _clock, "list modules");
        foreach (uint stale in _modules)
        {
            Unload(stale);
            AudioProofLog.Note($"unloaded stale speech sink module {stale}");
        }
    }

    private void LoadSink()
    {
        nint operation = 0;
        string argument = $"sink_name={Sink} sink_properties=device.description={Sink} rate={MixerCore.SampleRate} channels=1";
        _connection.Locked(() => operation = PulseNative.ContextLoadModule(_connection.Context, "module-null-sink", argument, (nint)(delegate* unmanaged<nint, uint, nint, void>)&ModuleLoaded, GCHandle.ToIntPtr(_self)));
        _connection.Await(operation, _clock, "load the speech sink");
        if (_module == uint.MaxValue)
        {
            throw new InvalidOperationException("The speech sink did not load.");
        }

        AudioProofLog.Note($"loaded speech sink {Sink} (module {_module})");
    }

    private void Unload(uint module)
    {
        nint operation = 0;
        _connection.Locked(() => operation = PulseNative.ContextUnloadModule(_connection.Context, module, PulseConnection.SuccessCallback, _connection.Mainloop));
        _connection.Await(operation, _clock, "unload a speech sink");
    }

    private void StartMonitor()
    {
        _connection.Locked(() =>
        {
            _stream = _connection.NewStream("DENICE speech capture", 1);
            PulseNative.StreamSetReadCallback(_stream, (nint)(delegate* unmanaged<nint, nuint, nint, void>)&Captured, GCHandle.ToIntPtr(_self));
            var attributes = new PulseNative.BufferAttributes { MaximumLength = uint.MaxValue, TargetLength = uint.MaxValue, Prebuffer = uint.MaxValue, MinimumRequest = uint.MaxValue, FragmentSize = 480 * 4 };
            if (PulseNative.StreamConnectRecord(_stream, Sink + ".monitor", &attributes, PulseNative.AdjustLatency) < 0)
            {
                throw new InvalidOperationException("The speech sink's monitor could not be recorded.");
            }
        });
        _connection.WaitUntil(() => PulseNative.StreamGetState(_stream) == PulseNative.StreamReady, _clock, "the speech monitor stream");
        AudioProofLog.Note($"recording {Sink}.monitor into the mixer");
    }

    private void Drain(nint stream)
    {
        while (true)
        {
            nint data;
            nuint bytes;
            if (PulseNative.StreamPeek(stream, &data, &bytes) < 0 || bytes == 0)
            {
                return;
            }

            int frames = Math.Min((int)bytes / 4, _scratch.Length);
            if (data == 0)
            {
                Array.Clear(_scratch, 0, frames);
            }
            else
            {
                new ReadOnlySpan<float>((void*)data, frames).CopyTo(_scratch);
            }

            ReadOnlySpan<float> samples = _scratch.AsSpan(0, frames);
            int sounding = 0;
            foreach (float sample in samples)
            {
                sounding += Math.Abs(sample) > 1e-4f ? 1 : 0;
            }

            if (_armed)
            {
                _ = _ring.Write(samples, samples);
            }

            Interlocked.Add(ref _framesCaptured, frames);
            Interlocked.Add(ref _soundingFrames, sounding);
            _ = PulseNative.StreamDrop(stream);
        }
    }
}
