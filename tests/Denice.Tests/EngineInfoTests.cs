// Copyright (c) Denice.ai. All rights reserved.

using Denice.Engine;
using FluentAssertions;
using Xunit;

namespace Denice.Tests;

public class EngineInfoTests
{
    [Fact]
    public void Name_ReturnsEngineAssemblyName()
    {
        EngineInfo.Name.Should().Be("Denice.Engine");
    }
}
