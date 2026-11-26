// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;
using Denice.CrossPlatform.Linux.Browser;
using Xilium.CefGlue;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>Checks native buffer ABI boundaries without opening a display or loading a graphics driver.</summary>
public sealed class DmaBufAttributesTests
{
    [Fact]
    public void PlaneSizeDoesNotTreatSharedBufferOffsetAsPlaneLength()
    {
        var buffer = new BrowserGpuBuffer([new BrowserGpuPlane(0, 16, 4096, 128)], 0);
        int[] attributes = LinuxDmaBufAttributes.Create(in buffer, (int)CefColorType.Rgba8888, 4, 8);
        Assert.Equal(4096, Value(attributes, 0x3273));
        Assert.Equal(0, Value(attributes, 0x3272));
        Assert.Equal(0x34324241, Value(attributes, 0x3271));
    }

    [Fact]
    public void ModifierAuxiliaryPlanesRetainTheirDescriptorsAndBothModifierWords()
    {
        BrowserGpuPlane[] planes = [new(11, 256, 0, 1024), new(12, 64, 2048, 256), new(13, 64, 4096, 256), new(14, 64, 8192, 256)];
        var buffer = new BrowserGpuBuffer(planes, 0x0100000000000002);
        int[] attributes = LinuxDmaBufAttributes.Create(in buffer, (int)CefColorType.Bgra8888, 1920, 1080);
        Assert.Equal(14, Value(attributes, 0x3440));
        Assert.Equal(8192, Value(attributes, 0x3441));
        Assert.Equal(2, Value(attributes, 0x3449));
        Assert.Equal(0x01000000, Value(attributes, 0x344A));
        Assert.Equal(0x34325241, Value(attributes, 0x3271));
    }

    [Fact]
    public void UnspecifiedModifierDoesNotBecomeLinear()
    {
        var buffer = new BrowserGpuBuffer([new BrowserGpuPlane(4, 64, 0, 256)], 0x00FFFFFFFFFFFFFF);
        int[] attributes = LinuxDmaBufAttributes.Create(in buffer, (int)CefColorType.Rgba8888, 1920, 1080);
        Assert.Equal(-1, Value(attributes, 0x3443));
        Assert.Equal(0x00FFFFFF, Value(attributes, 0x3444));
    }

    [Theory]
    [InlineData(-1, 16U, 0UL, 128UL)]
    [InlineData(1, 0U, 0UL, 128UL)]
    [InlineData(1, uint.MaxValue, 0UL, 128UL)]
    [InlineData(1, 16U, (ulong)int.MaxValue + 1, 128UL)]
    [InlineData(1, 16U, 0UL, 0UL)]
    [InlineData(1, 16U, 0UL, 127UL)]
    public void InvalidLinearPlaneIsRejectedBeforeNativeImport(int descriptor, uint stride, ulong offset, ulong size)
    {
        Assert.Throws<ArgumentException>(() =>
        {
            var buffer = new BrowserGpuBuffer([new BrowserGpuPlane(descriptor, stride, offset, size)], 0);
            _ = LinuxDmaBufAttributes.Create(in buffer, (int)CefColorType.Rgba8888, 4, 8);
        });
    }

    private static int Value(int[] attributes, int key)
    {
        for (int index = 0; index < attributes.Length - 1; index += 2)
        {
            if (attributes[index] == key)
            {
                return attributes[index + 1];
            }
        }

        throw new InvalidOperationException("The expected EGL attribute is absent.");
    }
}
