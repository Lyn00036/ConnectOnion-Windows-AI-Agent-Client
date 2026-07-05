using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ConnectOnion.Protocol;

/// <summary>
/// Native C# port of <c>remote-agent.ts</c>: a single persistent WebSocket
/// carrying the CONNECT/INPUT/OUTPUT protocol, PING/PONG keepalive, streamed
/// intermediate events, onboarding, and session reconnect.
///
/// Supports both connection modes the SDK does:
/// - Direct URL: connect straight to <c>ws(s)://host/ws</c>.
/// - Relay address: resolve a direct endpoint via the relay; if none is
///   reachable, fall back to the relay's <c>/ws/input</c> route and stamp every
///   message with <c>to: &lt;address&gt;</c> so the relay routes it.
///
/// Threading model mirrors the SDK's promise machine with
/// <see cref="TaskCompletionSource{TResult}"/>: <see cref="EnsureConnectedAsync"/>
/// completes on CONNECTED, <see cref="SendInputAsync"/> completes on OUTPUT. A
/// background receive loop dispatches frames; a watchdog closes the socket after
/// 60s of silence (the only bounded wait, matching the SDK — an input turn has
/// no wall-clock deadline because ask_user legitimately waits on a human).
/// </summary>
public sealed class AgentConnectionService : IAsyncDisposable
{
    private const int ConnectTimeoutMs = 30_000;
    private const int SilenceTimeoutMs = 60_000;

    private readonly string _address;
    private readonly string? _directUrl;
    private readonly string _relayUrl;
    private readonly AgentIdentity _identity;
    private readonly HttpClient _http;

    // Resolved lazily on first connect. isDirect=false means the relay /ws/input
    // path, where every message must carry `to`.
    private string? _wsUrl;
    private bool _isDirect;
    private bool _resolutionAttempted;

    private ClientWebSocket? _ws;
    private Task? _receiveLoop;
    private CancellationTokenSource? _loopCts;
    private bool _authenticated;
    private DateTime _lastActivityUtc;

    private TaskCompletionSource<WireMessage>? _connectTcs;
    private TaskCompletionSource<string>? _inputTcs;
    private CancellationTokenSource? _connectTimeoutCts;

    public string? SessionId { get; private set; }

    /// <summary>
    /// Last fully-processed stream event ID. Sent as <c>last_msg_id</c> on
    /// reconnect so the server can replay only events the client hasn't seen.
    /// </summary>
    public string? LastProcessedEventId { get; private set; }

    /// <summary>The duration (ms) of the most recent execution, from the OUTPUT frame.</summary>
    public double LastExecutionDurationMs { get; private set; }

    public bool IsConnected => _ws is { State: WebSocketState.Open } && _authenticated;

    /// <summary>
    /// Invite code submitted automatically if the agent's trust gate responds
    /// with ONBOARD_REQUIRED. Null means no onboarding is attempted.
    /// </summary>
    public string? InviteCode { get; set; }

    /// <summary>Streamed intermediate events (thinking, tool_call, …) for status UI.</summary>
    public event Action<AgentStreamEvent>? StreamEvent;

    /// <summary>Raised when the connection drops unexpectedly.</summary>
    public event Action<Exception>? ConnectionLost;

    /// <summary>The agent is waiting for a human answer (ask_user).</summary>
    public event Action<AskUserRequest>? AskUserRequested;

    /// <summary>The agent requires onboarding before CONNECT can complete.</summary>
    public event Action? OnboardRequired;

    /// <summary>The agent wants approval to run a tool (approval_needed).</summary>
    public event Action<ApprovalRequest>? ApprovalRequested;

    /// <summary>The agent wants its plan reviewed (plan_review).</summary>
    public event Action<PlanReviewRequest>? PlanReviewRequested;

    /// <summary>
    /// Creates a connection. Provide <paramref name="directUrl"/> for the direct
    /// path, or leave it null to connect by relay address. <paramref name="http"/>
    /// is used for relay endpoint resolution; a shared instance is recommended.
    /// </summary>
    public AgentConnectionService(
        string address,
        string? directUrl,
        AgentIdentity identity,
        HttpClient? http = null,
        string relayUrl = EndpointResolver.DefaultRelay)
    {
        _address = address;
        _directUrl = string.IsNullOrWhiteSpace(directUrl) ? null : directUrl.TrimEnd('/');
        _identity = identity;
        _http = http ?? new HttpClient();
        _relayUrl = EndpointResolver.NormalizeRelayUrl(relayUrl);
    }

