// Copyright (c) Denice.ai. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Denice.Abstractions;
using Denice.Engine.Setup;
using Denice.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Denice.Tests;

public sealed class GuardHostSurfaceTests
{
    [Fact]
    public async Task Hook_UnknownHost_FailsClosed()
    {
        HostDecision decision = await GuardHost.RunPipelineAsync(
            HookEvent.PreToolUse, "{}", "codex", SystemServicesBuilder.Real().Build(), CancellationToken.None);

        decision.ExitCode.Should().Be(2);
    }

    [Fact]
    public async Task Hook_EmptyPayload_FailsClosed()
    {
        HostDecision decision = await GuardHost.RunPipelineAsync(
            HookEvent.PreToolUse, string.Empty, GuardHost.ClaudeCodeHost, SystemServicesBuilder.Real().Build(), CancellationToken.None);

        decision.ExitCode.Should().Be(2);
    }

    [Fact]
    public async Task Hook_BenignEdit_Allows()
    {
        using var fixture = new FixtureProject();
        string payload = TestPayloads.Edit(fixture.Root, fixture.PathOf("Program.cs"));

        HostDecision decision = await GuardHost.RunPipelineAsync(
            HookEvent.PreToolUse, payload, GuardHost.ClaudeCodeHost, SystemServicesBuilder.Real().Build(), CancellationToken.None);

        decision.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task Hook_ProtectedEdit_Denies()
    {
        using var fixture = new FixtureProject();
        fixture.WriteFile(".claude/settings.json", "{ \"hooks\": {} }");
        string payload = TestPayloads.Edit(fixture.Root, fixture.PathOf(".claude/settings.json"));

        HostDecision decision = await GuardHost.RunPipelineAsync(
            HookEvent.PreToolUse, payload, GuardHost.ClaudeCodeHost, SystemServicesBuilder.Real().Build(), CancellationToken.None);

        decision.ExitCode.Should().Be(2);
    }
}
