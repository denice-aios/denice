// Copyright (c) Denice.ai. All rights reserved.

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Denice.Engine;

/// <summary>Serves DENICE's bundled interface from a loopback-only HTTP endpoint; one supervised attempt.</summary>
internal sealed class ApplicationServer : IRunningApplication
{
    private readonly WebApplication _application;
    private readonly HttpClient _probe;
    private readonly TimeProvider _clock;

    private ApplicationServer(WebApplication application, TimeProvider clock)
    {
        _application = application;
        _clock = clock;
        Address = new Uri(application.Urls.Single(), UriKind.Absolute);
        _probe = new HttpClient { BaseAddress = Address, Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <inheritdoc />
    public Uri Address { get; }

    /// <inheritdoc />
    public CancellationToken Stopping => _application.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

    /// <inheritdoc />
    public async Task<bool> ProbeAsync(TimeSpan timeout, CancellationToken cancellation)
    {
        using var deadline = new CancellationTokenSource(timeout, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellation);
        try
        {
            using HttpResponseMessage response = await _probe.GetAsync(new Uri("/health", UriKind.Relative), linked.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                && string.Equals(await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false), "ready", StringComparison.Ordinal);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            // A disposed attempt is not ready.
            return false;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _probe.Dispose();
        try
        {
            await _application.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await _application.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Starts the application server on an available loopback port.</summary>
    /// <param name="clock">The clock that bounds health probes.</param>
    /// <param name="cancellation">Cancels the start.</param>
    /// <returns>The running server, which the supervisor disposes.</returns>
    internal static async Task<IRunningApplication> StartServerAsync(TimeProvider clock, CancellationToken cancellation)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(ApplicationServer).Assembly.FullName,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        WebApplication application = builder.Build();
        application.MapGet("/", () => Results.Content(ApplicationPage.Html, "text/html; charset=utf-8"));
        application.MapGet("/health", () => Results.Text("ready"));
        try
        {
            await application.StartAsync(cancellation).ConfigureAwait(false);
            return new ApplicationServer(application, clock);
        }
        catch
        {
            await application.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Starts real servers for the supervisor.</summary>
    /// <param name="clock">The clock that bounds health probes.</param>
    internal sealed class Host(TimeProvider clock) : IApplicationHost
    {
        /// <inheritdoc />
        public Task<IRunningApplication> StartAsync(CancellationToken cancellation) => StartServerAsync(clock, cancellation);
    }
}
