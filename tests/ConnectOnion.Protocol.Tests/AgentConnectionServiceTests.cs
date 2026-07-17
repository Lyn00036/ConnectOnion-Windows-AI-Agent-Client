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
    public async Task SendInterruptAsync_ActiveTurn_SendsExactFrameOnExistingSocket()
    {
        string? interruptJson = null;
        var inputSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new FakeAgentServer(async (socket, ct) =>
        {
            await FakeAgentServer.ReceiveTextAsync(socket, ct); // CONNECT
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"CONNECTED\"}", ct);
            await FakeAgentServer.ReceiveTextAsync(socket, ct); // INPUT
            interruptJson = await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(
                socket, "{\"type\":\"OUTPUT\",\"result\":\"What would you like me to do?\"}", ct);
        });
        await using var service = Service(server);
        service.InputSent += inputSent.SetResult;

        var turn = service.SendInputAsync("start", null);
        await inputSent.Task;
        await service.SendInterruptAsync();

        Assert.Equal("What would you like me to do?", await turn);
        using var interrupt = System.Text.Json.JsonDocument.Parse(interruptJson!);
        var properties = interrupt.RootElement.EnumerateObject().ToList();
        Assert.Single(properties);
        Assert.Equal("type", properties[0].Name);
        Assert.Equal("INTERRUPT", properties[0].Value.GetString());
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
            await Task.Delay(TimeSpan.FromMilliseconds(75), ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"OUTPUT\",\"result\":\"continued\"}", ct);
        });
        await using var service = Service(
            server,
            connectTimeout: TimeSpan.FromSeconds(1),
            silenceTimeout: TimeSpan.FromMilliseconds(250),
            watchdogInterval: TimeSpan.FromMilliseconds(25));
        Exception? connectionLoss = null;
        service.ConnectionLost += error => connectionLoss = error;
        service.AskUserRequested += _ => questionRaised.TrySetResult();
        var send = service.SendInputAsync("start", null);

        await questionRaised.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(750);
        Assert.True(service.IsConnected);
        Assert.Null(connectionLoss);
        await service.RespondAskUserAsync("Yes");

        Assert.Equal("continued", await send);
    }

    [Fact]
    public async Task SendInputAsync_WithMode_SendsModeChangeOnTheFrameAfterInput()
    {
        string? connectJson = null;
        string? inputJson = null;
        string? modeJson = null;
        await using var server = new FakeAgentServer(async (socket, ct) =>
        {
            connectJson = await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"CONNECTED\",\"session_id\":\"s1\"}", ct);
            inputJson = await FakeAgentServer.ReceiveTextAsync(socket, ct);
            modeJson = await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"OUTPUT\",\"result\":\"ok\"}", ct);
        });
        await using var service = Service(server);

        await service.SendInputAsync("hello", "s1", mode: AgentModes.Plan);

        // Order is the whole point: the host only hands a mode_change to a *running* agent, so it
        // has to arrive behind the INPUT that spawns one. Sent first, it would be dropped.
        Assert.Equal("INPUT", WireMessage.Parse(inputJson!).Type);
        var mode = WireMessage.Parse(modeJson!);
        Assert.Equal("mode_change", mode.Type);
        Assert.Equal("plan", mode.GetString("mode"));

        // And it rides on the CONNECT too, which is the only thing that can set the mode of a
        // session the host has never run before (its session merge discards ours after that).
        var connect = WireMessage.Parse(connectJson!);
        Assert.True(connect.TryGet("session", out var session));
        Assert.Equal("plan", session.GetProperty("mode").GetString());
        await server.Completion;
    }

    [Fact]
    public async Task RespondApprovalAsync_Rejected_DefaultsToRejectHardSoTheAgentActuallyStops()
    {
        string? responseJson = null;
        await using var server = new FakeAgentServer(async (socket, ct) =>
        {
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"CONNECTED\"}", ct);
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(
                socket, "{\"type\":\"approval_needed\",\"tool\":\"bash\",\"arguments\":{}}", ct);
            responseJson = await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"OUTPUT\",\"result\":\"stopped\"}", ct);
        });
        await using var service = Service(server);
        service.ApprovalRequested += request => _ = service.RespondApprovalAsync(approved: false, feedback: "no");

        Assert.Equal("stopped", await service.SendInputAsync("start", null));

        // reject_soft only skips the one tool and lets the loop continue; reject_hard is what sets
        // the host's stop_signal and ends the turn. A user who clicked Reject meant the latter.
        var response = WireMessage.Parse(responseJson!);
        Assert.Equal("APPROVAL_RESPONSE", response.Type);
        Assert.False(response.GetBool("approved"));
        Assert.Equal(ApprovalRejectModes.Hard, response.GetString("mode"));
        Assert.Equal("no", response.GetString("feedback"));
        await server.Completion;
    }

    [Fact]
    public async Task RejectApprovalForStopAsync_SendsExactFifoPayloadWithoutTypeOrScope()
    {
        string? responseJson = null;
        await using var server = new FakeAgentServer(async (socket, ct) =>
        {
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"CONNECTED\"}", ct);
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(
                socket, "{\"type\":\"approval_needed\",\"tool\":\"bash\",\"arguments\":{}}", ct);
            responseJson = await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"OUTPUT\",\"result\":\"stopped\"}", ct);
        });
        await using var service = Service(server);
        service.ApprovalRequested += request => _ = service.RejectApprovalForStopAsync();

        Assert.Equal("stopped", await service.SendInputAsync("start", null));

        using var response = System.Text.Json.JsonDocument.Parse(responseJson!);
        var root = response.RootElement;
        Assert.Equal(3, root.EnumerateObject().Count());
        Assert.False(root.GetProperty("approved").GetBoolean());
        Assert.Equal("reject_hard", root.GetProperty("mode").GetString());
        Assert.Equal("用户中断", root.GetProperty("feedback").GetString());
        Assert.False(root.TryGetProperty("type", out _));
        Assert.False(root.TryGetProperty("scope", out _));
        await server.Completion;
    }

    [Fact]
    public async Task ModeChanged_AgentEntersPlanModeItself_RaisesEventAndStreamsIt()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new FakeAgentServer(async (socket, ct) =>
        {
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"CONNECTED\"}", ct);
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(
                socket,
                "{\"type\":\"mode_changed\",\"id\":\"e1\",\"mode\":\"plan\",\"triggered_by\":\"agent\"}",
                ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"OUTPUT\",\"result\":\"done\"}", ct);
            await release.Task.WaitAsync(ct);
        });
        await using var service = Service(server);
        ModeChangedEvent? observed = null;
        var streamed = new List<string>();
        service.ModeChanged += e => observed = e;
        service.StreamEvent += e => streamed.Add(e.Type);

        await service.SendInputAsync("hello", null);

        // The agent can switch itself into plan mode; the client has to follow it rather than keep
        // asserting whatever the user last picked — hence the typed event *and* the stream copy the
        // projection turns into a visible card.
        Assert.Equal("plan", observed?.Mode);
        Assert.Equal("agent", observed?.TriggeredBy);
        Assert.Contains("mode_changed", streamed);
        release.TrySetResult();
        await server.Completion;
    }

    [Fact]
    public async Task QuerySessionStatusAsync_HostReportsRunning_ReturnsRunning()
    {
        await using var server = new FakeAgentServer(async (socket, ct) =>
        {
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"CONNECTED\"}", ct);
            await FakeAgentServer.ReceiveTextAsync(socket, ct);
            await FakeAgentServer.SendTextAsync(socket, "{\"type\":\"OUTPUT\",\"result\":\"done\"}", ct);
            var query = WireMessage.Parse(await FakeAgentServer.ReceiveTextAsync(socket, ct));
            Assert.Equal("SESSION_STATUS", query.Type);
            await FakeAgentServer.SendTextAsync(
                socket, "{\"type\":\"SESSION_STATUS\",\"session_id\":\"s1\",\"status\":\"running\"}", ct);
        });
        await using var service = Service(server);
        await service.SendInputAsync("hello", "s1");

        Assert.Equal(SessionStatuses.Running, await service.QuerySessionStatusAsync("s1"));
        await server.Completion;
    }

    [Fact]
    public async Task QuerySessionStatusAsync_NoConnection_ReportsNotFoundRatherThanThrowing()
    {
        await using var service = new AgentConnectionService(
            "0x1", "http://127.0.0.1:1", AgentIdentity.Generate());

        Assert.Equal(SessionStatuses.NotFound, await service.QuerySessionStatusAsync("s1"));
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
