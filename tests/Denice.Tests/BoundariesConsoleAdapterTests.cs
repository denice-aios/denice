// Copyright (c) 4thWAIV. All rights reserved.

using System;
using Denice.Abstractions.Contracts;
using Denice.TestHelpers;
using Xunit;

namespace Denice.Tests;

/// <summary>
/// Exercises the real <c>ConsoleAdapter</c> in <c>Denice.Boundaries</c> — where the four adapters stay — through
/// the owned <see cref="IConsole"/> off a real container, now built by <c>SystemServices.Create()</c> in
/// <c>Denice.Engine</c> (<see cref="SystemServicesBuilder.Real"/>). <c>System.Console</c> is owned to this adapter and
/// DN0016 forbids <c>Console.SetOut</c> in a test, so the real console cannot be captured in-process; the observable
/// outcome asserted on the REAL adapter is therefore that each forward to
/// <c>System.Console.{Write,WriteLine,Error.Write,Error.WriteLine}</c> completes without throwing.
/// </summary>
public sealed class BoundariesConsoleAdapterTests
{
    private readonly IConsole console = SystemServicesBuilder.Real().Build().Console;

    [Fact]
    public void WriteAndWriteLine_ForwardToStandardOutputWithoutThrowing()
    {
        Exception? caught = Record.Exception(() =>
        {
            this.console.Write("denice-boundaries-console-test ");
            this.console.WriteLine("stdout line from BoundariesConsoleAdapterTests");
        });

        Assert.Null(caught);
    }

    [Fact]
    public void ErrorWriteAndErrorWriteLine_ForwardToStandardErrorWithoutThrowing()
    {
        Exception? caught = Record.Exception(() =>
        {
            this.console.ErrorWrite("denice-boundaries-console-test ");
            this.console.ErrorWriteLine("stderr line from BoundariesConsoleAdapterTests");
        });

        Assert.Null(caught);
    }
}
