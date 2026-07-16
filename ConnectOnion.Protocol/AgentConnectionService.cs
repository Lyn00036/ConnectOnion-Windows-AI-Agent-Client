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
    private static readonly TimeSpan SocketCloseTimeout = TimeSpan.FromSeconds(1.5);
    private const int ConnectTimeoutMs = 30_000;
    private const int SilenceTimeoutMs = 60_000;
    private const int WatchdogIntervalMs = 10_000;

    private readonly string _address;
    private readonly string? _directUrl;
    private readonly string _relayUrl;
    private readonly AgentIdentity _identity;
    private readonly HttpClient _http;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _silenceTimeout;
    private readonly TimeSpan _watchdogInterval;

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
    private volatile bool _awaitingHumanResponse;

    private TaskCompletionSource<WireMessage>? _connectTcs;
    private TaskCompletionSource<string>? _inputTcs;
    private TaskCompletionSource<string>? _sessionStatusTcs;
    private CancellationTokenSource? _connectTimeoutCts;

    public string? SessionId { get; private set; }

    /// <summary>
    /// The approval mode to put in the CONNECT frame's <c>session</c> object. This only
    /// lands for a session the host has never run before: on any later CONNECT the host's
    /// <c>merge_sessions</c> compares iteration counts and replaces the client's session
    /// wholesale with its own (we report no iteration, so it always wins). For an existing
    /// session the mode must therefore ride along with an INPUT — see
    /// <see cref="SendInputAsync"/> — or be pushed mid-turn with <see cref="SetModeAsync"/>.
    /// </summary>
    public string? InitialMode { get; set; }

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

    /// <summary>The agent's approval mode changed — either because we asked
    /// (<c>triggered_by: "user"</c>) or because the agent switched itself into plan mode.</summary>
    public event Action<ModeChangedEvent>? ModeChanged;

    /// <summary>The host queued a mid-execution prompt (our INPUT reached a session that already
    /// had an agent running, so it became a RUNTIME_INPUT rather than a new turn).</summary>
    public event Action? RuntimeInputAcknowledged;

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
        string relayUrl = EndpointResolver.DefaultRelay,
        TimeSpan? connectTimeout = null,
        TimeSpan? silenceTimeout = null,
        TimeSpan? watchdogInterval = null)
    {
        _address = address;
        _directUrl = string.IsNullOrWhiteSpace(directUrl) ? null : directUrl.TrimEnd('/');
        _identity = identity;
        _http = http ?? new HttpClient();
        _relayUrl = EndpointResolver.NormalizeRelayUrl(relayUrl);
        _connectTimeout = connectTimeout ?? TimeSpan.FromMilliseconds(ConnectTimeoutMs);
        _silenceTimeout = silenceTimeout ?? TimeSpan.FromMilliseconds(SilenceTimeoutMs);
        _watchdogInterval = watchdogInterval ?? TimeSpan.FromMilliseconds(WatchdogIntervalMs);
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
    /// <paramref name="images"/> and <paramref name="files"/> follow the exact
    /// wire contract in <see cref="InputMessageBuilder"/> — omit both for a
    /// text-only send identical to the original signature.
    /// </summary>
    public async Task<string> SendInputAsync(
        string prompt,
        string? sessionId,
        IReadOnlyList<string>? images = null,
        IReadOnlyList<OutgoingFileAttachment>? files = null,
        string? mode = null,
        CancellationToken ct = default)
    {
        // Carried into the CONNECT below when this is the socket's first turn.
        if (mode is not null) InitialMode = mode;

        await EnsureConnectedAsync(sessionId, ct).ConfigureAwait(false);

        var inputTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inputTcs = inputTcs;

        var inputMsg = InputMessageBuilder.BuildInput(
            prompt,
            Guid.NewGuid().ToString(),
            toAddress: _isDirect ? null : _address,
            images: images,
            files: files);
        await SendJsonAsync(inputMsg, ct).ConfigureAwait(false);

        // Mode has to be (re)asserted *after* the INPUT, never before. The host only forwards
        // a mode_change to the agent while a turn owns the connection (`active_io` in its
        // ws_router); sent on an idle socket it is dropped on the floor. Sending it here — on
        // the same socket, immediately behind the INPUT that spawns the agent — means the
        // host's sequential read loop has an agent to hand it to, and the agent's
        // `poll_mode_changes` (a before_iteration hook) picks it up at its first iteration
        // boundary. This is also why a CONNECT-only mode is not enough for a session the host
        // already knows: its session merge would have thrown ours away.
        if (mode is not null) await SendModeChangeAsync(mode, null, ct).ConfigureAwait(false);

        await using (ct.Register(() => inputTcs.TrySetCanceled()))
        {
            return await inputTcs.Task.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Switches the agent's approval mode on a live turn. Takes effect at the agent's next
    /// iteration boundary, not instantly (the host polls for these between LLM calls), and is
    /// silently dropped by the host if no turn is currently running — so callers must also pass
    /// the mode to the next <see cref="SendInputAsync"/>, which is what makes it stick.
    /// </summary>
    public Task SetModeAsync(string mode, int? turns = null, CancellationToken ct = default)
        => IsConnected ? SendModeChangeAsync(mode, turns, ct) : Task.CompletedTask;

    private Task SendModeChangeAsync(string mode, int? turns, CancellationToken ct)
    {
        var msg = new Dictionary<string, object?>
        {
            ["type"] = "mode_change",
            ["mode"] = mode,
        };
        if (turns is { } t) msg["turns"] = t;
        if (!_isDirect) msg["to"] = _address;
        return SendJsonAsync(msg, ct);
    }

    /// <summary>
    /// Sends a prompt into a turn that is <b>already running</b>. The host routes an INPUT on a
    /// running session to the agent's runtime-input mailbox instead of spawning a second agent,
    /// and the agent's <c>runtime_input</c> plugin appends it to the conversation at the next
    /// iteration boundary, framed as "additional context — do not abandon the original task
    /// unless the user explicitly says to stop".
    ///
    /// That framing is the whole reason this method exists: it is how a Stop request reaches an
    /// agent that is mid-LLM-call, because the protocol has <b>no</b> STOP frame and nothing
    /// else can preempt the agent's loop. It is therefore <b>cooperative</b> — the model chooses
    /// to comply — and must never be reported to the user as a completed stop. The pending
    /// <see cref="SendInputAsync"/> stays open; the turn still ends with a normal OUTPUT.
    /// </summary>
    public Task SendRuntimeInputAsync(string prompt, CancellationToken ct = default)
    {
        if (!IsConnected) return Task.CompletedTask;
        return SendJsonAsync(
            InputMessageBuilder.BuildInput(
                prompt, Guid.NewGuid().ToString(), toAddress: _isDirect ? null : _address),
            ct);
    }

    /// <summary>
    /// Asks the host what it thinks of a session: <see cref="SessionStatuses.Running"/>,
    /// <see cref="SessionStatuses.Connected"/>, or <see cref="SessionStatuses.NotFound"/>.
    /// Used after a force-stop, where the local run is gone but the agent thread on the host
    /// may well still be executing — in which case the next INPUT would be swallowed as a
    /// runtime input rather than starting a fresh turn, and the user deserves to know.
    /// Answers <see cref="SessionStatuses.NotFound"/> if there is no socket or no reply in time.
    /// </summary>
    public async Task<string> QuerySessionStatusAsync(string sessionId, CancellationToken ct = default)
    {
        if (!IsConnected) return SessionStatuses.NotFound;

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessionStatusTcs = tcs;

        var msg = new Dictionary<string, object?>
        {
            ["type"] = "SESSION_STATUS",
            ["session"] = new Dictionary<string, object?> { ["session_id"] = sessionId },
        };
        if (!_isDirect) msg["to"] = _address;

        try
        {
            await SendJsonAsync(msg, ct).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await using (timeout.Token.Register(() => tcs.TrySetResult(SessionStatuses.NotFound)))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }
        catch
        {
            return SessionStatuses.NotFound;
        }
        finally
        {
            _sessionStatusTcs = null;
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
        connectCts.CancelAfter(_connectTimeout);
        var connectToken = connectCts.Token;

        await ResolveConnectionAsync(connectToken).ConfigureAwait(false);

        _loopCts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(_wsUrl!), connectToken).ConfigureAwait(false);

        _lastActivityUtc = DateTime.UtcNow;
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_loopCts.Token), CancellationToken.None);
        _ = Task.Run(() => WatchdogAsync(_loopCts.Token), CancellationToken.None);

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
            var session = new Dictionary<string, object?> { ["session_id"] = sessionId };
            // Only decides the mode for a session the host has never run (see InitialMode):
            // otherwise its merge keeps the server's copy. Harmless either way, and it is the
            // only way a brand-new conversation starts its very first turn in the right mode.
            if (InitialMode is not null) session["mode"] = InitialMode;
            connectMsg["session"] = session;
            if (!string.IsNullOrEmpty(LastProcessedEventId))
                connectMsg["last_msg_id"] = LastProcessedEventId;
        }

        await SendJsonAsync(connectMsg, connectToken).ConfigureAwait(false);

        // The connect deadline is the only bounded wait; ONBOARD_REQUIRED
        // cancels it (a human/onboarding step now owns the connection).
        _connectTimeoutCts = new CancellationTokenSource(_connectTimeout);
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
    {
        ResumeWatchdogAfterHumanResponse();
        return SendJsonAsync(new Dictionary<string, object?>
        {
            ["type"] = "ASK_USER_RESPONSE",
            ["answer"] = answer,
        }, CancellationToken.None);
    }

    /// <summary>
    /// Responds to an approval_needed turn. <paramref name="scope"/> is "once" or "session".
    ///
    /// <paramref name="rejectMode"/> decides what a rejection <i>does</i>, and the default is
    /// deliberately <see cref="ApprovalRejectModes.Hard"/> — matching the host's own default.
    /// Hard is the only frame in the entire protocol that halts a running agent: it sets
    /// <c>stop_signal</c>, which rejects the rest of the tool batch and breaks the iteration
    /// loop. Soft/Explain merely skip the one tool and let the agent carry on, which is not
    /// what a user who clicked "Reject" means.
    /// </summary>
    public Task RespondApprovalAsync(
        bool approved,
        string scope = "once",
        string rejectMode = ApprovalRejectModes.Hard,
        string? feedback = null)
    {
        ResumeWatchdogAfterHumanResponse();
        var msg = new Dictionary<string, object?>
        {
            ["type"] = "APPROVAL_RESPONSE",
            ["approved"] = approved,
            ["scope"] = scope,
        };
        if (!approved)
        {
            msg["mode"] = rejectMode;
            if (!string.IsNullOrEmpty(feedback)) msg["feedback"] = feedback;
        }
        return SendJsonAsync(msg, CancellationToken.None);
    }

    /// <summary>Responds to a plan_review turn. Port of PLAN_REVIEW_RESPONSE.</summary>
    public Task RespondPlanReviewAsync(string message)
    {
        ResumeWatchdogAfterHumanResponse();
        return SendJsonAsync(new Dictionary<string, object?>
        {
            ["type"] = "PLAN_REVIEW_RESPONSE",
            ["message"] = message,
        }, CancellationToken.None);
    }

    private void ResumeWatchdogAfterHumanResponse()
    {
        // A human can legitimately leave the turn idle past the silence timeout.
        // Once they respond, give the agent a fresh timeout window instead of
        // counting the time spent waiting for the human as server silence.
        _lastActivityUtc = DateTime.UtcNow;
        _awaitingHumanResponse = false;
    }

    private static AskUserRequest ParseAskUser(WireMessage msg) => AgentInteractiveParsers.ParseAskUser(msg);

    /// <summary>Frame-reassembly buffer above this size is dropped rather than reused (see below).</summary>
    private const int MaxRetainedFrameCapacity = 64 * 1024;

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
                // Clear() keeps the capacity, so one multi-megabyte frame (a base64 image, an
                // LLM call carrying the whole message history) would otherwise leave that buffer
                // resident on the connection for as long as it lives. Reassemble small frames in
                // the existing builder; start fresh after an outsized one.
                if (frame.Capacity > MaxRetainedFrameCapacity) frame = new StringBuilder();
                else frame.Clear();
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
                _awaitingHumanResponse = false;
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
                _awaitingHumanResponse = true;
                AskUserRequested?.Invoke(ParseAskUser(msg));
                return;

            case "approval_needed":
                _awaitingHumanResponse = true;
                ApprovalRequested?.Invoke(new ApprovalRequest(
                    msg.GetString("tool") ?? "",
                    msg.GetString("description"),
                    msg.TryGet("arguments", out var argsEl) ? argsEl.GetRawText() : "{}"));
                return;

            case "plan_review":
                _awaitingHumanResponse = true;
                PlanReviewRequested?.Invoke(new PlanReviewRequest(msg.GetString("plan_content") ?? ""));
                return;

            case "mode_changed":
            {
                // Forwarded as a stream event *as well as* a typed one: the stream copy is what
                // the run buffers and the projection turns into a visible "Mode: plan" card, so a
                // mode the agent chose for itself is never a silent change.
                var mode = msg.GetString("mode");
                if (string.IsNullOrEmpty(mode)) return;
                var eventId = msg.GetString("id");
                if (!string.IsNullOrEmpty(eventId)) LastProcessedEventId = eventId;
                StreamEvent?.Invoke(new AgentStreamEvent(
                    msg.Type, DescribeEvent(msg), eventId, msg.Root.GetRawText()));
                ModeChanged?.Invoke(new ModeChangedEvent(mode, msg.GetString("triggered_by")));
                return;
            }

            case "RUNTIME_INPUT_ACK":
                RuntimeInputAcknowledged?.Invoke();
                return;

            case "SESSION_STATUS":
                _sessionStatusTcs?.TrySetResult(msg.GetString("status") ?? SessionStatuses.NotFound);
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
        "mode_changed" => $"Mode: {AgentModes.DisplayName(msg.GetString("mode"))}",
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
                await Task.Delay(_watchdogInterval, ct).ConfigureAwait(false);
                if (!_awaitingHumanResponse && DateTime.UtcNow - _lastActivityUtc > _silenceTimeout)
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
        _awaitingHumanResponse = false;
        _connectTcs?.TrySetException(ex);
        _inputTcs?.TrySetException(ex);
        // A status query is a question about the *host*, so a dead socket answers it rather
        // than faulting it: with no connection we cannot know, and "not_found" is what the
        // caller already treats as "no information".
        _sessionStatusTcs?.TrySetResult(SessionStatuses.NotFound);
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
        _awaitingHumanResponse = false;

        using var closeCts = new CancellationTokenSource(SocketCloseTimeout);

        // Drain the receive loop so it cannot race with the socket close
        // below and spuriously call HandleConnectionLoss (which would
        // fault the pending TCS instances and re-cancel the loop CTS).
        if (_receiveLoop is not null)
        {
            try { await _receiveLoop.WaitAsync(closeCts.Token).ConfigureAwait(false); }
            catch
            {
                // A provider that does not promptly interrupt ReceiveAsync must not hold app exit.
                _ws?.Abort();
            }
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
        ModeChanged = null;
        RuntimeInputAcknowledged = null;

        if (_ws is not null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                _ws.Abort();
            }
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
