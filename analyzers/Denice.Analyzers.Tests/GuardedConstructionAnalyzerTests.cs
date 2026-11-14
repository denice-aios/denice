// Copyright (c) 4thWAIV. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading.Tasks;
using Denice.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Denice.Analyzers.Tests;

/// <summary>
/// The consolidated construction rule (<see cref="GuardedConstructionAnalyzer"/>): DN0017 pins the container factory
/// to the composition callers, DN0033 pins the *Info wrapper construction to FileInfoFactory.
/// </summary>
public class GuardedConstructionAnalyzerTests
{
    // ISystemServices lives in Denice.Abstractions.Contracts (its real namespace), matching the return-type pin
    // DN0017 keys on; SystemServices.Create() returns it. CreateDefault() is a differently-named sibling factory that
    // also returns the container — the return-type pin catches it too.
    private const string BoundariesSource = """
        namespace Denice.Abstractions.Contracts
        {
            public interface ISystemServices { }
        }

        namespace Denice.Boundaries
        {
            using Denice.Abstractions.Contracts;

            public static class SystemServices
            {
                public static ISystemServices Create() => null!;

                public static ISystemServices CreateDefault() => null!;
            }
        }
        """;

    private const string CallFromDeepClassSource = """
        using Denice.Boundaries;
        using Denice.Abstractions.Contracts;

        public class Deep
        {
            public ISystemServices Get() => SystemServices.Create();
        }
        """;

    // A differently-named static sibling factory (CreateDefault, not Create) whose return type IS the ISystemServices
    // container, called deep in the chain — DN0017's return-type pin fires even though the name is not Create.
    private const string CallDifferentlyNamedFactoryFromDeepClassSource = """
        using Denice.Boundaries;
        using Denice.Abstractions.Contracts;

        public class Deep
        {
            public ISystemServices Get() => SystemServices.CreateDefault();
        }
        """;

    // A decoy SystemServices in the Denice.Boundaries namespace whose Create returns a DIFFERENT ISystemServices
    // rather than the real container type. The deleted name-only path reported this decoy as a false positive; the
    // return-type pin leaves it alone, because the return type is not the container.
    private const string DecoySystemServicesReturningNonContainerSource = """
        namespace Decoy
        {
            public interface ISystemServices { }
        }

        namespace Denice.Boundaries
        {
            public static class SystemServices
            {
                public static Decoy.ISystemServices Create() => null!;
            }
        }

        namespace App
        {
            public class Deep
            {
                public Decoy.ISystemServices Get() => Denice.Boundaries.SystemServices.Create();
            }
        }
        """;

    private const string CallFromProgramSource = """
        using Denice.Boundaries;
        using Denice.Abstractions.Contracts;

        namespace Denice.Cli
        {
            internal static class Program
            {
                private static ISystemServices Compose() => SystemServices.Create();
            }
        }
        """;

    private const string CallFromBuilderSource = """
        using Denice.Boundaries;
        using Denice.Abstractions.Contracts;

        namespace Denice.TestHelpers
        {
            public sealed class SystemServicesBuilder
            {
                public ISystemServices Build() => SystemServices.Create();
            }
        }
        """;

    private const string CallFromProgramInDifferentNamespaceSource = """
        using Denice.Boundaries;
        using Denice.Abstractions.Contracts;

        namespace Some.Other.Place
        {
            internal static class Program
            {
                private static ISystemServices Compose() => SystemServices.Create();
            }
        }
        """;

    // ---- DN0033 wrapper construction ----

    // The wrapper stub in namespace Denice.CrossPlatform whose static Create factory returns the OWNED
    // Denice.Abstractions.Contracts.IFileInfo — the return type DN0033 pins on (same shape as DN0017's container
    // pin). Compiled into a referenced assembly; the consumer that calls it lives in a separate assembly and is the
    // off-seam construction DN0033 reports.
    private const string WrapperInCrossPlatformSource = """
        namespace Denice.Abstractions.Contracts
        {
            public interface IFileInfo { }
        }

        namespace Denice.CrossPlatform
        {
            using Denice.Abstractions.Contracts;

            public sealed class AbstractedFileInfo
            {
                public static IFileInfo Create(string path) => null!;
            }
        }
        """;

