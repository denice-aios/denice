// Copyright (c) Denice.ai. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Denice.Tests.Shared;

/// <summary>
/// The test-only long-running command that proves the long-running host (D6); Denice itself ships no long-running command
/// yet. It runs until the test finishes it with an exit code, or until it is stopped through its token. The source file
/// is linked into each test project that needs it.
/// </summary>
internal sealed class LongRunningTestCommand
{
    private readonly TaskCompletionSource<int> _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets a task that completes once the command runs.</summary>
    internal Task Started => _started.Task;

    /// <summary>Gets a value indicating whether the command is still running.</summary>
    internal bool IsRunning => _started.Task.IsCompleted && !_finish.Task.IsCompleted;

    /// <summary>Gets a value indicating whether the command was stopped through its token.</summary>
    internal bool WasStopped => _finish.Task.IsCanceled;

    /// <summary>Ends the command on its own with <paramref name="exitCode"/>.</summary>
    /// <param name="exitCode">The command's exit code.</param>
    internal void Finish(int exitCode) => _finish.TrySetResult(exitCode);

    /// <summary>Runs until <see cref="Finish"/> is called or <paramref name="ct"/> is cancelled.</summary>
    /// <param name="ct">Stops the command.</param>
    /// <returns>The exit code given to <see cref="Finish"/>.</returns>
    /// <exception cref="TaskCanceledException">The command was stopped through <paramref name="ct"/>.</exception>
    internal async Task<int> RunAsync(CancellationToken ct)
    {
        using (ct.Register(() => _finish.TrySetCanceled(ct)))
        {
            _started.TrySetResult();
            return await _finish.Task.ConfigureAwait(false);
        }
    }
}
