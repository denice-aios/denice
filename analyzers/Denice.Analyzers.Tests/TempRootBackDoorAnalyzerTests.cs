// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Threading.Tasks;
using Denice.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Denice.Analyzers.Tests;

/// <summary>
/// AGS5443 (ags5443-temp-root-back-door-rule): every call site of <c>IEnvironment.GetTempDirectory()</c> — the owned
/// abstraction over <c>Path.GetTempPath()</c> that Sonar's <c>S5443</c> cannot see through — is a build error. It does
/// NOT touch the raw <c>Path.GetTempPath()</c> (owned by DN0020) nor <c>IDirectoryWriter.CreateTempSubdirectory</c> (a
/// uniquely-named atomic subdirectory the caller owns). Zero call sites exist today, so the rule is preventive.
/// </summary>
public class TempRootBackDoorAnalyzerTests
{
    // A caller of the IEnvironment.GetTempDirectory() abstraction — the back door AGS5443 flags.
    private const string CallerOfGetTempDirectorySource = """
        namespace Denice.Abstractions.Contracts
        {
            public interface IEnvironment
            {
                string GetTempDirectory();
            }
        }

        namespace App
        {
            using Denice.Abstractions.Contracts;

            public class Consumer
            {
                public string Get(IEnvironment environment) => environment.GetTempDirectory();
            }
        }
        """;

    // A method-group/delegate capture of IEnvironment.GetTempDirectory — the same back door reached by capturing the
    // member instead of calling it directly. AGS5443 matches by member identity regardless of operation kind (the
    // sibling DN0017 shape), so the capture-then-invoke shape is flagged too and cannot reopen the back door.
    private const string MethodGroupCaptureOfGetTempDirectorySource = """
        namespace Denice.Abstractions.Contracts
        {
            public interface IEnvironment
            {
                string GetTempDirectory();
            }
        }

        namespace App
        {
            using System;
            using Denice.Abstractions.Contracts;

            public class Consumer
            {
                public Func<string> Capture(IEnvironment environment)
                {
                    Func<string> g = environment.GetTempDirectory;
                    return g;
                }
            }
        }
        """;

    // A caller of IDirectoryWriter.CreateTempSubdirectory — creates a uniquely-named atomic subdirectory the caller
    // owns, not the shared root, so it is NOT flagged (Sonar has no rule against it either).
    private const string CallerOfCreateTempSubdirectorySource = """
        namespace Denice.Abstractions.Contracts
        {
            public interface IDirectoryWriter
            {
                string CreateTempSubdirectory(string prefix);
            }
        }

        namespace App
        {
            using Denice.Abstractions.Contracts;

            public class Consumer
            {
                public string Get(IDirectoryWriter writer) => writer.CreateTempSubdirectory("prefix");
            }
        }
        """;

    // The raw Path.GetTempPath() — owned and pinned to the CrossPlatform EnvironmentAdapter by DN0020, out of scope for AGS5443.
    private const string RawGetTempPathSource = """
        namespace App
        {
            using System.IO;

            public class Consumer
            {
                public string Get() => Path.GetTempPath();
            }
        }
        """;

    // A same-named GetTempDirectory() on a type that is NOT Denice.Abstractions.Contracts.IEnvironment — the
    // containing-type identity pin leaves it alone.
    private const string DecoyGetTempDirectoryOnOtherTypeSource = """
        namespace App
        {
            public interface INotEnvironment
            {
                string GetTempDirectory();
            }

            public class Consumer
            {
                public string Get(INotEnvironment thing) => thing.GetTempDirectory();
            }
        }
        """;

    // temp-root-owner-exemption: a GetTempDirectory() call INSIDE the test SystemServicesBuilder in
    // Denice.TestHelpers — the one owner licensed to read the real host temp root to seed the copy-on-write fake.
    // Compiled into the Denice.TestHelpers assembly, this is exempt; elsewhere it stays a build error.
    private const string GetTempDirectoryInsideSystemServicesBuilderSource = """
        namespace Denice.Abstractions.Contracts
        {
            public interface IEnvironment
            {
                string GetTempDirectory();
            }
        }

        namespace Denice.TestHelpers
        {
            using Denice.Abstractions.Contracts;

            public sealed class SystemServicesBuilder
            {
                public string Seed(IEnvironment environment) => environment.GetTempDirectory();
            }
        }
        """;