    private const string ConsumerCallingWrapperSource = """
        public class Consumer
        {
            public object Get(string path) => Denice.CrossPlatform.AbstractedFileInfo.Create(path);
        }
        """;

    private const string WrapperCalledFromFileInfoFactorySource = """
        namespace Denice.Abstractions.Contracts
        {
            public interface IFileInfo { }
        }

        namespace Denice.CrossPlatform
        {
            using Denice.Abstractions.Contracts;

            public sealed class AbstractedFileInfo
            {
                public static IFileInfo Create(string path) => null!;
            }

            public sealed class FileInfoFactory
            {
                public object Get(string path) => AbstractedFileInfo.Create(path);
            }
        }
        """;

    // FileInfoFactory is the ONLY exempt site — a sibling class in the Denice.CrossPlatform assembly that is not
    // FileInfoFactory is still RED.
    private const string WrapperCalledFromSiblingInCrossPlatformSource = """
        namespace Denice.Abstractions.Contracts
        {
            public interface IFileInfo { }
        }

        namespace Denice.CrossPlatform
        {
            using Denice.Abstractions.Contracts;

            public sealed class AbstractedFileInfo
            {
                public static IFileInfo Create(string path) => null!;
            }

            public sealed class NotTheFactory
            {
                public object Get(string path) => AbstractedFileInfo.Create(path);
            }
        }
        """;

    // A DIFFERENTLY-NAMED wrapper (not AbstractedFileInfo/AbstractedDirectoryInfo — nothing on any hard-coded name
    // list) whose static factory returns the owned Denice.Abstractions.Contracts.IDirectoryInfo, constructed by a
    // consumer OUTSIDE FileInfoFactory. The return-type pin catches it with no edit to the rule — the Open/Closed
    // generalization: a renamed or additional IFileInfo/IDirectoryInfo wrapper cannot escape the guard.
    private const string DifferentlyNamedWrapperOutsideFactorySource = """
        namespace Denice.Abstractions.Contracts
        {
            public interface IDirectoryInfo { }
        }

        namespace Denice.CrossPlatform
        {
            using Denice.Abstractions.Contracts;

            public sealed class RenamedDirectoryInfoWrapper
            {
                public static IDirectoryInfo Create(string path) => null!;
            }
        }

        namespace App
        {
            public class Deep
            {
                public object Get(string path) => Denice.CrossPlatform.RenamedDirectoryInfoWrapper.Create(path);
            }
        }
        """;

    // The same differently-named wrapper factory, but called INSIDE FileInfoFactory — the one exempt site — so it
    // stays silent even though its return type is the owned IDirectoryInfo.
    private const string DifferentlyNamedWrapperInsideFactorySource = """
        namespace Denice.Abstractions.Contracts
        {
            public interface IDirectoryInfo { }
        }

        namespace Denice.CrossPlatform
        {
            using Denice.Abstractions.Contracts;

            public sealed class RenamedDirectoryInfoWrapper
            {
                public static IDirectoryInfo Create(string path) => null!;
            }

            public sealed class FileInfoFactory
            {
                public object Get(string path) => RenamedDirectoryInfoWrapper.Create(path);
            }
        }
        """;

    // A wrapper whose static factory returns a DECOY interface named IFileInfo in a foreign namespace, not the owned
    // Denice.Abstractions.Contracts.IFileInfo. The return-type pin is anchored on the owned interface's full
    // identity (namespace + name), so a same-named decoy return type is not the guarded abstraction and is not reported.
    private const string WrapperFactoryReturningDecoyInfoSource = """
        namespace Decoy
        {
            public interface IFileInfo { }
        }

        namespace Denice.CrossPlatform
        {
            public sealed class AbstractedFileInfo
            {
                public static Decoy.IFileInfo Create(string path) => null!;
            }
        }

        namespace App
        {
            public class Deep
            {
                public Decoy.IFileInfo Get(string path) => Denice.CrossPlatform.AbstractedFileInfo.Create(path);
            }
        }
        """;

