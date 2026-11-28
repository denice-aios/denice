// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Browser;

/// <summary>
/// DENICE's own connection to the PulseAudio-compatible server: a threaded mainloop and a ready context, opened through
/// DENICE's own libpulse imports. Every call into the context is made with the mainloop locked.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S6640", Justification = "Audio redirection proof: libpulse callbacks take unmanaged pointers.")]
internal sealed unsafe class PulseConnection : IDisposable
{
    private bool _disposed;

    private PulseConnection(nint mainloop, nint context)
    {
        Mainloop = mainloop;
        Context = context;
    }

    /// <summary>Gets the callback that signals the mainloop given as its userdata (state and success callbacks).</summary>
    internal static nint SignalCallback => (nint)(delegate* unmanaged<nint, nint, void>)&Signal;

    /// <summary>Gets the success callback (context, success, mainloop) that signals the mainloop given as its userdata.</summary>
    internal static nint SuccessCallback => (nint)(delegate* unmanaged<nint, int, nint, void>)&Succeeded;

    /// <summary>Gets the threaded mainloop.</summary>
    internal nint Mainloop { get; }

    /// <summary>Gets the ready context.</summary>
    internal nint Context { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Locked(() =>
        {
            PulseNative.ContextDisconnect(Context);
            PulseNative.ContextUnref(Context);
        });
        PulseNative.MainloopStop(Mainloop);
        PulseNative.MainloopFree(Mainloop);
    }

    /// <summary>Connects to the default server and waits, bounded by the clock, until the context is ready.</summary>
    /// <param name="name">The client name.</param>
    /// <param name="clock">The injected clock.</param>
    /// <returns>The connection.</returns>
    internal static PulseConnection Open(string name, TimeProvider clock)
    {
        nint mainloop = PulseNative.MainloopNew();
        nint context = PulseNative.ContextNew(PulseNative.MainloopGetApi(mainloop), name);
        PulseNative.ContextSetStateCallback(context, SignalCallback, mainloop);
        if (PulseNative.ContextConnect(context, 0, 0, 0) < 0 || PulseNative.MainloopStart(mainloop) < 0)
        {
            PulseNative.ContextUnref(context);
            PulseNative.MainloopFree(mainloop);
            throw new InvalidOperationException("DENICE could not connect to the PulseAudio-compatible server.");
        }

        var connection = new PulseConnection(mainloop, context);
        connection.WaitUntil(() => PulseNative.ContextGetState(context) == PulseNative.ContextReady, clock, "the server connection");
        return connection;
    }

    /// <summary>Creates a 48 kHz float stream whose state changes signal the mainloop; call with the mainloop locked.</summary>
    /// <param name="name">The stream name.</param>
    /// <param name="channels">The channel count.</param>
    /// <returns>The stream.</returns>
    internal nint NewStream(string name, byte channels)
    {
        var spec = new PulseNative.SampleSpec { Format = PulsePlaybackStream.Float32, Rate = Denice.CrossPlatform.Browser.MixerCore.SampleRate, Channels = channels };
        nint stream = PulseNative.StreamNew(Context, name, &spec, 0);
        PulseNative.StreamSetStateCallback(stream, SignalCallback, Mainloop);
        return stream;
    }

    /// <summary>Runs work with the mainloop locked.</summary>
    /// <param name="work">The work.</param>
    internal void Locked(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        PulseNative.MainloopLock(Mainloop);
        try
        {
            work();
        }
        finally
        {
            PulseNative.MainloopUnlock(Mainloop);
        }
    }

    /// <summary>Waits until a condition read under the lock holds, polling every few milliseconds, at most five seconds.</summary>
    /// <param name="done">The condition.</param>
    /// <param name="clock">The injected clock.</param>
    /// <param name="what">What is awaited, for the failure.</param>
    internal void WaitUntil(Func<bool> done, TimeProvider clock, string what)
    {
        ArgumentNullException.ThrowIfNull(done);
        ArgumentNullException.ThrowIfNull(clock);
        long started = clock.GetTimestamp();
        while (true)
        {
            bool finished = false;
            Locked(() => finished = done());
            if (finished)
            {
                return;
            }

            if (clock.GetElapsedTime(started) > TimeSpan.FromSeconds(5))
            {
                throw new InvalidOperationException($"PulseAudio did not complete {what} within five seconds.");
            }

            Task.Delay(TimeSpan.FromMilliseconds(2), clock).GetAwaiter().GetResult();
        }
    }

    /// <summary>Waits for an operation to finish, then releases it.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="clock">The injected clock.</param>
    /// <param name="what">What the operation does, for the failure.</param>
    internal void Await(nint operation, TimeProvider clock, string what)
    {
        if (operation == 0)
        {
            throw new InvalidOperationException($"PulseAudio refused to {what}.");
        }

        WaitUntil(() => PulseNative.OperationGetState(operation) != PulseNative.OperationRunning, clock, what);
        Locked(() => PulseNative.OperationUnref(operation));
    }

    [UnmanagedCallersOnly]
    private static void Succeeded(nint source, int success, nint mainloop) => PulseNative.MainloopSignal(mainloop, 0);

    [UnmanagedCallersOnly]
    private static void Signal(nint source, nint mainloop) => PulseNative.MainloopSignal(mainloop, 0);
}
