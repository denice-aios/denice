// Copyright (c) 4thWAIV. All rights reserved.

using System;
using System.Threading.Tasks;
using Denice.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Denice.Analyzers.Tests;

public class NoStaticServiceHolderAnalyzerTests
{
    // DN0024's service-type set is now DERIVED from ISystemServices (derive-service-set-from-isystemservices), so the
    // fixture's container must actually expose the service through an accessor — an empty ISystemServices would derive
    // an empty set. The container fixture is SharedAnalyzerSources.AbstractionsPrefix. The assertions below are
    // unchanged from the hand-list era.
    [Fact]
    public async Task StaticFieldTypedAsService_IsReported()
    {
        string source = SharedAnalyzerSources.AbstractionsPrefix + """

            namespace App
            {
                public class Holder
                {
                    private static Denice.Abstractions.Contracts.IFileReader Field = null!;
                }
            }
            """;

        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<NoStaticServiceHolderAnalyzer>(source, "Denice.Engine"));
        Assert.Equal("DN0024", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Field", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaticPropertyTypedAsContainer_IsReported()
    {
        string source = SharedAnalyzerSources.AbstractionsPrefix + """

            namespace App
            {
                public class Holder
                {
                    public static Denice.Abstractions.Contracts.ISystemServices Services { get; } = null!;
                }
            }
            """;

        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<NoStaticServiceHolderAnalyzer>(source, "Denice.Engine"));
        Assert.Equal("DN0024", diagnostic.Id);
        Assert.Contains("Services", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaticServiceField_AtCompositionPoint_IsNotReported()
    {
        // The composition point (the Program type in namespace Denice.Cli) is the one place a service type may sit
        // in a static. It is compiled into the CLI's REAL assembly name "denice" (<AssemblyName>denice</AssemblyName>),
        // the name the composition-point exemption matches on — not the root namespace.
        string source = SharedAnalyzerSources.AbstractionsPrefix + """

            namespace Denice.Cli
            {
                internal static class Program
                {
                    private static Denice.Abstractions.Contracts.IFileReader Field = null!;
                }
            }
            """;

        Assert.Empty(await AnalyzerRunner.RunAsync<NoStaticServiceHolderAnalyzer>(source, "denice"));
    }

    [Fact]
    public async Task StaticServiceField_NestedOneLevelInsideCompositionPoint_IsNotReported()
    {
        // A static service field one level nested inside the Program composition point is still at the composition
        // point. The walking Encloses form recognises the enclosing Program (Denice.Cli, assembly "denice"), exactly
        // as the three sibling composition-point rules do; the single-level containing-type check it replaced would
        // have wrongly flagged the field because its immediate containing type is the nested Holder, not Program.
        string source = SharedAnalyzerSources.AbstractionsPrefix + """

            namespace Denice.Cli
            {
                internal static class Program
                {
                    private static class Holder
                    {
                        private static Denice.Abstractions.Contracts.IFileReader Field = null!;
                    }
                }
            }
            """;

        Assert.Empty(await AnalyzerRunner.RunAsync<NoStaticServiceHolderAnalyzer>(source, "denice"));
    }

    [Fact]
    public async Task InstanceServiceField_IsNotReported()
    {
        // Only a static holder is the service-locator smell; an instance field arrives by construction and is fine.
        string source = SharedAnalyzerSources.AbstractionsPrefix + """

            namespace App
            {
                public class Holder
                {
                    private Denice.Abstractions.Contracts.IFileReader Field = null!;
                }
            }
            """;

        Assert.Empty(await AnalyzerRunner.RunAsync<NoStaticServiceHolderAnalyzer>(source, "Denice.Engine"));
    }

    [Fact]
    public async Task StaticFieldOfNonServiceType_IsNotReported()
    {
        const string source = """
            namespace App
            {
                public class Holder
                {
                    private static string Value = "";
                }
            }
            """;

        Assert.Empty(await AnalyzerRunner.RunAsync<NoStaticServiceHolderAnalyzer>(source, "Denice.Engine"));
    }
}
