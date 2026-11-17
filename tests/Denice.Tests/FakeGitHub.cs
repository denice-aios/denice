// Copyright (c) Denice.ai. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Denice.Tests;

/// <summary>
/// Stands in for GitHub in update tests: it answers each URL a test registered and records every request, so no test
/// touches the network. An unregistered URL answers 404.
/// </summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    /// <summary>Gets every request, in order.</summary>
    internal List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Gets or sets what runs just before a request is answered (e.g. to cancel the caller).</summary>
    internal Action<HttpRequestMessage>? BeforeAnswer { get; set; }

    /// <summary>Answers <paramref name="url"/> with <paramref name="answer"/>.</summary>
    /// <param name="url">The absolute URL.</param>
    /// <param name="answer">Builds the response, or throws to simulate a network failure.</param>
    internal void Answer(string url, Func<HttpResponseMessage> answer) => _routes[new Uri(url).AbsoluteUri] = answer;

    /// <summary>Answers <paramref name="url"/> with 200 and <paramref name="body"/>.</summary>
    /// <param name="url">The absolute URL.</param>
    /// <param name="body">The response body.</param>
    internal void Answer(string url, byte[] body) =>
        Answer(url, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });

    /// <summary>Answers <paramref name="url"/> with 200 and <paramref name="body"/> as UTF-8.</summary>
    /// <param name="url">The absolute URL.</param>
    /// <param name="body">The response body.</param>
    internal void Answer(string url, string body) => Answer(url, Encoding.UTF8.GetBytes(body));

    /// <summary>Gets whether <paramref name="url"/> was requested.</summary>
    /// <param name="url">The absolute URL.</param>
    /// <returns>Whether it was requested.</returns>
    internal bool WasRequested(string url) =>
        Requests.Exists(request => string.Equals(request.RequestUri!.AbsoluteUri, new Uri(url).AbsoluteUri, StringComparison.Ordinal));

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        BeforeAnswer?.Invoke(request);
        cancellationToken.ThrowIfCancellationRequested();
        Func<HttpResponseMessage> answer = _routes.GetValueOrDefault(request.RequestUri!.AbsoluteUri, NotFound);
        return Task.FromResult(answer());
    }

    private static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);
}
