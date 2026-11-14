// Copyright (c) Denice.ai. All rights reserved.

namespace Denice.Abstractions;

/// <summary>
/// The input of a shell tool: the command line it would run.
/// </summary>
/// <param name="Command">The shell command line.</param>
public sealed record ShellCommandInput(string Command) : ToolInput;