    // An INSTANCE method returning the owned IFileInfo (the real IFileSystem GetFileInfo shape), declared OUTSIDE
    // FileInfoFactory and called by an ordinary consumer. DN0033 pins only STATIC wrapper factories, so an instance
    // method that hands back an IFileInfo is the legal consumer seam and is never flagged. This is the exact
    // guarantee the IsStatic guard in IsStaticFactoryReturningAnyOf exists for.
    private const string InstanceMethodReturningInfoOutsideFactorySource = """
        namespace Denice.Abstractions.Contracts
        {
            public interface IFileInfo { }

            public interface IFileSystem
            {
                IFileInfo GetFileInfo(string path);
            }
        }

        namespace App
        {
            using Denice.Abstractions.Contracts;

            public class Consumer
            {
                public object Get(IFileSystem fileSystem, string path) => fileSystem.GetFileInfo(path);
            }
        }
        """;

    // ---- DN0027 store construction ----

    // The shared overlay InMemoryFileSystemStore (internal to Denice.TestHelpers) constructed in TWO places: inside
    // SystemServicesBuilder (the one legal site) and inside a sibling type (the off-site second construction DN0027
    // reports). Both live in the Denice.TestHelpers namespace, matching the namespace+name pin. Compiled into the
    // Denice.TestHelpers assembly, the gate DN0027 is scoped to.
    private const string StoreConstructedInsideAndOutsideBuilderSource = """
        namespace Denice.TestHelpers
        {
            internal sealed class InMemoryFileSystemStore { }

            public sealed class SystemServicesBuilder
            {
                private static InMemoryFileSystemStore MakeInBuilder() => new InMemoryFileSystemStore();
            }

            public sealed class SomethingElse
            {
                private static InMemoryFileSystemStore MakeOutside() => new InMemoryFileSystemStore();
            }
        }
        """;

    // The store constructed ONLY inside SystemServicesBuilder — the one legal site — so nothing is reported.
    private const string StoreConstructedInsideBuilderOnlySource = """
        namespace Denice.TestHelpers
        {
            internal sealed class InMemoryFileSystemStore { }

            public sealed class SystemServicesBuilder
            {
                private static InMemoryFileSystemStore Make() => new InMemoryFileSystemStore();
            }
        }
        """;