    // Port of _resolveWsUrl + _resolveEndpointOnce. Direct URL wins; otherwise
    // try to resolve a direct endpoint through the relay, else use the relay's
    // /ws/input route (isDirect=false).
    private async Task ResolveConnectionAsync(CancellationToken ct)
    {
        if (_wsUrl is not null) return;

        if (_directUrl is not null)
        {
            _wsUrl = DirectWsUrl(_directUrl);
            _isDirect = true;
            return;
        }

        if (!_resolutionAttempted && _address.StartsWith("0x", StringComparison.Ordinal) && _address.Length == 66)
        {
            _resolutionAttempted = true;
            var resolved = await EndpointResolver
                .ResolveEndpointAsync(_http, _address, _relayUrl)
                .ConfigureAwait(false);
            if (resolved is not null)
            {
                _wsUrl = resolved.WsUrl;
                _isDirect = true;
                return;
            }
        }

        // Fall back to the relay input socket; messages carry `to`.
        _wsUrl = $"{_relayUrl}/ws/input";
        _isDirect = false;
    }

    private static string DirectWsUrl(string directUrl)
    {
        if (!Uri.TryCreate(directUrl, UriKind.Absolute, out var uri))
        {
            throw new UriFormatException("Invalid Direct URL.");
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Path = uri.AbsolutePath.TrimEnd('/') + "/ws",
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri.ToString();
    }

    /// <summary>
    /// Sends a prompt and resolves with the final OUTPUT text. Opens and
    /// authenticates the socket first if needed. No wall-clock timeout by design.
    /// </summary>
    public async Task<string> SendInputAsync(string prompt, string? sessionId, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(sessionId, ct).ConfigureAwait(false);

        var inputTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inputTcs = inputTcs;

        var inputMsg = new Dictionary<string, object?>
        {
            ["type"] = "INPUT",
            ["input_id"] = Guid.NewGuid().ToString(),
            ["prompt"] = prompt,
        };
        if (!_isDirect) inputMsg["to"] = _address;
        await SendJsonAsync(inputMsg, ct).ConfigureAwait(false);

        await using (ct.Register(() => inputTcs.TrySetCanceled()))
        {
            return await inputTcs.Task.ConfigureAwait(false);
        }
    }

    /// <summary>Force-reconnects to an existing session. Port of <c>reconnect()</c>.</summary>
    public async Task ReconnectAsync(string sessionId, CancellationToken ct = default)
    {
        await CloseSocketAsync().ConfigureAwait(false);
        SessionId = sessionId;
        await EnsureConnectedAsync(sessionId, ct).ConfigureAwait(false);
    }

    private async Task EnsureConnectedAsync(string? sessionId, CancellationToken ct)
    {
        if (_ws is { State: WebSocketState.Open } && _authenticated) return;

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(ConnectTimeoutMs);
        var connectToken = connectCts.Token;

        await ResolveConnectionAsync(connectToken).ConfigureAwait(false);

        _loopCts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(_wsUrl!), connectToken).ConfigureAwait(false);

        _lastActivityUtc = DateTime.UtcNow;
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_loopCts.Token));
        _ = Task.Run(() => WatchdogAsync(_loopCts.Token));

        var connectTcs = new TaskCompletionSource<WireMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _connectTcs = connectTcs;

        // CONNECT payload is signed: { timestamp, to }.
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var envelope = _identity.SignPayload(new List<KeyValuePair<string, object?>>
        {
            new("timestamp", timestamp),
            new("to", _address),
        });

        var connectMsg = new Dictionary<string, object?>
        {
            ["type"] = "CONNECT",
            ["payload"] = envelope.Payload,
            ["from"] = envelope.From,
            ["signature"] = envelope.Signature,
            ["timestamp"] = envelope.Timestamp,
        };
        // Relay path: the relay needs `to` to route the frame to the agent.
        if (!_isDirect) connectMsg["to"] = _address;
        if (!string.IsNullOrEmpty(sessionId))
        {
            connectMsg["session_id"] = sessionId;
            connectMsg["session"] = new Dictionary<string, object?> { ["session_id"] = sessionId };
            if (!string.IsNullOrEmpty(LastProcessedEventId))
                connectMsg["last_msg_id"] = LastProcessedEventId;
        }

        await SendJsonAsync(connectMsg, connectToken).ConfigureAwait(false);

        // The connect deadline is the only bounded wait; ONBOARD_REQUIRED
        // cancels it (a human/onboarding step now owns the connection).
        _connectTimeoutCts = new CancellationTokenSource(ConnectTimeoutMs);
        await using (_connectTimeoutCts.Token.Register(() =>
                         connectTcs.TrySetException(new TimeoutException("Authentication timed out"))))
        await using (connectToken.Register(() => connectTcs.TrySetCanceled()))
        {
            var connected = await connectTcs.Task.ConfigureAwait(false);
            _authenticated = true;
            SessionId = connected.GetString("session_id") ?? sessionId;
        }
    }

    public Task SubmitOnboardInviteCodeAsync(string inviteCode, CancellationToken ct = default)
    {
        InviteCode = inviteCode;
        return SubmitOnboardAsync(inviteCode, ct);
    }

    // Port of signOnboard(): a signed { timestamp, invite_code } envelope.
    private async Task SubmitOnboardAsync(string inviteCode, CancellationToken ct)
    {
        var payload = new List<KeyValuePair<string, object?>>
        {
            new("timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            new("invite_code", inviteCode),
        };
        var envelope = _identity.SignPayload(payload);
        await SendJsonAsync(new Dictionary<string, object?>
        {
            ["type"] = "ONBOARD_SUBMIT",
            ["payload"] = envelope.Payload,
            ["from"] = envelope.From,
            ["signature"] = envelope.Signature,
            ["timestamp"] = envelope.Timestamp,
        }, ct).ConfigureAwait(false);
    }

    // --- Interactive-turn responses (sent on the live socket; the pending
    // SendInputAsync stays open until the agent finally emits OUTPUT). ---

    /// <summary>Answers an ask_user turn. Port of the ASK_USER_RESPONSE message.</summary>
    public Task RespondAskUserAsync(string answer)
        => SendJsonAsync(new Dictionary<string, object?>
        {
            ["type"] = "ASK_USER_RESPONSE",
            ["answer"] = answer,
        }, CancellationToken.None);

    /// <summary>
    /// Responds to an approval_needed turn. <paramref name="scope"/> is
    /// "once" or "session"; a rejection sends mode "reject_soft".
    /// </summary>
    public Task RespondApprovalAsync(bool approved, string scope = "once")
    {
        var msg = new Dictionary<string, object?>
        {
            ["type"] = "APPROVAL_RESPONSE",
            ["approved"] = approved,
            ["scope"] = scope,
        };
        if (!approved) msg["mode"] = "reject_soft";
        return SendJsonAsync(msg, CancellationToken.None);
    }

    /// <summary>Responds to a plan_review turn. Port of PLAN_REVIEW_RESPONSE.</summary>
    public Task RespondPlanReviewAsync(string message)
        => SendJsonAsync(new Dictionary<string, object?>
        {
            ["type"] = "PLAN_REVIEW_RESPONSE",
            ["message"] = message,
        }, CancellationToken.None);

    private static AskUserRequest ParseAskUser(WireMessage msg)
    {
        var options = new List<string>();
        if (msg.TryGet("options", out var optsEl) && optsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var opt in optsEl.EnumerateArray())
            {
                if (opt.ValueKind == JsonValueKind.String) options.Add(opt.GetString()!);
            }
        }

        var fields = new List<AskUserField>();
        if (msg.TryGet("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fieldsEl.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object) continue;
                fields.Add(new AskUserField(
                    f.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    f.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "",
                    f.TryGetProperty("placeholder", out var p) ? p.GetString() : null,
                    f.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True,
                    f.TryGetProperty("type", out var t) ? t.GetString() : null));
            }
        }

        return new AskUserRequest(
            msg.GetString("id"),
            msg.GetString("text") ?? msg.GetString("question") ?? "",
            options,
            msg.GetBool("multi_select"),
            fields);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var ws = _ws!;
        var buffer = new byte[16 * 1024];
        var frame = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    HandleConnectionLoss(new WebSocketException("Server closed the connection"));
                    return;
                }

                frame.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                var json = frame.ToString();
                frame.Clear();
                _lastActivityUtc = DateTime.UtcNow;
                await HandleMessageAsync(json, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Intentional close via CloseSocketAsync/Dispose.
        }
        catch (Exception ex)
        {
            HandleConnectionLoss(ex);
        }
    }

    // Port of _handleMessage: dispatch on the frame's type discriminator.
    private async Task HandleMessageAsync(string json, CancellationToken ct)
    {
        WireMessage msg;
        try { msg = WireMessage.Parse(json); }
        catch { return; }

        switch (msg.Type)
        {
            case "PING":
                await SendJsonAsync(new Dictionary<string, object?> { ["type"] = "PONG" }, ct).ConfigureAwait(false);
                return;

            case "CONNECTED":
                _connectTcs?.TrySetResult(msg);
                return;

            case "OUTPUT":
            {
                var result = msg.GetString("result") ?? "";
                if (msg.TryGet("duration_ms", out var d) && d.ValueKind == JsonValueKind.Number)
                    LastExecutionDurationMs = d.GetDouble();
                _inputTcs?.TrySetResult(result);
                return;
            }

            case "ERROR":
                var message = msg.GetString("message") ?? msg.GetString("error") ?? "Unknown error";
                var error = new InvalidOperationException($"Agent error: {message}");
                _connectTcs?.TrySetException(error);
                _inputTcs?.TrySetException(error);
                return;

            // Streamed intermediate events → surface for status UI.
            case "thinking":
            case "llm_call":
            case "llm_result":
            case "tool_call":
            case "tool_result":
            case "assistant":
            case "agent_image":
            case "intent":
            case "eval":
            case "compact":
            case "tool_blocked":
            case "files_received":
            {
                var eventId = msg.GetString("id");
                if (!string.IsNullOrEmpty(eventId))
                    LastProcessedEventId = eventId;
                StreamEvent?.Invoke(new AgentStreamEvent(msg.Type, DescribeEvent(msg), eventId, msg.Root.GetRawText()));
                return;
            }

            case "ONBOARD_REQUIRED":
                // A human/onboarding step now owns the connection, so stop the
                // 30s auth deadline. If we hold an invite code, submit it; the
                // follow-up CONNECTED then resolves the pending connect.
                _connectTimeoutCts?.CancelAfter(Timeout.InfiniteTimeSpan);
                StreamEvent?.Invoke(new AgentStreamEvent(msg.Type, DescribeEvent(msg), null, msg.Root.GetRawText()));
                if (!string.IsNullOrEmpty(InviteCode))
                {
                    await SubmitOnboardAsync(InviteCode, ct).ConfigureAwait(false);
                }
                else
                {
                    OnboardRequired?.Invoke();
                }
                return;

            case "ONBOARD_SUCCESS":
                // No client retry: the host finishes the interrupted CONNECT and
                // sends CONNECTED, which resolves the connect promise.
                StreamEvent?.Invoke(new AgentStreamEvent(msg.Type, DescribeEvent(msg), null, msg.Root.GetRawText()));
                return;

            case "ask_user":
                AskUserRequested?.Invoke(ParseAskUser(msg));
                return;

            case "approval_needed":
                ApprovalRequested?.Invoke(new ApprovalRequest(
                    msg.GetString("tool") ?? "",
                    msg.GetString("description"),
                    msg.TryGet("arguments", out var argsEl) ? argsEl.GetRawText() : "{}"));
                return;

            case "plan_review":
                PlanReviewRequested?.Invoke(new PlanReviewRequest(msg.GetString("plan_content") ?? ""));
                return;

            default:
                return;
        }
    }

    private static string DescribeEvent(WireMessage msg) => msg.Type switch
    {
        "tool_call" => $"Running tool: {msg.GetString("name")}",
        "tool_result" => $"Tool finished: {msg.GetString("name")}",
        "thinking" => "Thinking…",
        "ask_user" => msg.GetString("text") ?? msg.GetString("question") ?? "Agent asked a question",
        "ONBOARD_REQUIRED" => "Agent requires onboarding",
        _ => msg.Type,
    };

    private async Task WatchdogAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(10_000, ct).ConfigureAwait(false);
                if ((DateTime.UtcNow - _lastActivityUtc).TotalMilliseconds > SilenceTimeoutMs)
                {
                    HandleConnectionLoss(new TimeoutException("Connection went silent"));
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void HandleConnectionLoss(Exception ex)
    {
        _authenticated = false;
        _connectTcs?.TrySetException(ex);
        _inputTcs?.TrySetException(ex);
        _loopCts?.Cancel();
        ConnectionLost?.Invoke(ex);
    }

    private async Task SendJsonAsync(Dictionary<string, object?> message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct)
            .ConfigureAwait(false);
    }

    private async Task CloseSocketAsync()
    {
        _loopCts?.Cancel();
        _authenticated = false;

        // Drain the receive loop so it cannot race with the socket close
        // below and spuriously call HandleConnectionLoss (which would
        // fault the pending TCS instances and re-cancel the loop CTS).
        if (_receiveLoop is not null)
        {
            try { await _receiveLoop.ConfigureAwait(false); }
            catch { /* expected: loop exits via cancellation or close */ }
            _receiveLoop = null;
        }

        // Clear event subscribers so the old connection can be GC'd even
        // if the subscriber (ChatViewModel) is still alive.
        StreamEvent = null;
        ConnectionLost = null;
        AskUserRequested = null;
        ApprovalRequested = null;
        PlanReviewRequested = null;
        OnboardRequired = null;

        if (_ws is not null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch { /* best-effort close */ }
            _ws.Dispose();
            _ws = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseSocketAsync().ConfigureAwait(false);
        _loopCts?.Dispose();
    }
}
