// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>Describes one callback-borrowed native pixmap plane without owning its descriptor.</summary>
/// <param name="FileDescriptor">The borrowed DMA-BUF descriptor.</param>
/// <param name="Stride">Bytes between rows.</param>
/// <param name="Offset">The plane byte offset in the buffer.</param>
/// <param name="Size">The plane allocation size in bytes.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct BrowserGpuPlane(int FileDescriptor, uint Stride, ulong Offset, ulong Size);
