// Copyright (c) Denice.ai. All rights reserved.

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// A minimal DevTools protocol client for the proof driver: it attaches to the browser's one page target through the
/// remote-debugging port and evaluates case functions with a user gesture, returning their JSON results.
/// </summary>
internal sealed class DevToolsSession : IDisposable
{
    private readonly ClientWebSocket _socket;
    private readonly TimeProvider _clock;
    private int _next;

    private DevToolsSession(ClientWebSocket socket, TimeProvider clock)
    {
        _socket = socket;
        _clock = clock;
    }

    /// <inheritdoc />
    public void Dispose() => _socket.Dispose();

    /// <summary>Attaches to the page target, retrying until the port answers, at most 30 seconds.</summary>
    /// <param name="port">The remote-debugging port.</param>
    /// <param name="clock">The injected clock.</param>
    /// <returns>The session.</returns>
    internal static async Task<DevToolsSession> AttachAsync(int port, TimeProvider clock)
    {
        using var http = new HttpClient();
        long started = clock.GetTimestamp();
        while (true)
        {
            try
            {
                string list = await http.GetStringAsync(new Uri($"http://127.0.0.1:{port}/json/list")).ConfigureAwait(false);
                string? address = JsonNode.Parse(list)?.AsArray()
                    .FirstOrDefault(target => string.Equals((string?)target?["type"], "page", StringComparison.Ordinal))?["webSocketDebuggerUrl"]?.GetValue<string>();
                if (address is not null)
                {
                    var socket = new ClientWebSocket();
                    await socket.ConnectAsync(new Uri(address), CancellationToken.None).ConfigureAwait(false);
                    return new DevToolsSession(socket, clock);
                }
            }
            catch (HttpRequestException) when (clock.GetElapsedTime(started) < TimeSpan.FromSeconds(30))
            {
                // The port opens once CEF has started; retried below.
            }

            if (clock.GetElapsedTime(started) > TimeSpan.FromSeconds(30))
            {
                throw new InvalidOperationException("The DevTools page target did not appear within 30 seconds.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), clock).ConfigureAwait(false);
        }
    }

    /// <summary>Evaluates an expression with a user gesture and awaits its promise.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="timeout">The bound.</param>
    /// <returns>The returned value as JSON.</returns>
    internal async Task<string> EvaluateAsync(string expression, TimeSpan timeout)
    {
        var parameters = new JsonObject
        {
            ["expression"] = expression,
            ["awaitPromise"] = true,
            ["returnByValue"] = true,
            ["userGesture"] = true,
        };
        JsonNode reply = await SendAsync("Runtime.evaluate", parameters, timeout).ConfigureAwait(false);
        if (reply["result"]?["exceptionDetails"] is { } failure)
        {
            return new JsonObject { ["exception"] = failure["exception"]?["description"]?.GetValue<string>() ?? failure.ToJsonString() }.ToJsonString();
        }

        return reply["result"]?["result"]?["value"]?.ToJsonString() ?? "null";
    }

    /// <summary>Navigates the page and waits for its load event.</summary>
    /// <param name="url">The address.</param>
    /// <param name="timeout">The bound.</param>
    /// <returns>A task that completes when the new document has loaded.</returns>
    internal async Task NavigateAsync(Uri url, TimeSpan timeout)
    {
        _ = await SendAsync("Page.enable", [], timeout).ConfigureAwait(false);
        _ = await SendAsync("Page.navigate", new JsonObject { ["url"] = url.AbsoluteUri }, timeout).ConfigureAwait(false);
        using var limit = new CancellationTokenSource(timeout, _clock);
        while (true)
        {
            JsonNode message = await ReceiveAsync(limit.Token).ConfigureAwait(false);
            if (string.Equals((string?)message["method"], "Page.loadEventFired", StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    private async Task<JsonNode> SendAsync(string method, JsonObject parameters, TimeSpan timeout)
    {
        int id = ++_next;
        string request = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString();
        using var limit = new CancellationTokenSource(timeout, _clock);
        await _socket.SendAsync(Encoding.UTF8.GetBytes(request), WebSocketMessageType.Text, true, limit.Token).ConfigureAwait(false);
        while (true)
        {
            JsonNode message = await ReceiveAsync(limit.Token).ConfigureAwait(false);
            if (message["id"] is { } reply && reply.GetValue<int>() == id)
            {
                return message;
            }
        }
    }

    private async Task<JsonNode> ReceiveAsync(CancellationToken cancel)
    {
        var buffer = new byte[65536];
        using var message = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult part = await _socket.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
            await message.WriteAsync(buffer.AsMemory(0, part.Count), cancel).ConfigureAwait(false);
            if (part.EndOfMessage)
            {
                return JsonNode.Parse(message.ToArray()) ?? new JsonObject();
            }
        }
    }
}
