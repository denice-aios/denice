// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform;

/// <summary>Wires the macOS desktop capability into the platform container.</summary>
internal sealed partial class PlatformServices
{
    private static partial IPlatformDesktop? CreateDesktop() => MacOS.Desktop.MacDesktop.Create();
}
