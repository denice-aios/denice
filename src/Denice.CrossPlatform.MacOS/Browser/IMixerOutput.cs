// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.CrossPlatform.Browser;

/// <summary>
/// The browser mixer's own device output, opened through DENICE's own OS audio calls, which the browser redirection
/// never touches. The format is fixed: 48 kHz, two planar float channels.
/// </summary>
internal interface IMixerOutput : IDisposable
{
    /// <summary>Starts the device and returns only after its first render cycle; throws if none arrives within three seconds.</summary>
    /// <param name="render">Called on the device's real-time thread for every cycle.</param>
    /// <param name="clock">The injected clock that bounds the start wait.</param>
    void Start(IMixerRender render, TimeProvider clock);

    /// <summary>Stops the device; called only after CEF shutdown has returned.</summary>
    void Stop();
}
