// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace Denice.CrossPlatform.Windows.Recording;

/// <summary>Balances COM initialization on the recording owner thread.</summary>
internal static partial class MfNative
{
    /// <summary>Begins a balanced COM and Media Foundation lifetime on the calling thread.</summary>
    /// <returns>The thread-affine lifetime owner.</returns>
    internal static Session OpenSession()
    {
        // S_OK and S_FALSE both acquire a reference; RPC_E_CHANGED_MODE fails
        // before either runtime is acquired and is never silently accepted.
        new Result(Initialize(0, 0)).CheckError();
        try
        {
            MediaFactory.MFStartup().CheckError();
            return new Session();
        }
        catch
        {
            Uninitialize();
            throw;
        }
    }

    /// <summary>Initializes the calling thread's COM apartment.</summary>
    /// <param name="reserved">The required null reserved pointer.</param>
    /// <param name="flags">The COM apartment flags.</param>
    /// <returns>The initialization HRESULT.</returns>
    [LibraryImport("ole32", EntryPoint = "CoInitializeEx")]
    private static partial int Initialize(nint reserved, uint flags);

    /// <summary>Releases one successful COM initialization on the calling thread.</summary>
    [LibraryImport("ole32", EntryPoint = "CoUninitialize")]
    private static partial void Uninitialize();

    /// <summary>Releases Media Foundation and the matching COM apartment reference.</summary>
    internal sealed class Session : IDisposable
    {
        private readonly Thread _thread = Thread.CurrentThread;
        private bool _disposed;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            RequireThread();
            _disposed = true;
            try
            {
                MediaFactory.MFShutdown().CheckError();
            }
            finally
            {
                Uninitialize();
            }
        }

        private void RequireThread()
        {
            if (_thread != Thread.CurrentThread)
            {
                throw new InvalidOperationException("Media Foundation must close on the thread that initialized COM.");
            }
        }
    }
}
