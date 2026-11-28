// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform.Windows.Desktop;

/// <summary>
/// Ends the process when a borrowed browser GPU frame cannot be completed safely (approved GPU completion policy): a
/// copy still pending past <c>browser.gpuCopyTimeoutSeconds</c>, or an unexplained completion-query failure, on a live
/// device. The Windows door builds it from the owned environment and hands it to the desktop by constructor.
/// </summary>
internal sealed class BrowserGpuCopyTermination
{
    private readonly IEnvironment _environment;

    /// <summary>Initializes a new instance of the <see cref="BrowserGpuCopyTermination"/> class.</summary>
    /// <param name="environment">The owned environment whose <see cref="IEnvironment.FailFast"/> ends the process.</param>
    internal BrowserGpuCopyTermination(IEnvironment environment)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <summary>Ends the process at once with the reason.</summary>
    /// <param name="message">Why the borrowed frame could not be completed safely.</param>
    [DoesNotReturn]
    internal void Terminate(string message) => _environment.FailFast(message);
}
