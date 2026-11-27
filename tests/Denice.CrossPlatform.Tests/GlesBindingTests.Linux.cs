// Copyright (c) Denice.ai. All rights reserved.

using Denice.CrossPlatform.Linux.Desktop;
using Xunit;

namespace Denice.CrossPlatform.Tests;

/// <summary>The EGL condition under which GlesNative resolves its GLES functions through eglGetProcAddress.</summary>
public sealed class GlesBindingTests
{
    [Theory]
    [InlineData("1.5 libglvnd", "", "", true)]
    [InlineData("1.5", "", "", true)]
    [InlineData("1.10 vendor", "", "", true)]
    [InlineData("2.0", "", "", true)]
    [InlineData("1.4 Mesa", "EGL_EXT_client_extensions EGL_KHR_client_get_all_proc_addresses", "", true)]
    [InlineData("1.4", "", "EGL_KHR_image_base EGL_KHR_get_all_proc_addresses", true)]
    [InlineData("1.4 Mesa", "EGL_EXT_client_extensions", "EGL_KHR_image_base", false)]
    [InlineData("1.4", "EGL_KHR_client_get_all_proc_addresses_suffix", "XEGL_KHR_get_all_proc_addresses", false)]
    [InlineData("", "", "", false)]
    [InlineData("libglvnd", "", "", false)]
    [InlineData("1", "", "", false)]
    public void EglMustGuaranteeCoreFunctionsThroughGetProcAddress(string clientVersion, string clientExtensions, string displayExtensions, bool guaranteed)
    {
        Assert.Equal(guaranteed, GlesNative.GuaranteesAllProcAddresses(clientVersion, clientExtensions, displayExtensions));
    }
}
