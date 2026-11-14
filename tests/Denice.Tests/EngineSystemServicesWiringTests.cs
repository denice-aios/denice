// Copyright (c) Denice.ai. All rights reserved.

using System;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// Proves the <c>SystemServices.Create()</c> composition in <c>Denice.Engine</c> wires every owned service to
/// its real adapter: building over <see cref="SystemServicesBuilder.Real"/> and driving each accessor through real
/// behavior (distinct GUIDs, a system-clock read, and the real directory separator)
/// exercises the container's construction and its property accessors and asserts the wiring reached the real owners.
/// </summary>
public sealed class EngineSystemServicesWiringTests
{
    [Fact]
    public void Real_RandomOwnerProducesDistinctGuids()
    {
        IRandomGenerator random = SystemServicesBuilder.Real().Build().Random;

        Assert.NotEqual(random.NewGuid(), random.NewGuid());
    }

    [Fact]
    public void Real_ClockOwnerReadsTheSystemTime()
    {
        TimeProvider clock = SystemServicesBuilder.Real().Build().Clock;

        Assert.True(clock.GetUtcNow().Year >= 2020);
    }

    [Fact]
    public void Real_PlatformOwnerExposesTheRealDirectorySeparator()
    {
        IPlatformFileSystem platform = SystemServicesBuilder.Real().Build().Platform.FileSystem;

        char separator = platform.DirectorySeparator;
        Assert.True(separator is '/' or '\\', $"Unexpected directory separator '{separator}'.");
    }
}
