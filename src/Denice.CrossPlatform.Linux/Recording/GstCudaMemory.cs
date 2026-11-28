// Copyright (c) Denice.ai. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Recording;

/// <summary>Transfers an owned device frame to GStreamer's public CUDA memory allocator.</summary>
internal static class GstCudaMemory
{
    private static readonly DestroyNotify ReleaseCallback = Release;
    private static readonly nint ReleasePointer = Marshal.GetFunctionPointerForDelegate(ReleaseCallback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroyNotify(nint userData);

    /// <summary>Wraps device-only RGBA storage without making a CPU pixel copy.</summary>
    /// <param name="frame">The frame, transferred to native ownership only on success.</param>
    /// <returns>The owned GstMemory reference; its final unref releases the frame.</returns>
    internal static nint Create(LinuxCudaContext.DeviceFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        frame.BeginNativeOwnership();
        nint info = 0;
        GCHandle handle = default;
        bool transferred = false;
        try
        {
            info = GstNative.NewVideoInfo();
            if (info == 0)
            {
                throw new InvalidOperationException("GStreamer could not allocate video metadata.");
            }

            int format = GstNative.ParseVideoFormat("RGBA");
            if (format == 0 || GstNative.SetVideoFormat(info, format, (uint)frame.Width, (uint)frame.Height) == 0)
            {
                throw new NotSupportedException("GStreamer could not describe the RGBA device frame.");
            }

            handle = GCHandle.Alloc(frame);
            nint memory = GstNative.WrapCudaMemory(0, frame.Context, 0, info, frame.Pointer, GCHandle.ToIntPtr(handle), ReleasePointer);
            if (memory == 0)
            {
                throw new InvalidOperationException("GStreamer could not wrap the owned CUDA recording frame.");
            }

            transferred = true;
            return memory;
        }
        finally
        {
            if (info != 0)
            {
                GstNative.FreeVideoInfo(info);
            }

            if (!transferred)
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }

                frame.CancelNativeOwnership();
            }
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A native GDestroyNotify callback cannot propagate managed exceptions; the recording owner retains and reports the failure.")]
    private static void Release(nint userData)
    {
        GCHandle handle = GCHandle.FromIntPtr(userData);
        var frame = (LinuxCudaContext.DeviceFrame)handle.Target!;
        try
        {
            frame.ReleaseNativeOwnership();
        }
        catch (Exception error)
        {
            // Exceptions cannot cross GDestroyNotify. The context keeps the
            // frame reachable and reports the error to recording finalization.
            frame.RecordReleaseFailure(error);
        }
        finally
        {
            handle.Free();
        }
    }
}
