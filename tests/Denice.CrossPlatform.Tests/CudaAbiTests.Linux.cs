// Copyright (c) Denice.ai. All rights reserved.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Denice.CrossPlatform.Linux.Recording;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>Checks the NVIDIA public CUDA_MEMCPY2D ABI used on Linux x64 and ARM64.</summary>
public sealed class CudaAbiTests
{
    [Fact]
    public void CopyDescriptorMatchesPublic64BitDriverLayout()
    {
        CudaNative.Copy2D[] descriptor =
        [
            new()
            {
                SourceX = 1,
                SourceY = 2,
                SourceType = 3,
                SourceHost = 4,
                SourceDevice = 5,
                SourceArray = 6,
                SourcePitch = 7,
                DestinationX = 8,
                DestinationY = 9,
                DestinationType = 10,
                DestinationHost = 11,
                DestinationDevice = 12,
                DestinationArray = 13,
                DestinationPitch = 14,
                WidthBytes = 15,
                Height = 16,
            },
        ];
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(descriptor.AsSpan());
        Assert.Equal(128, bytes.Length);
        int[] offsets = [0, 8, 16, 24, 32, 40, 48, 56, 64, 72, 80, 88, 96, 104, 112, 120];
        for (int index = 0; index < offsets.Length; index++)
        {
            // CUmemorytype is uint32; the following pointer is naturally aligned.
            ulong actual = index is 2 or 9
                ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[offsets[index]..])
                : BinaryPrimitives.ReadUInt64LittleEndian(bytes[offsets[index]..]);
            Assert.Equal((ulong)index + 1, actual);
        }
    }

    [Fact]
    public void GObjectHeadersMatchThePublic64BitAbi()
    {
        GObjectNative.Value[] value = [default];
        Assert.Equal(24, MemoryMarshal.AsBytes(value.AsSpan()).Length);
        GObjectNative.ParamSpecHeader[] header =
        [
            new() { Class = 1, Name = 2, Flags = 3, ValueType = 28, OwnerType = 5 },
        ];
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(header.AsSpan());
        Assert.Equal(40, bytes.Length);
        Assert.Equal(1UL, BinaryPrimitives.ReadUInt64LittleEndian(bytes));
        Assert.Equal(2UL, BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]));
        Assert.Equal(3U, BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]));
        Assert.Equal(28UL, BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]));
        Assert.Equal(5UL, BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]));
    }

    [Fact]
    public void BufferTimestampsMatchThePinnedGStreamer64BitAbi()
    {
        GstPipelineNative.BufferHeader[] buffer =
        [
            new() { PresentationTime = 1, DecodeTime = ulong.MaxValue, Duration = 3 },
        ];
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(buffer.AsSpan());
        Assert.Equal(112, bytes.Length);
        Assert.Equal(1UL, BinaryPrimitives.ReadUInt64LittleEndian(bytes[72..]));
        Assert.Equal(ulong.MaxValue, BinaryPrimitives.ReadUInt64LittleEndian(bytes[80..]));
        Assert.Equal(3UL, BinaryPrimitives.ReadUInt64LittleEndian(bytes[88..]));
    }
}
