// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Xilium.CefGlue;

namespace Denice.CrossPlatform.Linux.Browser;

/// <summary>Encodes callback-borrowed CEF planes using EGL's signed 32-bit attribute ABI.</summary>
internal static class LinuxDmaBufAttributes
{
    /// <summary>Builds the complete attribute list without transferring descriptor ownership.</summary>
    /// <param name="buffer">The callback-scoped native pixmap.</param>
    /// <param name="format">The CEF color format.</param>
    /// <param name="width">The coded width.</param>
    /// <param name="height">The coded height.</param>
    /// <returns>The EGL image import attribute list.</returns>
    internal static int[] Create(scoped in BrowserGpuBuffer buffer, int format, int width, int height)
    {
        if (!buffer.IsNativePixmap || buffer.Planes.Length > 4)
        {
            throw new ArgumentException("A Linux native pixmap with one to four planes is required.", nameof(buffer));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        // The DRM fourcc describes word significance rather than byte order.
        // Both supported architectures use little-endian component ordering.
        int fourcc = (CefColorType)format switch
        {
            CefColorType.Rgba8888 => 0x34324241,
            CefColorType.Bgra8888 => 0x34325241,
            _ => throw new NotSupportedException("The CEF DMA-BUF color format is unsupported."),
        };
        var result = new List<int>(7 + (buffer.Planes.Length * 10)) { 0x3057, width, 0x3056, height, 0x3271, fourcc };
        for (int index = 0; index < buffer.Planes.Length; index++)
        {
            BrowserGpuPlane plane = buffer.Planes[index];
            if (plane.FileDescriptor < 0 || plane.Stride == 0 || plane.Stride > int.MaxValue
                || plane.Offset > int.MaxValue || plane.Size == 0)
            {
                throw new ArgumentException("The native pixmap has an invalid descriptor, stride, offset or allocation size.", nameof(buffer));
            }

            if (buffer.Modifier == 0 && (buffer.Planes.Length != 1
                || plane.Stride < (ulong)width * 4
                || checked(((ulong)plane.Stride * (uint)(height - 1)) + ((ulong)width * 4)) > plane.Size))
            {
                throw new ArgumentException("The linear RGBA native pixmap exceeds its plane allocation.", nameof(buffer));
            }

            // Modifiers may add auxiliary planes to RGB formats. EGL validates the
            // complete format/modifier tuple; no inferred stride is used for tiling.
            int descriptor = index == 3 ? 0x3440 : 0x3272 + (index * 3);
            int modifier = 0x3443 + (index * 2);
            result.AddRange([descriptor, plane.FileDescriptor, descriptor + 1, (int)plane.Offset, descriptor + 2, (int)plane.Stride,
                modifier, unchecked((int)(uint)buffer.Modifier), modifier + 1, unchecked((int)(buffer.Modifier >> 32))]);
        }

        result.Add(0x3038);
        return result.ToArray();
    }
}
