// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions.Contracts;

/// <summary>A callback-borrowed GPU buffer. Consumers finish native reads before returning and never close its handles.</summary>
public readonly ref struct BrowserGpuBuffer
{
    /// <summary>Initializes a new instance of the <see cref="BrowserGpuBuffer"/> struct for a Windows shared texture or macOS IOSurface.</summary>
    /// <param name="sharedTexture">The nonzero borrowed texture handle.</param>
    public BrowserGpuBuffer(nint sharedTexture)
    {
        if (sharedTexture == 0)
        {
            throw new ArgumentException("A shared texture is required.", nameof(sharedTexture));
        }

        SharedTexture = sharedTexture;
        Planes = default;
        Modifier = 0;
    }

    /// <summary>Initializes a new instance of the <see cref="BrowserGpuBuffer"/> struct for callback-local Linux pixmap planes.</summary>
    /// <param name="planes">One to four borrowed planes.</param>
    /// <param name="modifier">The DRM format modifier.</param>
    public BrowserGpuBuffer(ReadOnlySpan<BrowserGpuPlane> planes, ulong modifier)
    {
        if (planes.IsEmpty || planes.Length > 4)
        {
            throw new ArgumentException("One to four planes are required.", nameof(planes));
        }

        SharedTexture = 0;
        Planes = planes;
        Modifier = modifier;
    }

    /// <summary>Gets the borrowed native texture handle, or zero for a pixmap.</summary>
    public nint SharedTexture { get; }

    /// <summary>Gets the callback-borrowed native pixmap descriptors.</summary>
    public ReadOnlySpan<BrowserGpuPlane> Planes { get; }

    /// <summary>Gets the DRM format modifier for the native pixmap.</summary>
    public ulong Modifier { get; }

    /// <summary>Gets a value indicating whether this buffer describes native pixmap planes.</summary>
    public bool IsNativePixmap => !Planes.IsEmpty;
}
