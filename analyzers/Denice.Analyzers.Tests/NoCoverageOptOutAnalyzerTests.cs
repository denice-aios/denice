// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Threading.Tasks;
using Denice.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Denice.Analyzers.Tests;

public class NoCoverageOptOutAnalyzerTests
{
    private const string ExcludeOnTypeSource = """
        using System.Diagnostics.CodeAnalysis;

        namespace App
        {
            [ExcludeFromCodeCoverage]
            public class Untested { }
        }
        """;

    [Fact]
    public async Task ExcludeFromCodeCoverage_OnType_InProductAssembly_IsReported()
    {
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<NoCoverageOptOutAnalyzer>(ExcludeOnTypeSource, "Denice.Engine"));
        Assert.Equal("DN0032", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Untested", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExcludeFromCodeCoverage_OnMethod_InProductAssembly_IsReported()
    {
        const string source = """
            using System.Diagnostics.CodeAnalysis;

            namespace App
            {
                public class Sample
                {
                    [ExcludeFromCodeCoverage]
                    public void Work() { }
                }
            }
            """;

        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<NoCoverageOptOutAnalyzer>(source, "Denice.Runtime"));
        Assert.Equal("DN0032", diagnostic.Id);
        Assert.Contains("Work", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExcludeFromCodeCoverage_InTestAssembly_IsNotReported()
    {
        // Test projects are out of the coverage scope, so they may exclude freely.
        Assert.Empty(await AnalyzerRunner.RunAsync<NoCoverageOptOutAnalyzer>(ExcludeOnTypeSource, "Denice.Runtime.Tests"));
    }

    [Fact]
    public async Task ExcludeFromCodeCoverage_InAbstractionsAssembly_IsNotReported()
    {
        // Denice.Abstractions (interfaces only) is out of the coverage scope.
        Assert.Empty(await AnalyzerRunner.RunAsync<NoCoverageOptOutAnalyzer>(ExcludeOnTypeSource, "Denice.Abstractions"));
    }

    [Fact]
    public async Task NoExcludeAttribute_IsNotReported()
    {
        const string source = """
            namespace App
            {
                public class Sample
                {
                    public void Work() { }
                }
            }
            """;

        Assert.Empty(await AnalyzerRunner.RunAsync<NoCoverageOptOutAnalyzer>(source, "Denice.Engine"));
    }
}