    // A GetTempDirectory() call inside Denice.TestHelpers but NOT in SystemServicesBuilder — the exemption is for
    // the one owner, not the whole assembly, so this stays a build error.
    private const string GetTempDirectoryElsewhereInTestHelpersSource = """
        namespace Denice.Abstractions.Contracts
        {
            public interface IEnvironment
            {
                string GetTempDirectory();
            }
        }

        namespace Denice.TestHelpers
        {
            using Denice.Abstractions.Contracts;

            public sealed class SomeOtherHelper
            {
                public string Seed(IEnvironment environment) => environment.GetTempDirectory();
            }
        }
        """;

    // A decoy IEnvironment in a FOREIGN namespace whose GetTempDirectory() is not the owned abstraction — the
    // namespace+name pin leaves it alone.
    private const string DecoyEnvironmentInForeignNamespaceSource = """
        namespace Decoy
        {
            public interface IEnvironment
            {
                string GetTempDirectory();
            }
        }

        namespace App
        {
            public class Consumer
            {
                public string Get(Decoy.IEnvironment environment) => environment.GetTempDirectory();
            }
        }
        """;

    [Fact]
    public async Task GetTempDirectoryCall_IsReported()
    {
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<TempRootBackDoorAnalyzer>(CallerOfGetTempDirectorySource));
        Assert.Equal("AGS5443", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains(
            "GetTempDirectory",
            AnalyzerRunner.SpanText(CallerOfGetTempDirectorySource, diagnostic),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MethodGroupCaptureOfGetTempDirectory_IsReported()
    {
        // Capturing the method group (Func<string> g = environment.GetTempDirectory;) reaches the same back door as a
        // direct call; matching by member identity regardless of operation kind flags it too.
        Diagnostic diagnostic = Assert.Single(
            await AnalyzerRunner.RunAsync<TempRootBackDoorAnalyzer>(MethodGroupCaptureOfGetTempDirectorySource));
        Assert.Equal("AGS5443", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains(
            "GetTempDirectory",
            AnalyzerRunner.SpanText(MethodGroupCaptureOfGetTempDirectorySource, diagnostic),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateTempSubdirectoryCall_IsNotReported()
    {
        Assert.Empty(await AnalyzerRunner.RunAsync<TempRootBackDoorAnalyzer>(CallerOfCreateTempSubdirectorySource));
    }

    [Fact]
    public async Task RawGetTempPathCall_IsNotReported()
    {
        // AGS5443 flags the abstraction back door, never the raw Path.GetTempPath — that is DN0020's owned member.
        Assert.Empty(await AnalyzerRunner.RunAsync<TempRootBackDoorAnalyzer>(RawGetTempPathSource));
    }

    [Fact]
    public async Task GetTempDirectoryOnDecoyType_IsNotReported()
    {
        Assert.Empty(await AnalyzerRunner.RunAsync<TempRootBackDoorAnalyzer>(DecoyGetTempDirectoryOnOtherTypeSource));
    }

    [Fact]
    public async Task GetTempDirectoryOnDecoyEnvironmentInForeignNamespace_IsNotReported()
    {
        Assert.Empty(await AnalyzerRunner.RunAsync<TempRootBackDoorAnalyzer>(DecoyEnvironmentInForeignNamespaceSource));
    }

    [Fact]
    public async Task GetTempDirectoryInsideSystemServicesBuilder_IsExempt()
    {
        // temp-root-owner-exemption: SystemServicesBuilder in the Denice.TestHelpers assembly is the one licensed
        // owner, so its seed read of the real temp root is allowed.
        Assert.Empty(await AnalyzerRunner.RunAsync<TempRootBackDoorAnalyzer>(
            GetTempDirectoryInsideSystemServicesBuilderSource, "Denice.TestHelpers"));
    }

    [Fact]
    public async Task GetTempDirectoryElsewhereInTestHelpers_IsStillReported()
    {
        // The exemption is for the one owner, not the whole TestHelpers assembly — a different type there still fires.
        Diagnostic diagnostic = Assert.Single(await AnalyzerRunner.RunAsync<TempRootBackDoorAnalyzer>(
            GetTempDirectoryElsewhereInTestHelpersSource, "Denice.TestHelpers"));
        Assert.Equal("AGS5443", diagnostic.Id);
    }

    [Fact]
    public async Task GetTempDirectoryInSystemServicesBuilderOfAnotherAssembly_IsStillReported()
    {
        // The exemption keys on the Denice.TestHelpers assembly too: a same-named SystemServicesBuilder compiled
        // into a different assembly cannot self-grant it, so the ban still fires.
        Diagnostic diagnostic = Assert.Single(await AnalyzerRunner.RunAsync<TempRootBackDoorAnalyzer>(
            GetTempDirectoryInsideSystemServicesBuilderSource, "Denice.Other"));
        Assert.Equal("AGS5443", diagnostic.Id);
    }
}
