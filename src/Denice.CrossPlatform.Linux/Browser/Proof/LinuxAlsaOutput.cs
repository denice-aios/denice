// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Linux.Browser;

/// <summary>
/// The Linux mixer output when no PulseAudio-compatible server is reachable: one 48 kHz float stereo playback PCM on the
/// ALSA "default" device, through DENICE's own libasound imports (not libcef.so's redirected slots). A writer thread
/// renders one 240-frame cycle at a time and blocks in snd_pcm_writei, which paces it at the device's rate.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: libasound takes unmanaged buffers.")]
internal sealed unsafe partial class LinuxAlsaOutput : IMixerOutput
{
    private const string Alsa = "libasound.so.2";
    private const int Frames = 240;
    private const int FloatLittleEndian = 14;
    private const int Interleaved = 3;
    private readonly float[] _left = new float[Frames];
    private readonly float[] _right = new float[Frames];
    private readonly float[] _interleaved = new float[Frames * 2];
    private nint _pcm;
    private Thread? _writer;
    private volatile bool _stopping;
    private long _cycles;
    private long _errors;

    /// <summary>Gets the cycles written so far.</summary>
    internal long Cycles => Interlocked.Read(ref _cycles);

    /// <inheritdoc />
    public void Start(IMixerRender render, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(clock);
        if (_pcm != 0)
        {
            throw new InvalidOperationException("The browser audio output has already started.");
        }

        nint pcm;
        if (Open(&pcm, "default", 0, 0) < 0)
        {
            throw new InvalidOperationException("DENICE could not open the ALSA default device.");
        }

        _pcm = pcm;
        if (SetParams(_pcm, FloatLittleEndian, Interleaved, 2, MixerCore.SampleRate, 1, 20_000) < 0)
        {
            Stop();
            throw new InvalidOperationException("The ALSA default device refused 48 kHz float stereo.");
        }

        _stopping = false;
        _writer = new Thread(() => Run(render, clock)) { IsBackground = true, Name = "DENICE ALSA mixer output" };
        _writer.Start();
        MixerOutputStart.WaitForFirstCycle(() => Cycles, clock);
        AudioProofLog.Note("DENICE mixer output: ALSA playback PCM on default, 48 kHz float stereo");
    }

    /// <inheritdoc />
    public void Stop()
    {
        _stopping = true;
        _writer?.Join();
        _writer = null;
        if (_pcm == 0)
        {
            return;
        }

        _ = Drop(_pcm);
        _ = Close(_pcm);
        _pcm = 0;
        AudioProofLog.Note($"DENICE ALSA output stopped after {Cycles} cycles, {Interlocked.Read(ref _errors)} write errors");
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    [LibraryImport(Alsa, EntryPoint = "snd_pcm_open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(nint* pcm, string name, int stream, int mode);

    [LibraryImport(Alsa, EntryPoint = "snd_pcm_set_params")]
    private static partial int SetParams(nint pcm, int format, int access, uint channels, uint rate, int resample, uint latency);

    [LibraryImport(Alsa, EntryPoint = "snd_pcm_writei")]
    private static partial long WriteInterleaved(nint pcm, float* buffer, ulong frames);

    [LibraryImport(Alsa, EntryPoint = "snd_pcm_recover")]
    private static partial int Recover(nint pcm, int error, int silent);

    [LibraryImport(Alsa, EntryPoint = "snd_pcm_drop")]
    private static partial int Drop(nint pcm);

    [LibraryImport(Alsa, EntryPoint = "snd_pcm_close")]
    private static partial int Close(nint pcm);

    private void Run(IMixerRender render, TimeProvider clock)
    {
        while (!_stopping)
        {
            long host = (long)(clock.GetTimestamp() * (1_000_000_000.0 / clock.TimestampFrequency));
            render.RenderCycle(_left, _right, host);
            for (int frame = 0; frame < Frames; frame++)
            {
                _interleaved[2 * frame] = _left[frame];
                _interleaved[(2 * frame) + 1] = _right[frame];
            }

            fixed (float* samples = _interleaved)
            {
                long written = WriteInterleaved(_pcm, samples, Frames);
                if (written < 0)
                {
                    _ = Interlocked.Increment(ref _errors);
                    _ = Recover(_pcm, (int)written, 1);
                }
            }

            _ = Interlocked.Increment(ref _cycles);
        }
    }
}
