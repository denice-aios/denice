// Copyright (c) Denice.ai. All rights reserved.

using Denice.Abstractions.Contracts;

namespace Denice.CrossPlatform;

/// <summary>Creates the Linux native desktop capability.</summary>
internal sealed partial class PlatformServices
{
    private static partial IPlatformDesktop? CreateDesktop() => Linux.Desktop.LinuxDesktop.Create();
}
