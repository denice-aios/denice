// Copyright (c) Denice.ai. All rights reserved.

using System.Text;
using Denice.Engine;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Denice.Tests;

/// <summary>The real loopback application server attempt and the bundled startup page.</summary>
public sealed class ApplicationServerTests
{
    [Fact]
    public async Task AStartedServerAnswersItsHealthCheckUntilDisposed()
    {
        IRunningApplication server = await new ApplicationServer.Host(new FakeTimeProvider()).StartAsync(CancellationToken.None);
        Uri address = server.Address;
        try
        {
            address.IsLoopback.Should().BeTrue();
            (await server.ProbeAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).Should().BeTrue();
            server.Stopping.IsCancellationRequested.Should().BeFalse();
        }
        finally
        {
            await server.DisposeAsync();
        }

        (await server.ProbeAsync(TimeSpan.FromSeconds(1), CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task AStartCancelledBeforeItBeginsThrows()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Func<Task> start = () => new ApplicationServer.Host(new FakeTimeProvider()).StartAsync(cancelled.Token);
        await start.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void TheStartupPageIsASelfContainedDataUrlThatShowsTheFragmentAsText()
    {
        Uri page = StartupPage.Address;
        page.Scheme.Should().Be("data");
        string payload = page.AbsoluteUri[(page.AbsoluteUri.IndexOf(',', StringComparison.Ordinal) + 1)..];
        string html = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        html.Should().Be(StartupPage.Html);
        html.Should().Contain("hashchange").And.Contain("textContent").And.NotContain("innerHTML").And.NotContain("http");
    }
}
