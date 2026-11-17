// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;

namespace Denice.Runtime;

/// <summary>
/// A command that declares itself long-running: <c>Program</c> sets its action to run <see cref="Run"/> through
/// <c>LongRunningHost</c>, so the session checks for a newer version while it runs and restarts into one that is ready
/// (D6). A one-shot command is an ordinary <see cref="System.CommandLine.Command"/> and finishes once (D5).
/// </summary>
/// <param name="Command">The command, with its name, description, arguments, and options; <c>Program</c> sets its
/// action.</param>
/// <param name="Run">What the command does: it returns its exit code, and must end when its token is cancelled (that
/// is how it is stopped for a newer version, or by Ctrl-C).</param>
internal sealed record LongRunningCommand(Command Command, Func<ParseResult, CancellationToken, Task<int>> Run);
