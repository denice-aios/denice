// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Denice.Abstractions.Contracts;

namespace Denice.TestHelpers;

/// <summary>An application session whose server is a fixed page that becomes ready as soon as the session starts.</summary>
public sealed class FixedApplicationSession : IApplicationSession
{
    private readonly Uri page;
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ApplicationPageState state = new(0, null, null);
    private int started;

    private FixedApplicationSession(Uri page)
    {
        this.page = page;
    }

    /// <inheritdoc />
    public Uri StartupPage { get; } = new("data:text/html,%3C!doctype%20html%3E%3Ctitle%3EDENICE%3C%2Ftitle%3E");

    /// <inheritdoc />
    public ApplicationPageState State => Volatile.Read(ref state);

    /// <inheritdoc />
    public Task Stopped => stopped.Task;

    /// <summary>Creates a session whose application page is the given address.</summary>
    /// <param name="page">The application page.</param>
    /// <returns>The session.</returns>
    public static IApplicationSession Create(Uri page) => new FixedApplicationSession(page ?? throw new ArgumentNullException(nameof(page)));

    /// <inheritdoc />
    public void Start()
    {
        if (Interlocked.Exchange(ref started, 1) == 0 && !stopped.Task.IsCompleted)
        {
            Volatile.Write(ref state, new ApplicationPageState(1, page, null));
        }
    }

    /// <inheritdoc />
    public void RequestStop() => stopped.TrySetResult();
}
