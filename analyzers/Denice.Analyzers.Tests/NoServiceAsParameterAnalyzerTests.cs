// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Threading.Tasks;
using Denice.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Denice.Analyzers.Tests;

public class NoServiceAsParameterAnalyzerTests
{
    // DN0031's owner-interface set is now DERIVED from ISystemServices (derive-service-set-from-isystemservices), so
    // the fixture's container must expose IFileReader through an accessor for it to count as a service. The container
    // fixture is SharedAnalyzerSources.AbstractionsPrefix. The assertions below are unchanged from the hand-list era.
    [Fact]
    public async Task OrdinaryMethodTakingService_IsReported()
    {
        // Passing a lone service into a method is the service-locator smell; inject it through the constructor instead.
        string source = SharedAnalyzerSources.AbstractionsPrefix + """

            namespace App
            {
                public class Consumer
                {
                    public void Use(Denice.Abstractions.Contracts.IFileReader reader) { }
                }
            }
            """;

        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<NoServiceAsParameterAnalyzer>(source, "Denice.Engine"));
        Assert.Equal("DN0031", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Use", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConstructorTakingService_IsNotReported()
    {
        // Constructor injection is the sanctioned mechanism, so a constructor parameter is never flagged.
        string source = SharedAnalyzerSources.AbstractionsPrefix + """

            namespace App
            {
                public class Consumer
                {
                    private readonly Denice.Abstractions.Contracts.IFileReader reader;

                    public Consumer(Denice.Abstractions.Contracts.IFileReader reader) => this.reader = reader;
                }
            }
            """;

        Assert.Empty(await AnalyzerRunner.RunAsync<NoServiceAsParameterAnalyzer>(source, "Denice.Engine"));
    }

    [Fact]
    public async Task MethodTakingService_AtCompositionPoint_IsNotReported()
    {
        // The composition method (the Program type in namespace Denice.Runtime) legitimately takes a service. It is
        // compiled into the CLI's REAL assembly name "Denice.Runtime" (<AssemblyName>Denice.Runtime</AssemblyName>), the name the
        // composition-point exemption matches on — not the root namespace.
        string source = SharedAnalyzerSources.AbstractionsPrefix + """

            namespace Denice.Runtime
            {
                internal static class Program
                {
                    private static void Compose(Denice.Abstractions.Contracts.IFileReader reader) { }
                }
            }
            """;

        Assert.Empty(await AnalyzerRunner.RunAsync<NoServiceAsParameterAnalyzer>(source, "Denice.Runtime"));
    }

    [Fact]
    public async Task MethodTakingNonService_IsNotReported()
    {
        const string source = """
            namespace App
            {
                public class Consumer
                {
                    public void Use(string value) { }
                }
            }
            """;

        Assert.Empty(await AnalyzerRunner.RunAsync<NoServiceAsParameterAnalyzer>(source, "Denice.Engine"));
    }
}
