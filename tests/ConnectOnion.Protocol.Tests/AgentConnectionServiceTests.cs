using System.Net.Sockets;
using System.Net.WebSockets;
using ConnectOnion.Protocol.Tests.Fakes;

namespace ConnectOnion.Protocol.Tests;

public sealed class AgentConnectionServiceTests
{
    [Fact]
    public async Task SendInputAsync_NormalHandshake_ReturnsOutputAndExpectedWireFrames()
    {
        string? connectJson = null;
        string? inputJson = null;
        var releaseServer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new FakeAgentServer(async (socket, ct) =>
        {
            connectJson = await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"CONNECTED\",\"session_id\":\"session-1\"}", ct);
            inputJson = await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"OUTPUT\",\"result\":\"done\",\"duration_ms\":42}", ct);
            await releaseServer.Task.WaitAsync(ct);
        });
        await using var service = Service(server);

        var result = await service.SendInputAsync("hello", null);

        Assert.Equal("done", result);
        Assert.True(service.IsConnected);
        Assert.Equal("session-1", service.SessionId);
        Assert.Equal(42, service.LastExecutionDurationMs);
        Assert.Equal("CONNECT", WireMessage.Parse(connectJson!).Type);
        var input = WireMessage.Parse(inputJson!);
        Assert.Equal("INPUT", input.Type);
        Assert.Equal("hello", input.GetString("prompt"));
        releaseServer.TrySetResult();
        await server.Completion;
    }

    [Fact]
    public async Task SendInputAsync_ServerPing_RespondsWithPongBeforeOutput()
    {
        string? pong = null;
        await using var server = new FakeAgentServer(async (socket, ct) =>
        {
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"CONNECTED\"}", ct);
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"PING\"}", ct);
            pong = await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"OUTPUT\",\"result\":\"pong handled\"}", ct);
        });
        await using var service = Service(server);

        Assert.Equal("pong handled", await service.SendInputAsync("hello", null));
        Assert.Equal("PONG", WireMessage.Parse(pong!).Type);
        await server.Completion;
    }

    [Fact]
    public async Task SendInputAsync_AskUser_RaisesRequestAndSendsAnswer()
    {
        string? responseJson = null;
        await using var server = new FakeAgentServer(async (socket, ct) =>
        {
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"CONNECTED\"}", ct);
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"ask_user\",\"id\":\"question-1\",\"text\":\"Continue?\",\"options\":[\"Yes\",\"No\"]}", ct);
            responseJson = await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"OUTPUT\",\"result\":\"continued\"}", ct);
        });
        await using var service = Service(server);
        service.AskUserRequested += request => _ = service.RespondAskUserAsync("Yes");

        Assert.Equal("continued", await service.SendInputAsync("start", null));
        var response = WireMessage.Parse(responseJson!);
        Assert.Equal("ASK_USER_RESPONSE", response.Type);
        Assert.Equal("Yes", response.GetString("answer"));
        await server.Completion;
    }

    [Fact]
    public async Task SendInputAsync_HandshakeError_ThrowsComprehensibleException()
    {
        await using var server = new FakeAgentServer(async (socket, ct) =>
        {
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"ERROR\",\"message\":\"not authorized\"}", ct);
        });
        await using var service = Service(server);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendInputAsync("hello", null));

        Assert.Contains("not authorized", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.IsConnected);
        await server.Completion;
    }

    [Fact]
    public async Task SendInputAsync_ServerNeverAuthenticates_ThrowsConnectTimeout()
    {
        await using var server = new FakeAgentServer(async (socket, ct) =>
        {
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        });
        await using var service = Service(
            server,
            connectTimeout: TimeSpan.FromMilliseconds(100),
            silenceTimeout: TimeSpan.FromSeconds(5),
            watchdogInterval: TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<Exception>(() => service.SendInputAsync("hello", null));

        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task SendInputAsync_AuthenticatedServerGoesSilent_RaisesWatchdogTimeout()
    {
        await using var server = new FakeAgentServer(async (socket, ct) =>
        {
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"CONNECTED\"}", ct);
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        });
        await using var service = Service(
            server,
            connectTimeout: TimeSpan.FromSeconds(1),
            silenceTimeout: TimeSpan.FromMilliseconds(80),
            watchdogInterval: TimeSpan.FromMilliseconds(20));
        Exception? connectionLoss = null;
        service.ConnectionLost += error => connectionLoss = error;

        var error = await Assert.ThrowsAsync<TimeoutException>(() => service.SendInputAsync("hello", null));

        Assert.Equal("Connection went silent", error.Message);
        Assert.IsType<TimeoutException>(connectionLoss);
        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task SendInputAsync_AskUserWaitsPastSilenceTimeout_WatchdogStaysConnected()
    {
        var questionRaised = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new FakeAgentServer(async (socket, ct) =>
        {
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"CONNECTED\"}", ct);
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"ask_user\",\"id\":\"question-1\",\"text\":\"Continue?\"}", ct);
            var response = WireMessage.Parse(await FakeAgentServer.ReceiveTextAsync(socket, ct));
            Assert.Equal("ASK_USER_RESPONSE", response.Type);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"OUTPUT\",\"result\":\"continued\"}", ct);
        });
        await using var service = Service(
            server,
            connectTimeout: TimeSpan.FromSeconds(1),
            silenceTimeout: TimeSpan.FromMilliseconds(60),
            watchdogInterval: TimeSpan.FromMilliseconds(15));
        Exception? connectionLoss = null;
        service.ConnectionLost += error => connectionLoss = error;
        service.AskUserRequested += _ => questionRaised.TrySetResult();
        var send = service.SendInputAsync("start", null);

        await questionRaised.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(180);
        Assert.True(service.IsConnected);
        Assert.Null(connectionLoss);
        await service.RespondAskUserAsync("Yes");

        Assert.Equal("continued", await send);
    }

    [Fact]
    public async Task DisposeAsync_ConnectedService_DoesNotRaiseConnectionLost()
    {
        await using var server = new FakeAgentServer(async (socket, ct) =>
        {
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"CONNECTED\"}", ct);
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"OUTPUT\",\"result\":\"done\"}", ct);
            try { await FakeAgentServer.ReceiveTextAsync(socket, ct); }
            catch (WebSocketException) { }
        });
        var service = Service(server);
        Exception? connectionLoss = null;
        service.ConnectionLost += error => connectionLoss = error;
        await service.SendInputAsync("hello", null);

        await service.DisposeAsync();

        Assert.Null(connectionLoss);
        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task DisposeAsync_CalledRepeatedly_DoesNotThrow()
    {
        var service = new AgentConnectionService("0x1", "http://127.0.0.1:1", AgentIdentity.Generate());

        await service.DisposeAsync();
        await service.DisposeAsync();
        await service.DisposeAsync();
    }

    [Fact]
    public async Task SendInputAsync_UnreachableEndpoint_FailsWithoutConnectionLostEvent()
    {
        var port = ReserveClosedPort();
        await using var service = new AgentConnectionService(
            "0x1", $"http://127.0.0.1:{port}", AgentIdentity.Generate());
        var raised = false;
        service.ConnectionLost += _ => raised = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<Exception>(() => service.SendInputAsync("hello", null, ct: timeout.Token));

        Assert.False(service.IsConnected);
        Assert.False(raised);
    }

    private static AgentConnectionService Service(
        FakeAgentServer server,
        TimeSpan? connectTimeout = null,
        TimeSpan? silenceTimeout = null,
        TimeSpan? watchdogInterval = null) =>
        new(
            "0x1",
            server.BaseUri.ToString(),
            AgentIdentity.Generate(),
            connectTimeout: connectTimeout,
            silenceTimeout: silenceTimeout,
            watchdogInterval: watchdogInterval);

    private static int ReserveClosedPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