    [Fact]
    public async Task StoreConstruction_OutsideBuilder_InTestHelpers_IsReported()
    {
        // DN0027: a second InMemoryFileSystemStore construction outside SystemServicesBuilder, in the TestHelpers
        // compilation, is a build error; the construction inside the builder is the one legal site and stays silent —
        // so exactly one diagnostic fires, the off-site one.
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<GuardedConstructionAnalyzer>(
                StoreConstructedInsideAndOutsideBuilderSource, "Denice.TestHelpers"));
        Assert.Equal("DN0027", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains(
            "InMemoryFileSystemStore",
            AnalyzerRunner.SpanText(StoreConstructedInsideAndOutsideBuilderSource, diagnostic),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreConstruction_InsideBuilderOnly_IsNotReported()
    {
        // The one legal site: constructing the store inside SystemServicesBuilder is never flagged.
        Assert.Empty(await AnalyzerRunner.RunAsync<GuardedConstructionAnalyzer>(
            StoreConstructedInsideBuilderOnlySource, "Denice.TestHelpers"));
    }

    [Fact]
    public async Task StoreConstruction_OutsideBuilder_InNonTestHelpersAssembly_IsNotReported()
    {
        // DN0027 is gated to the Denice.TestHelpers compilation. The identical off-site construction compiled into a
        // DIFFERENT assembly is not reported — the store is internal to TestHelpers, so this within-TestHelpers rule
        // does not reach across assemblies.
        Assert.Empty(await AnalyzerRunner.RunAsync<GuardedConstructionAnalyzer>(
            StoreConstructedInsideAndOutsideBuilderSource, "Denice.Engine"));
    }

    [Fact]
    public async Task CreateCall_DeepInTheChain_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics =
            await AnalyzerRunner.RunWithReferenceAsync<GuardedConstructionAnalyzer>(
                CallFromDeepClassSource, "Denice.Engine", BoundariesSource, "Denice.Boundaries");

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DN0017", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public async Task CreateCall_FromTheMainTestAssembly_IsReported()
    {
        // After the relocation the main test assembly gains compiler VISIBILITY of the container through the
        // InternalsVisibleTo grant Engine already gives it, so a direct SystemServices.Create() written there now
        // COMPILES where it previously could not. DN0017 is the one wall that remains; this pins that it still fires
        // from that assembly, so tests keep building the container through SystemServicesBuilder.
        const string source = """
            using Denice.Abstractions.Contracts;
            using Denice.Engine;

            public class ContainerTest
            {
                public ISystemServices Build() => SystemServices.Create();
            }
            """;

        Diagnostic diagnostic = Assert.Single(
            await SharedAnalyzerSources.RunAgainstFakeEngineAsync<GuardedConstructionAnalyzer>(
                source, "Denice.Tests"));

        Assert.Equal("DN0017", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public async Task CreateCall_InProgramCompositionMethod_IsNotReported()
    {
        Assert.Empty(await AnalyzerRunner.RunWithReferenceAsync<GuardedConstructionAnalyzer>(
            CallFromProgramSource, "guard", BoundariesSource, "Denice.Boundaries"));
    }

    [Fact]
    public async Task CreateCall_InSystemServicesBuilder_IsNotReported()
    {
        Assert.Empty(await AnalyzerRunner.RunWithReferenceAsync<GuardedConstructionAnalyzer>(
            CallFromBuilderSource, "Denice.TestHelpers", BoundariesSource, "Denice.Boundaries"));
    }

    [Fact]
    public async Task CreateCall_InProgramNamedTypeInNonAllowedAssembly_IsReported()
    {
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunWithReferenceAsync<GuardedConstructionAnalyzer>(
                CallFromProgramSource, "Denice.Engine", BoundariesSource, "Denice.Boundaries"));
        Assert.Equal("DN0017", diagnostic.Id);
    }

    [Fact]
    public async Task CreateCall_InProgramNamespaceButAssemblyNamedAsTheRootNamespace_IsReported()
    {
        // The CLI's real compiled assembly name is "guard", NOT its root namespace "Denice.Cli"; a Program compiled
        // into an assembly named "Denice.Cli" is not the real composition point, so it IS reported.
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunWithReferenceAsync<GuardedConstructionAnalyzer>(
                CallFromProgramSource, "Denice.Cli", BoundariesSource, "Denice.Boundaries"));
        Assert.Equal("DN0017", diagnostic.Id);
    }

    [Fact]
    public async Task CreateCall_InProgramNamedTypeInDifferentNamespaceOfGuardAssembly_IsReported()
    {
        // Nominal collision: a class named Program in a DIFFERENT namespace of the real assembly ("guard") cannot
        // self-grant — the exemption anchors on namespace + name.
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunWithReferenceAsync<GuardedConstructionAnalyzer>(
                CallFromProgramInDifferentNamespaceSource, "guard", BoundariesSource, "Denice.Boundaries"));
        Assert.Equal("DN0017", diagnostic.Id);
    }

    [Fact]
    public async Task DifferentlyNamedFactoryReturningContainer_DeepInTheChain_IsReported()
    {
        // DN0017's return-type pin: a static sibling factory NOT named Create, but whose return type is the
        // ISystemServices container, still fires outside the composition callers — the name-only path is gone; the
        // return type is what pins the wall.
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunWithReferenceAsync<GuardedConstructionAnalyzer>(
                CallDifferentlyNamedFactoryFromDeepClassSource, "Denice.Engine", BoundariesSource, "Denice.Boundaries"));
        Assert.Equal("DN0017", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);

        // The message names the ACTUAL violating call (CreateDefault), not the hardcoded literal Create(): the
        // Open/Closed fix makes DN0017 describe the real member so a developer hitting it on a differently-named
        // sibling factory is pointed at a call that is in their code.
        Assert.Contains(
            "CreateDefault", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateCall_OnSameNamedSystemServicesReturningNonContainer_IsNotReported()
    {
        // The deleted name-only match reported every Denice.Boundaries SystemServices Create regardless of its
        // return type — a decoy-assembly false positive. The return-type pin fires only when the return type IS the
        // container, so a same-named SystemServices whose Create returns something else is not reported.
        Assert.Empty(await AnalyzerRunner.RunAsync<GuardedConstructionAnalyzer>(
            DecoySystemServicesReturningNonContainerSource, "Denice.Engine"));
    }

    [Fact]
    public async Task WrapperConstruction_OutsideFileInfoFactory_IsReported()
    {
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunWithReferenceAsync<GuardedConstructionAnalyzer>(
                ConsumerCallingWrapperSource, "Denice.Engine", WrapperInCrossPlatformSource, "Denice.CrossPlatform"));
        Assert.Equal("DN0033", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains(
            "AbstractedFileInfo", AnalyzerRunner.SpanText(ConsumerCallingWrapperSource, diagnostic), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DifferentlyNamedWrapperFactoryReturningInfo_OutsideFileInfoFactory_IsReported()
    {
        // DN0033's return-type pin, mirroring DN0017's: a differently-named wrapper (not on any hard-coded name list)
        // whose static factory returns the owned IDirectoryInfo still fires outside FileInfoFactory — a renamed or
        // additional IFileInfo/IDirectoryInfo wrapper cannot escape the guard, and the rule needs no edit to cover it
        // (Open/Closed).
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<GuardedConstructionAnalyzer>(
                DifferentlyNamedWrapperOutsideFactorySource, "Denice.Engine"));
        Assert.Equal("DN0033", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public async Task DifferentlyNamedWrapperFactoryReturningInfo_InsideFileInfoFactory_IsNotReported()
    {
        // The same differently-named wrapper factory is silent when called inside FileInfoFactory, the one exempt site.
        Assert.Empty(await AnalyzerRunner.RunAsync<GuardedConstructionAnalyzer>(
            DifferentlyNamedWrapperInsideFactorySource, "Denice.CrossPlatform"));
    }

    [Fact]
    public async Task WrapperFactoryReturningDecoyInfoOfAnotherNamespace_IsNotReported()
    {
        // The deleted namespace-name-assembly pin is gone; the return-type pin fires only when the static factory
        // returns the OWNED Denice.Abstractions.Contracts.IFileInfo/IDirectoryInfo. A same-named decoy interface in
        // a foreign namespace is not the guarded abstraction, so it is not reported (mirrors DN0017's decoy test).
        Assert.Empty(await AnalyzerRunner.RunAsync<GuardedConstructionAnalyzer>(
            WrapperFactoryReturningDecoyInfoSource, "Denice.Engine"));
    }

    [Fact]
    public async Task WrapperConstruction_InsideFileInfoFactory_IsNotReported()
    {
        Assert.Empty(
            await AnalyzerRunner.RunAsync<GuardedConstructionAnalyzer>(
                WrapperCalledFromFileInfoFactorySource, "Denice.CrossPlatform"));
    }

    [Fact]
    public async Task WrapperConstruction_InSiblingOfCrossPlatformAssembly_IsReported()
    {
        // The exempt site is FileInfoFactory specifically, not the whole Denice.CrossPlatform assembly.
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<GuardedConstructionAnalyzer>(
                WrapperCalledFromSiblingInCrossPlatformSource, "Denice.CrossPlatform"));
        Assert.Equal("DN0033", diagnostic.Id);
    }

    [Fact]
    public async Task InstanceMethodReturningInfo_OutsideFileInfoFactory_IsNotReported()
    {
        // The load-bearing exemption the IsStatic guard exists for: IFileSystem.GetFileInfo is an INSTANCE method
        // returning the owned IFileInfo — the legal consumer seam — so calling it outside FileInfoFactory is never
        // flagged, even in a disallowed assembly. Mirrors the *_InsideFileInfoFactory_IsNotReported shape.
        Assert.Empty(await AnalyzerRunner.RunAsync<GuardedConstructionAnalyzer>(
            InstanceMethodReturningInfoOutsideFactorySource, "Denice.Engine"));
    }

    [Fact]
    public async Task WrapperConstruction_InFileInfoFactoryNamedTypeInNonAllowedAssembly_IsReported()
    {
        // Nominal collision: a class literally named FileInfoFactory in the Denice.CrossPlatform NAMESPACE but
        // compiled into a DISALLOWED assembly ("Denice.Engine") cannot self-grant the exemption — the wrapper
        // factory anchors on the compiled assembly too (WellKnownType.IsInAssembly), so the wrapper construction is
        // still reported. Mirrors CreateCall_InProgramNamedTypeInNonAllowedAssembly_IsReported for DN0017.
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<GuardedConstructionAnalyzer>(
                WrapperCalledFromFileInfoFactorySource, "Denice.Engine"));
        Assert.Equal("DN0033", diagnostic.Id);
    }
}
