// Copyright (c) Denice.ai. All rights reserved.

using System.Collections.Immutable;
using System.Threading.Tasks;
using Denice.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Denice.Analyzers.Tests;

public class TestHelpersOnlyInTestsAnalyzerTests
{
    private const string TestHelpersSource = """
        namespace Denice.TestHelpers
        {
            public sealed class SystemServicesBuilder
            {
                public static SystemServicesBuilder Fake() => new();
            }
        }
        """;

    private const string UseBuilderSource = """
        using Denice.TestHelpers;

        public class Sample
        {
            public object Build() => SystemServicesBuilder.Fake();
        }
        """;

    [Fact]
    public async Task TestHelpersType_UsedFromShippingAssembly_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics =
            await AnalyzerRunner.RunWithReferenceAsync<TestHelpersOnlyInTestsAnalyzer>(
                UseBuilderSource, "Denice.Engine", TestHelpersSource, "Denice.TestHelpers");

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DN0018", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public async Task TestHelpersType_UsedFromTestAssembly_IsNotReported()
    {
        Assert.Empty(await AnalyzerRunner.RunWithReferenceAsync<TestHelpersOnlyInTestsAnalyzer>(
            UseBuilderSource, "Denice.Tests", TestHelpersSource, "Denice.TestHelpers"));
    }

    [Fact]
    public async Task TestHelpersType_UsedFromTestHelpersItself_IsNotReported()
    {
        // The helpers assembly references its own types freely; the rule only stops shipping code reaching them.
        Assert.Empty(await AnalyzerRunner.RunWithReferenceAsync<TestHelpersOnlyInTestsAnalyzer>(
            UseBuilderSource, "Denice.TestHelpers", TestHelpersSource, "Denice.TestHelpers"));
    }
}
