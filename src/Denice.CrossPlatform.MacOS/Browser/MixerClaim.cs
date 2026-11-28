// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.CrossPlatform.Browser;

/// <summary>
/// One registration of a redirected source in the mixer, with the claim that keeps the render thread and a remover
/// apart: either the render thread sees the removal and skips the source, or the remover sees the running call and
/// waits for it.
/// </summary>
internal sealed class MixerClaim
{
    private int _inCallback;
    private int _removed;

    /// <summary>Initializes a new instance of the <see cref="MixerClaim"/> class.</summary>
    /// <param name="source">The registered source.</param>
    internal MixerClaim(IRedirectedSource source) => Source = source;

    /// <summary>Gets the registered source.</summary>
    internal IRedirectedSource Source { get; }

    /// <summary>Gets a value indicating whether the render thread is inside the source's mix call.</summary>
    internal bool InCallback => Interlocked.CompareExchange(ref _inCallback, 0, 0) == 1;

    /// <summary>Render thread: claims the source for one mix call, unless it has been removed.</summary>
    /// <returns>Whether the call may run; if so, <see cref="Exit"/> must follow.</returns>
    internal bool TryEnter()
    {
        // Full fence before reading Removed; MarkRemoved fences before the remover reads InCallback (Dekker).
        _ = Interlocked.Exchange(ref _inCallback, 1);
        if (Interlocked.CompareExchange(ref _removed, 0, 0) == 1)
        {
            _ = Interlocked.Exchange(ref _inCallback, 0);
            return false;
        }

        return true;
    }

    /// <summary>Render thread: ends the claim taken by <see cref="TryEnter"/>.</summary>
    internal void Exit() => _ = Interlocked.Exchange(ref _inCallback, 0);

    /// <summary>Control thread: stops the render thread from entering this source again.</summary>
    internal void MarkRemoved() => _ = Interlocked.Exchange(ref _removed, 1);
}
