// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.Linux.Recording;

/// <summary>Binds public CUDA driver graphics interop; the host supplies its GPU driver.</summary>
internal static partial class CudaNative
{
    /// <summary>Invokes cuInit.</summary>
    /// <param name="flags">The native flags argument.</param>
    /// <returns>The CUDA status code.</returns>
    [LibraryImport("libcuda.so.1", EntryPoint = "cuInit")]
    internal static partial int Initialize(uint flags);

    /// <summary>Invokes cuGLGetDevices.</summary>
    /// <param name="count">The native count argument.</param>
    /// <param name="device">The native device argument.</param>
    /// <param name="capacity">The native capacity argument.</param>
    /// <param name="deviceList">The native deviceList argument.</param>
    /// <returns>The CUDA status code.</returns>
    [LibraryImport("libcuda.so.1", EntryPoint = "cuGLGetDevices")]
    internal static partial int GetGlDevices(out uint count, out int device, uint capacity, uint deviceList);

    /// <summary>Invokes cuGraphicsGLRegisterImage.</summary>
    /// <param name="resource">The native resource argument.</param>
    /// <param name="texture">The native texture argument.</param>
    /// <param name="target">The native target argument.</param>
    /// <param name="flags">The native flags argument.</param>
    /// <returns>The CUDA status code.</returns>
    [LibraryImport("libcuda.so.1", EntryPoint = "cuGraphicsGLRegisterImage")]
    internal static partial int RegisterImage(out nint resource, uint texture, uint target, uint flags);

    /// <summary>Invokes cuGraphicsMapResources.</summary>
    /// <param name="count">The native count argument.</param>
    /// <param name="resource">The native resource argument.</param>
    /// <param name="stream">The native stream argument.</param>
    /// <returns>The CUDA status code.</returns>
    [LibraryImport("libcuda.so.1", EntryPoint = "cuGraphicsMapResources")]
    internal static partial int MapResources(uint count, in nint resource, nint stream);

    /// <summary>Invokes cuGraphicsSubResourceGetMappedArray.</summary>
    /// <param name="array">The native array argument.</param>
    /// <param name="resource">The native resource argument.</param>
    /// <param name="arrayIndex">The native arrayIndex argument.</param>
    /// <param name="mipLevel">The native mipLevel argument.</param>
    /// <returns>The CUDA status code.</returns>
    [LibraryImport("libcuda.so.1", EntryPoint = "cuGraphicsSubResourceGetMappedArray")]
    internal static partial int GetMappedArray(out nint array, nint resource, uint arrayIndex, uint mipLevel);

    /// <summary>Invokes cuMemcpy2DUnaligned_v2.</summary>
    /// <param name="copy">The native copy argument.</param>
    /// <returns>The CUDA status code.</returns>
    [LibraryImport("libcuda.so.1", EntryPoint = "cuMemcpy2DUnaligned_v2")]
    internal static partial int Copy(in Copy2D copy);

    /// <summary>Invokes cuGraphicsUnmapResources.</summary>
    /// <param name="count">The native count argument.</param>
    /// <param name="resource">The native resource argument.</param>
    /// <param name="stream">The native stream argument.</param>
    /// <returns>The CUDA status code.</returns>
    [LibraryImport("libcuda.so.1", EntryPoint = "cuGraphicsUnmapResources")]
    internal static partial int UnmapResources(uint count, in nint resource, nint stream);

    /// <summary>Invokes cuGraphicsUnregisterResource.</summary>
    /// <param name="resource">The native resource argument.</param>
    /// <returns>The CUDA status code.</returns>
    [LibraryImport("libcuda.so.1", EntryPoint = "cuGraphicsUnregisterResource")]
    internal static partial int UnregisterResource(nint resource);

    /// <summary>Invokes cuMemAlloc_v2.</summary>
    /// <param name="pointer">The native pointer argument.</param>
    /// <param name="bytes">The native bytes argument.</param>
    /// <returns>The CUDA status code.</returns>
    [LibraryImport("libcuda.so.1", EntryPoint = "cuMemAlloc_v2")]
    internal static partial int Allocate(out ulong pointer, nuint bytes);

    /// <summary>Invokes cuMemFree_v2.</summary>
    /// <param name="pointer">The native pointer argument.</param>
    /// <returns>The CUDA status code.</returns>
    [LibraryImport("libcuda.so.1", EntryPoint = "cuMemFree_v2")]
    internal static partial int Free(ulong pointer);

    /// <summary>Invokes cuCtxSynchronize.</summary>
    /// <returns>The CUDA status code.</returns>
    [LibraryImport("libcuda.so.1", EntryPoint = "cuCtxSynchronize")]
    internal static partial int Synchronize();

    /// <summary>Matches the public CUDA_MEMCPY2D ABI on the supported 64-bit Linux targets.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Copy2D
    {
        /// <summary>The native SourceX field.</summary>
        internal nuint SourceX;

        /// <summary>The native SourceY field.</summary>
        internal nuint SourceY;

        /// <summary>The native SourceType field.</summary>
        internal int SourceType;

        /// <summary>The native SourceHost field.</summary>
        internal nint SourceHost;

        /// <summary>The native SourceDevice field.</summary>
        internal ulong SourceDevice;

        /// <summary>The native SourceArray field.</summary>
        internal nint SourceArray;

        /// <summary>The native SourcePitch field.</summary>
        internal nuint SourcePitch;

        /// <summary>The native DestinationX field.</summary>
        internal nuint DestinationX;

        /// <summary>The native DestinationY field.</summary>
        internal nuint DestinationY;

        /// <summary>The native DestinationType field.</summary>
        internal int DestinationType;

        /// <summary>The native DestinationHost field.</summary>
        internal nint DestinationHost;

        /// <summary>The native DestinationDevice field.</summary>
        internal ulong DestinationDevice;

        /// <summary>The native DestinationArray field.</summary>
        internal nint DestinationArray;

        /// <summary>The native DestinationPitch field.</summary>
        internal nuint DestinationPitch;

        /// <summary>The native WidthBytes field.</summary>
        internal nuint WidthBytes;

        /// <summary>The native Height field.</summary>
        internal nuint Height;
    }
}
