// Copyright (c) Denice.ai. All rights reserved.

using System.Runtime.InteropServices;

namespace Denice.CrossPlatform.MacOS.Recording;

/// <summary>Core Media functions for turning captured PCM into sample buffers.</summary>
internal static partial class AudioNative
{
    private const string CoreMedia = "/System/Library/Frameworks/CoreMedia.framework/CoreMedia";

    /// <summary>Invokes CMAudioFormatDescriptionCreate with the declared native ABI.</summary>
    /// <param name="allocator">The Core Foundation allocator, or zero for default.</param>
    /// <param name="format">The native media format.</param>
    /// <param name="layoutSize">The optional channel layout byte size.</param>
    /// <param name="layout">The optional channel layout.</param>
    /// <param name="cookieSize">The codec cookie byte size.</param>
    /// <param name="cookie">The optional codec cookie.</param>
    /// <param name="extensions">The optional format extensions.</param>
    /// <param name="description">The audio format description.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(CoreMedia)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int CMAudioFormatDescriptionCreate(nint allocator, in AudioStreamFormat format, nuint layoutSize, nint layout, nuint cookieSize, nint cookie, nint extensions, out nint description);

    /// <summary>Invokes CMBlockBufferCreateWithMemoryBlock with the declared native ABI.</summary>
    /// <param name="allocator">The Core Foundation allocator, or zero for default.</param>
    /// <param name="memory">The memory block, or zero to allocate.</param>
    /// <param name="length">The byte length.</param>
    /// <param name="blockAllocator">The allocator for block storage.</param>
    /// <param name="customSource">The optional block ownership callbacks.</param>
    /// <param name="offset">The byte offset.</param>
    /// <param name="dataLength">The data byte length.</param>
    /// <param name="flags">The native operation flags.</param>
    /// <param name="block">The sample storage block.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(CoreMedia)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int CMBlockBufferCreateWithMemoryBlock(nint allocator, nint memory, nuint length, nint blockAllocator, nint customSource, nuint offset, nuint dataLength, uint flags, out nint block);

    /// <summary>Invokes CMBlockBufferReplaceDataBytes with the declared native ABI.</summary>
    /// <param name="source">The source address.</param>
    /// <param name="block">The sample storage block.</param>
    /// <param name="offset">The byte offset.</param>
    /// <param name="length">The byte length.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(CoreMedia)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int CMBlockBufferReplaceDataBytes(nint source, nint block, nuint offset, nuint length);

    /// <summary>Invokes CMSampleBufferCreateReady with the declared native ABI.</summary>
    /// <param name="allocator">The Core Foundation allocator, or zero for default.</param>
    /// <param name="block">The sample storage block.</param>
    /// <param name="description">The audio format description.</param>
    /// <param name="samples">The sample count.</param>
    /// <param name="timingEntries">The timing entry count.</param>
    /// <param name="timing">The sample timing.</param>
    /// <param name="sizeEntries">The size entry count.</param>
    /// <param name="sampleSize">The bytes in each sample.</param>
    /// <param name="sampleBuffer">The resulting sample buffer.</param>
    /// <returns>The native result.</returns>
    [LibraryImport(CoreMedia)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int CMSampleBufferCreateReady(nint allocator, nint block, MediaHandle description, nint samples, nint timingEntries, in AudioSampleTiming timing, nint sizeEntries, in nuint sampleSize, out nint sampleBuffer);
}
