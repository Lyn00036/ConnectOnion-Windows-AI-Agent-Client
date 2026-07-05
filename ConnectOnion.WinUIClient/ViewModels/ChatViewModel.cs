using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Data;
using Microsoft.UI.Dispatching;

namespace ConnectOnion.WinUIClient.ViewModels;

/// <summary>
/// Backs the Chat page. This step builds the UI end-to-end against a SIMULATED
/// agent reply and real conversation persistence; the real ConnectOnion
/// transport replaces <see cref="SimulateReplyAsync"/> in Step 9.
/// Port reference: <c>ChatPage.tsx</c> + <c>useDirectAgentForHuman.ts</c>.
/// </summary>
public sealed class ChatViewModel : ObservableObject
{
    private static readonly Regex GenericTitle = new(@"^Conversation \d+$", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly AgentPresenceService _presence = AppServices.Presence;

    private AgentConfig? _agent;
    private SessionSummary? _session;
    private SessionsState _sessionsState = new();
    private long _createdAt;
    private string? _currentExecutionId;

    // Trace events buffered in memory during execution, flushed to DB on OUTPUT.
    private readonly List<(string EventId, string Type, string RawJson, double? Ts)> _pendingTraces = new();

    // Real transport when the agent exposes a Direct URL; null falls back to the
    // simulated reply so the UI still works for agents without one.
    private AgentConnectionService? _connection;
    private string? _connectedAgentId;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public ChatViewModel()
    {
        _presence.PresenceChanged += OnPresenceChanged;
    }

    /// <summary>Detaches from the shared presence service and disposes the connection. Call when the page unloads.</summary>
    public void Cleanup()
    {
        _presence.PresenceChanged -= OnPresenceChanged;
        var old = _connection;
        _connection = null;
        _ = old?.DisposeAsync();
    }

    private void OnPresenceChanged(string agentId)
    {
        if (_agent is null || agentId != _agent.Id) return;
        _dispatcher.TryEnqueue(RaisePresenceProperties);
    }

    private bool _hasAgent;
    public bool HasAgent
    {
        get => _hasAgent;
        private set
        {
            if (!SetProperty(ref _hasAgent, value)) return;
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanReconnect));
        }
    }

    private bool _isProcessing;
    public bool IsProcessing
    {
        get => _isProcessing;
        private set
        {
            if (!SetProperty(ref _isProcessing, value)) return;
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanReconnect));
            OnPropertyChanged(nameof(ConnectionDetail));
        }
    }

    private bool _isConnecting;
    public bool IsConnecting
    {
        get => _isConnecting;
        private set
        {
            if (!SetProperty(ref _isConnecting, value)) return;
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanReconnect));
            OnPropertyChanged(nameof(ConnectionDetail));
        }
    }

    // Sending is gated on the agent being confirmed online — matching the "green
    // dot means you can chat" model — not on holding a live socket (the transport
    // connects lazily on the first send).
    public bool CanSend => HasAgent && IsOnline && !IsProcessing && !IsConnecting;
    public bool CanReconnect => HasAgent && !IsProcessing && !IsConnecting && Presence != AgentPresence.Checking;
    public bool HasEndpoint => _connection is not null;
    public bool HasConnection => _connection?.IsConnected == true;

    public AgentPresence Presence => _agent is null ? AgentPresence.Unknown : _presence.GetPresence(_agent.Id);
    public bool IsOnline => Presence == AgentPresence.Online;
    public string PresenceLabel => Presence switch
    {
        AgentPresence.Online => "Online",
        AgentPresence.Checking => "Checking...",
        AgentPresence.Offline => "Offline",
        _ => "Unknown",
    };

    public string SelectedAgentName => _agent?.Name ?? "Agent";
    public string SelectedAgentInitial => string.IsNullOrWhiteSpace(_agent?.Name) ? "?" : _agent!.Name[..1].ToUpperInvariant();
    public string SessionShortId => _session is null ? "" : _session.Id[..Math.Min(8, _session.Id.Length)];
    /// <summary>The current conversation id, exposed for scroll-position caching.</summary>
    public string? SessionId => _session?.Id;
    public string ConnectionDetail => IsConnecting ? "connecting" : IsProcessing ? "working" : "idle";

    private string _headerText = "Chat";
    public string HeaderText { get => _headerText; private set => SetProperty(ref _headerText, value); }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (!SetProperty(ref _statusText, value)) return;
            OnPropertyChanged(nameof(HasStatusText));
        }
    }

    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    // Interactive turns, re-raised on the UI thread for the page to show dialogs.
    public event Action<AskUserRequest>? AskUserRequested;
    public event Action<ApprovalRequest>? ApprovalRequested;
    public event Action<PlanReviewRequest>? PlanReviewRequested;

    public Task RespondAskUserAsync(string answer)
        => _connection?.RespondAskUserAsync(answer) ?? Task.CompletedTask;

    public Task RespondApprovalAsync(bool approved, string scope = "once")
        => _connection?.RespondApprovalAsync(approved, scope) ?? Task.CompletedTask;

    public Task RespondPlanReviewAsync(string message)
        => _connection?.RespondPlanReviewAsync(message) ?? Task.CompletedTask;

    private double _messageFontSize = 14;
    public double MessageFontSize { get => _messageFontSize; private set => SetProperty(ref _messageFontSize, value); }

    public async Task LoadAsync()
    {
        var prefs = await AppServices.Preferences.LoadAsync();
        MessageFontSize = prefs.MessageFontSize switch
        {
            Models.MessageFontSize.Sm => 12,
            Models.MessageFontSize.Lg => 18,
            _ => 14,
        };

        var agents = await AppServices.Agents.LoadAsync();
        _agent = agents.Agents.FirstOrDefault(a => a.Id == agents.SelectedAgentId);
        HasAgent = _agent is not null;
        OnPropertyChanged(nameof(CanSend));

        if (_agent is null)
        {
            HeaderText = "Select an agent to start chatting";
            RaiseHeaderProperties();
            return;
        }

        _session = await ResolveSessionAsync(_agent.Id);
        HeaderText = $"{_agent.Name} - {_session.Title}";
        RaiseHeaderProperties();

        SetupConnection();
        await RestoreConversationAsync();

        // Reflect cached presence immediately. The sidebar owns automatic
        // probing; the header button can still force a recheck.
        RaisePresenceProperties();
    }

    /// <summary>Re-probes the agent's online status (bound to the header button).</summary>
    public Task RecheckPresenceAsync()
        => _agent is null ? Task.CompletedTask : _presence.RefreshAsync(_agent);

    // Wire the real transport when the agent has a Direct URL. Stream events and
    // connection-loss callbacks arrive on the receive-loop thread, so marshal
    // them onto the UI thread before touching bound state.
    private void SetupConnection()
    {
        if (_agent is null) return;
        var hasDirect = !string.IsNullOrWhiteSpace(_agent.DirectUrl);
        var hasAddress = !string.IsNullOrWhiteSpace(_agent.Address);
        if (!hasDirect && !hasAddress) return;

        // Already connected to this agent — nothing to do.
        if (_connectedAgentId == _agent.Id && _connection is not null) return;

        // Dispose the old connection (WebSocket + receive loop + watchdog).
        var old = _connection;
        _connection = null;
        _connectedAgentId = null;
        _ = old?.DisposeAsync();

        var identity = IdentityStore.EnsureIdentity();
        _connection = new AgentConnectionService(_agent.Address, _agent.DirectUrl, identity, AppServices.Http);
        _connectedAgentId = _agent.Id;
        _connection.StreamEvent += e =>
            _dispatcher.TryEnqueue(() => ApplyStreamEvent(e));
        _connection.ConnectionLost += ex =>
            _dispatcher.TryEnqueue(() =>
            {
                StatusText = $"Disconnected: {ex.Message}";
                RaiseConnectionProperties();
            });
        _connection.OnboardRequired += () =>
            _dispatcher.TryEnqueue(AddOnboardingBubble);
        _connection.AskUserRequested += r =>
            _dispatcher.TryEnqueue(() => AskUserRequested?.Invoke(r));
        _connection.ApprovalRequested += r =>
            _dispatcher.TryEnqueue(() => ApprovalRequested?.Invoke(r));
        _connection.PlanReviewRequested += r =>
            _dispatcher.TryEnqueue(() => PlanReviewRequested?.Invoke(r));
        RaiseConnectionProperties();
    }

    /// <summary>
    /// Reuses the active session if it belongs to this agent, else the agent's
    /// most recent conversation, else creates a fresh one. Port of the store's
    /// <c>ensureSession</c>.
    /// </summary>
    private async Task<SessionSummary> ResolveSessionAsync(string agentId)
    {
        _sessionsState = await AppServices.Sessions.LoadAsync();

        var active = _sessionsState.Sessions
            .FirstOrDefault(s => s.Id == _sessionsState.ActiveSessionId && s.AgentId == agentId);
        if (active is not null) return active;

        var timestamp = DateTime.UtcNow.ToString("o");
        var count = _sessionsState.Sessions.Count(s => s.AgentId == agentId) + 1;
        var session = new SessionSummary
        {
            Id = Guid.NewGuid().ToString(),
            AgentId = agentId,
            Title = $"Conversation {count}",
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
        _sessionsState.Sessions.Add(session);
        _sessionsState.ActiveSessionId = session.Id;
        await AppServices.Sessions.SaveAsync(_sessionsState);
        return session;
    }

    private async Task RestoreConversationAsync()
    {
        Messages.Clear();
        if (_agent is null || _session is null) return;

        var messages = await AppServices.Conversations.LoadMessagesAsync(_session.Id);
        _createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (messages.Count == 0) return;

        foreach (var message in messages) Messages.Add(message);
        _createdAt = messages[0].Id > 0 ? messages[0].Id : _createdAt;
    }

    public async Task SendAsync(string text)
    {
        text = text?.Trim() ?? "";
        if (text.Length == 0 || _agent is null || _session is null || IsProcessing) return;

        Messages.Add(new ChatMessage { Id = NextId(), Role = ChatRole.User, Content = text });
        TouchSession(text);
        await PersistConversationAsync();

        IsProcessing = true;
        StatusText = _connection is null ? "Working..." : "Connecting...";
        var sent = false;
        var executionId = Guid.NewGuid().ToString();
        _currentExecutionId = executionId;
        _pendingTraces.Clear();
        await AppServices.Conversations.InsertExecutionAsync(
            executionId, _session.Id, null, text, "running");
        try
        {
            var reply = _connection is not null
                ? await _connection.SendInputAsync(text, _session.Id)
                : await SimulateReplyAsync(text);
            sent = true;
            RaiseConnectionProperties();

            // The agent may have already streamed this text via an `assistant`
            // event. Match the SDK: add the final reply only if it differs from
            // the last agent bubble, so the same text never renders twice.
            if (!string.IsNullOrEmpty(reply))
            {
                var lastAgent = Messages.LastOrDefault(m => m.Role == ChatRole.Agent);
                if (lastAgent is null || lastAgent.Content != reply)
                {
                    Messages.Add(new ChatMessage
                    {
                        Id = NextId(),
                        Role = ChatRole.Agent,
                        Content = reply,
                        AgentName = _agent.Name,
                    });
                }
            }
            await PersistConversationAsync();
            await PersistSessionMetadataAsync();
            await FlushPendingTracesAsync();
            await AppServices.Conversations.FinalizeExecutionAsync(
                executionId, _session.Id, reply, "done",
                _connection?.LastExecutionDurationMs ?? 0);
        }
        catch (Exception ex)
        {
            StatusText = $"Send failed: {UserFacingError(ex)}";
            RaiseConnectionProperties();
            Messages.Add(new ChatMessage
            {
                Id = NextId(),
                Role = ChatRole.Agent,
                Content = $"[connection error] {UserFacingError(ex)}",
                AgentName = _agent.Name,
            });
            await PersistConversationAsync();
            await AppServices.Conversations.FinalizeExecutionAsync(
                executionId, _session.Id, ex.Message, "error", 0);
        }
        finally
        {
            _currentExecutionId = null;
            IsProcessing = false;
            if (sent) StatusText = "";
            RaiseConnectionProperties();
        }
    }

    public async Task NewConversationAsync()
    {
        if (_agent is null || IsProcessing) return;

        var timestamp = DateTime.UtcNow.ToString("o");
        var count = _sessionsState.Sessions.Count(s => s.AgentId == _agent.Id) + 1;
        _session = new SessionSummary
        {
            Id = Guid.NewGuid().ToString(),
            AgentId = _agent.Id,
            Title = $"Conversation {count}",
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
        _sessionsState.Sessions.Add(_session);
        _sessionsState.ActiveSessionId = _session.Id;
        await AppServices.Sessions.SaveAsync(_sessionsState);

        Messages.Clear();
        _createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        HeaderText = $"{_agent.Name} - {_session.Title}";
        RaiseHeaderProperties();
    }

    public async Task ReconnectAsync()
    {
        if (_agent is null || _session is null) return;

        if (_connection is null)
        {
            SetupConnection();
        }

        if (_connection is null)
        {
            StatusText = "No agent endpoint configured.";
            RaiseConnectionProperties();
            return;
        }

        IsConnecting = true;
        StatusText = "Reconnecting...";
        RaiseConnectionProperties();

        try
        {
            await _connection.ReconnectAsync(_session.Id);
            StatusText = "Connected.";
        }
        catch (Exception ex)
        {
            StatusText = $"Reconnect failed: {UserFacingError(ex)}";
        }
        finally
        {
            IsConnecting = false;
            RaiseConnectionProperties();
        }
    }

    public async Task SubmitOnboardInviteCodeAsync(ChatMessage message)
    {
        if (_connection is null) return;

        var inviteCode = message.OnboardInviteCode.Trim();
        if (inviteCode.Length == 0) return;

        message.IsOnboardingSubmitted = true;
        message.Content = "Invite code submitted. Waiting for the agent to finish onboarding...";
        StatusText = "Submitting invite code...";
        OnPropertyChanged(nameof(Messages));

        try
        {
            await _connection.SubmitOnboardInviteCodeAsync(inviteCode);
        }
        catch (Exception ex)
        {
            message.IsOnboardingSubmitted = false;
            message.Content = $"Invite code submission failed: {ex.Message}";
            StatusText = "";
        }
    }

    // Placeholder for the real transport (Step 9). Kept isolated so swapping in
    // AgentConnectionService touches only this method.
    private static async Task<string> SimulateReplyAsync(string prompt)
    {
        await Task.Delay(600);
        return $"(simulated) You said: {prompt}";
    }

    private long NextId() => Messages.Count == 0 ? 1 : Messages.Max(m => m.Id) + 1;

    private void AddOnboardingBubble()
    {
        if (Messages.Any(message => message.IsOnboarding && !message.IsOnboardingSubmitted)) return;

        Messages.Add(new ChatMessage
        {
            Id = NextId(),
            Role = ChatRole.Agent,
            AgentName = _agent?.Name,
            IsOnboarding = true,
            Content = "This agent requires an invite code before the conversation can continue.",
        });
        StatusText = "Waiting for invite code...";
    }

    /// <summary>
    /// Maps a streamed event onto the message list, C# port of the SDK's
    /// <c>chat-item-mapper.ts</c>. Paired events merge by id into one bubble that
    /// transitions running → done/error (tool_call+tool_result, llm_call+llm_result),
    /// so a tool shows its arguments, result, and timing in a single card.
    /// </summary>
    private void ApplyStreamEvent(AgentStreamEvent e)
    {
        StatusText = e.Description;

        // Buffer trace event in memory; flushed to DB on OUTPUT.
        if (_session is not null && _currentExecutionId is not null)
        {
            _pendingTraces.Add((e.EventId ?? Guid.NewGuid().ToString(), e.Type, e.RawJson, TryParseTs(e.RawJson)));
        }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(e.RawJson);
            root = doc.RootElement.Clone();
        }
        catch
        {
            root = default;
        }

        string? Str(string name) => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;
        double? Num(string name) => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetDouble() : null;
        bool? Flag(string name) => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var v)
                ? v.ValueKind == JsonValueKind.True ? true
                    : v.ValueKind == JsonValueKind.False ? false : (bool?)null
                : null;
        string? RawObj(string name) => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? PrettyJson(v.GetRawText()) : null;

        switch (e.Type)
        {
            case "tool_call":
                Messages.Add(new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "tool",
                    EventKey = Str("tool_id") ?? Str("id"),
                    EventEyebrow = "Tool",
                    EventTitle = Str("name") ?? "tool",
                    EventArgs = RawObj("args"),
                    Status = EventStatus.Running,
                });
                break;

            case "tool_result":
            {
                var existing = FindEvent(Str("tool_id") ?? Str("id"), "tool");
                if (existing is not null)
                {
                    existing.Status = Str("status") == "error" ? EventStatus.Error : EventStatus.Done;
                    existing.EventResult = Str("result");
                    if (Num("timing_ms") is { } timing) existing.EventMeta = FormatDuration(timing);
                }
                break;
            }

            case "llm_call":
            {
                var id = Str("id");
                var iteration = Num("iteration");
                var model = Str("model");
                var title = iteration is not null
                    ? $"Iteration {iteration:0}"
                    : "Thinking...";
                Messages.Add(new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "activity",
                    EventKey = id,
                    EventTitle = title,
                    EventDetail = model,
                    Status = EventStatus.Running,
                });
                break;
            }

            case "llm_result":
            {
                var existing = FindEvent(Str("id"), "activity");
                if (existing is not null)
                {
                    existing.Status = Str("status") == "error" ? EventStatus.Error : EventStatus.Done;
                    var iteration = Num("iteration");
                    existing.EventTitle = iteration is not null
                        ? $"Iteration {iteration:0} complete"
                        : "Thinking complete";

                    var parts = new List<string>();
                    if (Num("duration_ms") is { } dur) parts.Add(FormatDuration(dur));
                    if (ParseTokenUsage(root) is (var tokIn, var tokOut) && tokIn is not null)
                        parts.Add($"{tokIn}→{tokOut} tok");
                    if (Num("context_percent") is { } ctx)
                        parts.Add($"ctx {ctx:0.#}%");
                    if (Num("tool_calls_count") is { } tcc and > 0)
                        parts.Add($"{tcc:0} tools");
                    existing.EventMeta = string.Join(" · ", parts);

                    existing.EventDetail = Str("model") ?? existing.EventDetail;
                }
                break;
            }

            case "thinking":
                Messages.Add(new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "activity",
                    EventTitle = "Thinking",
                    EventDetail = Str("content"),
                    Status = EventStatus.Done,
                });
                break;

            case "intent":
            {
                var id = Str("id");
                if (Str("status") == "analyzing")
                {
                    Messages.Add(NewActivity(id, "Understanding request...", EventStatus.Running));
                }
                else if (FindEvent(id, "activity") is { } existing)
                {
                    existing.EventTitle = "Request understood";
                    existing.EventDetail = Str("ack");
                    existing.Status = EventStatus.Done;
                }
                break;
            }

            case "eval":
            {
                var id = Str("id");
                if (Str("status") == "evaluating")
                {
                    var item = NewActivity(id, "Evaluating result...", EventStatus.Running);
                    item.EventDetail = Str("expected");
                    Messages.Add(item);
                }
                else if (FindEvent(id, "activity") is { } existing)
                {
                    existing.EventTitle = "Evaluation complete";
                    existing.EventDetail = Str("summary") ?? Str("expected");
                    existing.Status = Flag("passed") == false ? EventStatus.Error : EventStatus.Done;
                }
                break;
            }

            case "compact":
            {
                var id = Str("id");
                if (Str("status") == "compacting")
                {
                    Messages.Add(NewActivity(id, "Compacting context...", EventStatus.Running));
                }
                else if (FindEvent(id, "activity") is { } existing)
                {
                    existing.EventTitle = "Context compacted";
                    existing.Status = Str("status") == "error" ? EventStatus.Error : EventStatus.Done;
                    existing.EventDetail = Str("message") ?? Str("error");
                    if (Num("context_before") is { } before && Num("context_after") is { } after)
                        existing.EventMeta = $"{before:0} → {after:0}";
                }
                break;
            }

            case "tool_blocked":
                Messages.Add(new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "tool",
                    EventEyebrow = "Tool blocked",
                    EventTitle = Str("tool") ?? "tool",
                    EventDetail = Str("message"),
                    EventMeta = Str("reason"),
                    EventResult = Str("command"),
                    Status = EventStatus.Error,
                });
                break;

            case "files_received":
            {
                var names = new List<string>();
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                {
                    foreach (var file in files.EnumerateArray())
                        if (file.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                            names.Add(n.GetString()!);
                }
                var item = NewActivity(null, $"Files received - {names.Count} file(s)", EventStatus.Done);
                if (names.Count > 0) item.EventDetail = string.Join(", ", names);
                Messages.Add(item);
                break;
            }

            case "assistant":
            {
                // Streamed agent text. The final OUTPUT dedups against the last
                // agent bubble, so rendering these here does not double the reply.
                var content = Str("content");
                if (!string.IsNullOrEmpty(content))
                {
                    Messages.Add(new ChatMessage
                    {
                        Id = NextId(),
                        Role = ChatRole.Agent,
                        Content = content,
                        AgentName = _agent?.Name,
                    });
                }
                break;
            }

            case "ONBOARD_SUCCESS":
                Messages.Add(NewActivity(null, "Onboarding complete", EventStatus.Done));
                break;

            // "agent_image" and "ONBOARD_REQUIRED" are handled by their own flows.
        }
    }

    private ChatMessage NewActivity(string? key, string title, EventStatus status) => new()
    {
        Id = NextId(),
        Role = ChatRole.Event,
        EventKind = "activity",
        EventKey = key,
        EventTitle = title,
        Status = status,
    };

    // Most-recent event bubble of this kind still awaiting its finishing frame.
    private ChatMessage? FindEvent(string? key, string kind)
        => string.IsNullOrEmpty(key)
            ? null
            : Messages.LastOrDefault(m => m.IsEvent && m.EventKind == kind && m.EventKey == key);

    private static string FormatDuration(double ms)
        => ms < 1000 ? $"{ms:0} ms" : $"{ms / 1000:0.0} s";

    // Re-indents a JSON fragment for display without reflection (trim-safe).
    private static string? PrettyJson(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                doc.RootElement.WriteTo(writer);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch
        {
            return raw;
        }
    }

    private void TouchSession(string prompt)
    {
        if (_session is null) return;

        if (GenericTitle.IsMatch(_session.Title) && !string.IsNullOrWhiteSpace(prompt))
        {
            var title = Whitespace.Replace(prompt.Trim(), " ");
            _session.Title = title.Length > 48 ? title[..48] : title;
        }
        _session.UpdatedAt = DateTime.UtcNow.ToString("o");
        HeaderText = $"{_agent!.Name} - {_session.Title}";
        RaiseHeaderProperties();
        _ = AppServices.Sessions.SaveAsync(_sessionsState);
    }

    private void RaiseHeaderProperties()
    {
        OnPropertyChanged(nameof(SelectedAgentName));
        OnPropertyChanged(nameof(SelectedAgentInitial));
        OnPropertyChanged(nameof(SessionShortId));
    }

    private void RaiseConnectionProperties()
    {
        OnPropertyChanged(nameof(HasEndpoint));
        OnPropertyChanged(nameof(HasConnection));
        OnPropertyChanged(nameof(ConnectionDetail));
    }

    private async Task FlushPendingTracesAsync()
    {
        if (_session is null || _pendingTraces.Count == 0) return;
        var snap = _pendingTraces.ToList();
        _pendingTraces.Clear();

        foreach (var (eventId, type, rawJson, ts) in snap)
        {
            await AppServices.Conversations.InsertTraceEventAsync(
                eventId, _session.Id, _currentExecutionId,
                _connection?.SessionId, type, rawJson, ts);
        }
    }

    private static double? TryParseTs(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("ts", out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetDouble();
        }
        catch { }
        return null;
    }

    private static (string? input, string? output) ParseTokenUsage(JsonElement root)
    {
        if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
        {
            var input = u.TryGetProperty("input_tokens", out var i) && i.TryGetInt64(out var iv)
                ? FormatTokenCount(iv) : null;
            var output = u.TryGetProperty("output_tokens", out var o) && o.TryGetInt64(out var ov)
                ? FormatTokenCount(ov) : null;
            return (input, output);
        }
        return (null, null);
    }

    private static string FormatTokenCount(long tokens) =>
        tokens switch
        {
            >= 1_000_000 => $"{tokens / 1_000_000.0:0.#}M",
            >= 1_000 => $"{tokens / 1000.0:0.#}K",
            _ => tokens.ToString(),
        };

    private void RaisePresenceProperties()
    {
        OnPropertyChanged(nameof(Presence));
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(PresenceLabel));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanReconnect));
    }

    private static string UserFacingError(Exception ex)
        => ex is OperationCanceledException
            ? "Connection timed out."
            : ex.Message;

    private Task PersistConversationAsync()
    {
        if (_agent is null || _session is null) return Task.CompletedTask;

        var toSave = Messages.Where(m => !m.IsOnboarding).ToList();
        return AppServices.Conversations.SaveMessagesAsync(_session.Id, toSave);
    }

    /// <summary>
    /// Persists the remote session id and last processed event id so reconnect
    /// can skip already-seen events after a disconnect.
    /// </summary>
    private async Task PersistSessionMetadataAsync()
    {
        if (_agent is null || _session is null || _connection is null) return;

        _session.RemoteSessionId = _connection.SessionId;
        _session.LastProcessedEventId = _connection.LastProcessedEventId;
        _session.UpdatedAt = DateTime.UtcNow.ToString("o");
        await AppServices.Sessions.SaveAsync(_sessionsState);
    }
}
