// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using Denice.CrossPlatform.Browser;

namespace Denice.CrossPlatform.MacOS.Browser;

/// <summary>
/// The macOS mixer output: a default-output HAL unit opened through DENICE's own AudioToolbox imports, which do not go
/// through the CEF framework's redirected entries.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio interop: render callbacks exchange stable unmanaged function pointers and buffers with AudioToolbox.")]
internal sealed unsafe class MacMixerOutput : IMixerOutput
{
    private readonly CoreAudioCycle _cycle;
    private IMixerRender? _render;
    private GCHandle _self;
    private nint _unit;
    private long _cycles;

    /// <summary>Initializes a new instance of the <see cref="MacMixerOutput"/> class.</summary>
    /// <param name="cycle">Receives each cycle's AudioTimeStamp for the redirected streams.</param>
    internal MacMixerOutput(CoreAudioCycle cycle) => _cycle = cycle;

    /// <inheritdoc />
    public void Start(IMixerRender render, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(clock);
        if (_unit != 0)
        {
            throw new InvalidOperationException("The browser audio output has already started.");
        }

        _render = render;
        _self = GCHandle.Alloc(this);
        _unit = AudioOutputRedirection.StartOutputUnit((nint)(delegate* unmanaged<nint, uint*, nint, uint, uint, nint, int>)&Render, GCHandle.ToIntPtr(_self), MixerCore.SampleRate);
        MixerOutputStart.WaitForFirstCycle(() => Interlocked.Read(ref _cycles), clock);
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_unit == 0)
        {
            return;
        }

        AudioOutputRedirection.StopOutputUnit(_unit);
        _unit = 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    [UnmanagedCallersOnly]
    private static int Render(nint context, uint* flags, nint timestamp, uint bus, uint frames, nint bufferList)
    {
        if (GCHandle.FromIntPtr(context).Target is MacMixerOutput { _render: { } render } output)
        {
            output._cycle.Render(render, timestamp, (int)frames, bufferList);
            _ = Interlocked.Increment(ref output._cycles);
        }

        return 0;
    }
}
