// Copyright (c) 4thWAIV. All rights reserved.

using System.Collections.Immutable;
using System.Threading.Tasks;
using Denice.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Denice.Analyzers.Tests;

public class PlatformFactoryMustReturnContainerAnalyzerTests
{
    [Fact]
    public async Task Factory_ReturningBareFileSystem_IsReported()
    {
        // The container IPlatformServices lives at its real post-move home, Denice.Abstractions.Contracts, while
        // the PlatformServices factory class stays in Denice.CrossPlatform. PlatformServices.Create() — the
        // mandated self-building door (container-is-one-class-with-its-own-create) — returns a bare service
        // (IPlatformFileSystem) instead of the container, so it is reported.
        const string source = """
            namespace Denice.Abstractions.Contracts
            {
                public interface IPlatformServices { }

                public interface IPlatformFileSystem { }
            }

            namespace Denice.CrossPlatform
            {
                public static class PlatformServices
                {
                    public static Denice.Abstractions.Contracts.IPlatformFileSystem Create() => null!;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics =
            await AnalyzerRunner.RunAsync<PlatformFactoryMustReturnContainerAnalyzer>(source, "Denice.CrossPlatform.MacOS");

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("AG0010", diagnostic.Id);
        Assert.Equal("Create", AnalyzerRunner.SpanText(source, diagnostic));
    }

    [Fact]
    public async Task Factory_ReturningConcreteType_IsReported()
    {
        // The self-building PlatformServices.Create() returns the concrete PlatformServices rather than the
        // IPlatformServices container interface, so it is reported.
        const string source = """
            namespace Denice.Abstractions.Contracts
            {
                public interface IPlatformServices { }
            }

            namespace Denice.CrossPlatform
            {
                public sealed class PlatformServices : Denice.Abstractions.Contracts.IPlatformServices
                {
                    public static PlatformServices Create() => new PlatformServices();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics =
            await AnalyzerRunner.RunAsync<PlatformFactoryMustReturnContainerAnalyzer>(source, "Denice.CrossPlatform.MacOS");

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("AG0010", diagnostic.Id);
    }

    [Fact]
    public async Task Factory_ReturningContainerFromOldNamespace_IsReported()
    {
        // The return type's simple name is IPlatformServices, but it is declared in the OLD pre-move namespace
        // Denice.CrossPlatform, so it is NOT the container at its real home Denice.Abstractions.Contracts.
        // A bare name compare would miss this; the namespace-qualified identity check catches it. The factory must
        // fire until IPlatformServices is relocated.
        const string source = """
            namespace Denice.CrossPlatform
            {
                public interface IPlatformServices { }

                public static class PlatformServices
                {
                    public static IPlatformServices Create() => null!;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics =
            await AnalyzerRunner.RunAsync<PlatformFactoryMustReturnContainerAnalyzer>(source, "Denice.CrossPlatform.MacOS");

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("AG0010", diagnostic.Id);
    }

    [Fact]
    public async Task Factory_ReturningContainer_IsNotReported()
    {
        // Clean: PlatformServices.Create() returns the IPlatformServices container declared at its real post-move
        // home, Denice.Abstractions.Contracts, while the PlatformServices factory stays in Denice.CrossPlatform.
        const string source = """
            namespace Denice.Abstractions.Contracts
            {
                public interface IPlatformServices { }
            }

            namespace Denice.CrossPlatform
            {
                public static class PlatformServices
                {
                    public static Denice.Abstractions.Contracts.IPlatformServices Create() => null!;
                }
            }
            """;

        Assert.Empty(await AnalyzerRunner.RunAsync<PlatformFactoryMustReturnContainerAnalyzer>(source, "Denice.CrossPlatform.MacOS"));
    }

    [Fact]
    public async Task CreateOnPlatformServices_InDifferentNamespace_IsNotReported()
    {
        const string source = """
            namespace Other
            {
                public interface IPlatformFileSystem { }

                public static class PlatformServices
                {
                    public static IPlatformFileSystem Create() => null!;
                }
            }
            """;

        Assert.Empty(await AnalyzerRunner.RunAsync<PlatformFactoryMustReturnContainerAnalyzer>(source, "Denice.CrossPlatform.MacOS"));
    }

    [Fact]
    public async Task NonCreateMethodOnPlatformServices_IsNotReported()
    {
        const string source = """
            namespace Denice.CrossPlatform
            {
                public interface IPlatformFileSystem { }

                public static class PlatformServices
                {
                    public static IPlatformFileSystem Build() => null!;
                }
            }
            """;

        Assert.Empty(await AnalyzerRunner.RunAsync<PlatformFactoryMustReturnContainerAnalyzer>(source, "Denice.CrossPlatform.MacOS"));
    }
}
