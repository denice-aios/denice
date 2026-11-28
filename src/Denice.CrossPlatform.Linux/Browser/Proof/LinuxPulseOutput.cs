// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.Linux.Browser;

/// <summary>
/// The Linux mixer output with a PulseAudio-compatible server: one 48 kHz float stereo playback stream on the default
/// sink, through DENICE's own libpulse imports and threaded mainloop. Each server write request is one or more mixer
/// cycles of at most <see cref="MixerCore.MaximumCycleFrames"/> frames.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: libpulse write callbacks exchange unmanaged buffers.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A managed exception must not cross the libpulse callback; the render records its own failures.")]
internal sealed unsafe class LinuxPulseOutput : IMixerOutput
{
    private readonly float[] _left = new float[MixerCore.MaximumCycleFrames];
    private readonly float[] _right = new float[MixerCore.MaximumCycleFrames];
    private PulseConnection? _connection;
    private IMixerRender? _render;
    private TimeProvider? _clock;
    private GCHandle _self;
    private nint _stream;
    private long _cycles;

    /// <summary>Gets the cycles rendered so far.</summary>
    internal long Cycles => Interlocked.Read(ref _cycles);

    /// <inheritdoc />
    public void Start(IMixerRender render, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(clock);
        if (_connection is not null)
        {
            throw new InvalidOperationException("The browser audio output has already started.");
        }

        _render = render;
        _clock = clock;
        _self = GCHandle.Alloc(this);
        _connection = PulseConnection.Open("DENICE browser audio", clock);
        PulseConnection connection = _connection;
        connection.Locked(() =>
        {
            _stream = connection.NewStream("DENICE browser mix", 2);
            PulseNative.StreamSetWriteCallback(_stream, (nint)(delegate* unmanaged<nint, nuint, nint, void>)&WriteRequested, GCHandle.ToIntPtr(_self));
            var attributes = new PulseNative.BufferAttributes { MaximumLength = uint.MaxValue, TargetLength = 960 * 8, Prebuffer = uint.MaxValue, MinimumRequest = 240 * 8, FragmentSize = uint.MaxValue };
            if (PulseNative.StreamConnectPlayback(_stream, 0, &attributes, PulseNative.AdjustLatency, 0, 0) < 0)
            {
                throw new InvalidOperationException("DENICE's mixer stream could not connect to the default sink.");
            }
        });
        int state = -1;
        connection.WaitUntil(
            () =>
            {
                state = PulseNative.StreamGetState(_stream);
                return state is PulseNative.StreamReady or 3;
            },
            clock,
            "the mixer stream");
        if (state != PulseNative.StreamReady)
        {
            throw new InvalidOperationException($"DENICE's mixer stream failed (state {state}, context error {PulseNative.ContextErrno(connection.Context)}).");
        }

        MixerOutputStart.WaitForFirstCycle(() => Cycles, clock);
        AudioProofLog.Note("DENICE mixer output: libpulse playback stream on the default sink, 48 kHz float stereo");
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_connection is not { } connection)
        {
            return;
        }

        connection.Locked(() =>
        {
            _ = PulseNative.StreamDisconnect(_stream);
            PulseNative.StreamUnref(_stream);
        });
        connection.Dispose();
        _connection = null;
        _stream = 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _connection?.Dispose();
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    [UnmanagedCallersOnly]
    private static void WriteRequested(nint stream, nuint bytes, nint userdata)
    {
        try
        {
            if (GCHandle.FromIntPtr(userdata).Target is LinuxPulseOutput output)
            {
                output.Fill(stream, (int)bytes / 8);
            }
        }
        catch (Exception error)
        {
            AudioProofLog.Note("FAILED mixer output write: " + error.Message);
        }
    }

    private void Fill(nint stream, int frames)
    {
        while (frames > 0 && _render is { } render && _clock is { } clock)
        {
            int chunk = Math.Min(frames, MixerCore.MaximumCycleFrames);
            nint data;
            nuint size = (nuint)(chunk * 8);
            if (PulseNative.StreamBeginWrite(stream, &data, &size) < 0 || data == 0)
            {
                return;
            }

            chunk = (int)size / 8;
            long host = (long)(clock.GetTimestamp() * (1_000_000_000.0 / clock.TimestampFrequency));
            render.RenderCycle(_left.AsSpan(0, chunk), _right.AsSpan(0, chunk), host);
            float* samples = (float*)data;
            for (int frame = 0; frame < chunk; frame++)
            {
                samples[2 * frame] = _left[frame];
                samples[(2 * frame) + 1] = _right[frame];
            }

            _ = PulseNative.StreamWrite(stream, data, (nuint)(chunk * 8), 0, 0, 0);
            _ = Interlocked.Increment(ref _cycles);
            frames -= chunk;
        }
    }
}
