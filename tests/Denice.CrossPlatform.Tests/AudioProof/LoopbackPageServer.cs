// Copyright (c) Denice.ai. All rights reserved.

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Denice.CrossPlatform.Tests;

/// <summary>
/// Serves the proof page over plain HTTP on 127.0.0.1 and 127.0.0.2 at one port: two loopback sites, both potentially
/// trustworthy origins, so the page has media devices and a navigation between them is cross-site.
/// </summary>
internal sealed class LoopbackPageServer : IDisposable
{
    private readonly TcpListener _first;
    private readonly TcpListener _second;
    private readonly CancellationTokenSource _stop = new();

    private LoopbackPageServer(TcpListener first, TcpListener second)
    {
        _first = first;
        _second = second;
        Port = ((IPEndPoint)first.LocalEndpoint).Port;
    }

    /// <summary>Gets the port both sites use.</summary>
    internal int Port { get; }

    /// <summary>Gets the first site's page.</summary>
    internal Uri First => new($"http://127.0.0.1:{Port}/first");

    /// <summary>Gets the second site's page.</summary>
    internal Uri Second => new($"http://127.0.0.2:{Port}/second");

    /// <inheritdoc />
    public void Dispose()
    {
        _stop.Cancel();
        _first.Stop();
        _second.Stop();
        _first.Dispose();
        _second.Dispose();
        _stop.Dispose();
    }

    /// <summary>Starts serving the page.</summary>
    /// <param name="html">The page.</param>
    /// <returns>The server.</returns>
    internal static LoopbackPageServer Start(string html)
    {
        var first = new TcpListener(IPAddress.Loopback, 0);
        first.Start();
        var second = new TcpListener(IPAddress.Parse("127.0.0.2"), ((IPEndPoint)first.LocalEndpoint).Port);
        second.Start();
        var server = new LoopbackPageServer(first, second);
        byte[] body = Encoding.UTF8.GetBytes(html);
        _ = Task.Run(() => server.ServeAsync(first, body));
        _ = Task.Run(() => server.ServeAsync(second, body));
        return server;
    }

    private static async Task AnswerAsync(TcpClient client, byte[] body)
    {
        using (client)
        {
            NetworkStream stream = client.GetStream();
            var request = new byte[8192];
            int read = await stream.ReadAsync(request).ConfigureAwait(false);
            string line = Encoding.ASCII.GetString(request, 0, read).Split('\n')[0];
            bool page = line.Contains(" /first ", StringComparison.Ordinal) || line.Contains(" /second ", StringComparison.Ordinal);
            byte[] content = page ? body : [];
            string header = $"HTTP/1.1 {(page ? "200 OK" : "404 Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header)).ConfigureAwait(false);
            await stream.WriteAsync(content).ConfigureAwait(false);
        }
    }

    private async Task ServeAsync(TcpListener listener, byte[] body)
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => AnswerAsync(client, body));
        }
    }
}
