// Copyright (c) Denice.ai. All rights reserved.

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Denice.Engine;

/// <summary>Serves DENICE's bundled interface from a loopback-only HTTP endpoint.</summary>
public sealed class ApplicationServer : IDisposable
{
    private readonly WebApplication _application;

    private ApplicationServer(WebApplication application)
    {
        _application = application;
    }

    /// <summary>Gets the address assigned by the operating system after startup.</summary>
    public Uri Address => new(_application.Urls.Single(), UriKind.Absolute);

    /// <summary>Starts the application server on an available loopback port.</summary>
    /// <returns>The running server, which the host disposes after the desktop exits.</returns>
    public static ApplicationServer Start()
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
            application.StartAsync().GetAwaiter().GetResult();
            return new ApplicationServer(application);
        }
        catch
        {
            application.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _application.StopAsync().GetAwaiter().GetResult();
        _application.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
