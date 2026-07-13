using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace ConnectOnion.Protocol.Tests.Fakes;

internal sealed class FakeAgentServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _serverTask;

    public Uri BaseUri { get; }
    public Task Completion => _serverTask;

    public FakeAgentServer(Func<WebSocket, CancellationToken, Task> script)
    {
        var port = ReservePort();
        BaseUri = new Uri($"http://127.0.0.1:{port}");
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _serverTask = RunAsync(script, _cts.Token);
    }

    private async Task RunAsync(Func<WebSocket, CancellationToken, Task> script, CancellationToken ct)
    {
        try
        {
            var context = await _listener.GetContextAsync().WaitAsync(ct);
            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }

            var accepted = await context.AcceptWebSocketAsync(subProtocol: null);
            using var socket = accepted.WebSocket;
            await script(socket, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (HttpListenerException) when (ct.IsCancellationRequested)
        {
        }
    }

    public static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var text = new StringBuilder();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("Client closed before the expected frame arrived.");
            text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage) return text.ToString();
        }
    }

    public static Task SendTextAsync(WebSocket socket, string json, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try { await _serverTask; }
        catch (WebSocketException) { }
        _listener.Close();
        _cts.Dispose();
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
