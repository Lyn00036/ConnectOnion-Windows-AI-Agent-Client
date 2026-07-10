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
public sealed partial class ChatViewModel : ObservableObject
{
    private static readonly Regex GenericTitle = new(@"^Conversation \d+$", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    // ------ Session data cache ------
    // Re-opening a previously visited session skips the database round-trip.
    private static readonly Dictionary<string, CachedSession> SessionCache = new();
    private const int MaxCachedSessions = 4;

    private sealed record CachedSession(
        AgentConfig Agent,
        SessionSummary Session,
        List<ChatMessage> Messages,
        long CreatedAt);
    // -------------------------------

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly AgentPresenceService _presence = AppServices.Presence;

    private AgentConfig? _agent;
    private SessionSummary? _session;
    private SessionsState _sessionsState = new();
    private long _createdAt;
    private string? _currentExecutionId;

    // Trace events buffered in memory during execution, flushed to DB on OUTPUT.
    private readonly List<(string EventId, string Type, string RawJson, double? Ts)> _pendingTraces = new();

    // Accumulated across every llm_call/llm_result iteration of the current turn
    // (a turn can run several LLM calls via the tool-use loop) — used to show one
    // model+usage summary at the end instead of a bubble per iteration.
    private string? _turnModel;
    private long _turnTokensIn;
    private long _turnTokensOut;
    private bool _turnHasUsage;
    private double _turnDurationMs;
    private double? _turnContextPercent;
    private long _turnToolCallsCount;

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

    // True only while a user-initiated recheck is running, so the offline bar
    // stays visible (spinning) across the Offline→Checking→result transition
    // instead of vanishing the moment presence leaves Offline. Deliberately not
    // set by the initial page-load probe, so an online agent never flashes the
    // "offline" bar while its first check runs.
    private bool _isManualRecheck;

    public AgentPresence Presence => _agent is null ? AgentPresence.Unknown : _presence.GetPresence(_agent.Id);
    public bool IsOnline => Presence == AgentPresence.Online;
    public bool IsOffline => Presence == AgentPresence.Offline;
    public bool ShowOfflineNotice => HasAgent && (IsOffline || _isManualRecheck);
    public string PresenceDetail => _agent is null ? "" : _presence.GetDetail(_agent.Id);
    public string PresenceLabel => Presence switch
    {
        AgentPresence.Online => "Online",
        AgentPresence.Checking => "Checking...",
        AgentPresence.Offline => "Offline",
        _ => "Unknown",
    };

    /// <summary>True while a reachability probe is in flight (or a manual recheck is
    /// starting) — drives the offline bar's spinner.</summary>
    public bool IsCheckingPresence => _isManualRecheck || Presence == AgentPresence.Checking;

    private string _recheckFailedMessage = "";
    /// <summary>Attention line shown in the offline bar after a manual recheck that resolved to still-offline.</summary>
    public string RecheckFailedMessage
    {
        get => _recheckFailedMessage;
        private set => SetProperty(ref _recheckFailedMessage, value);
    }

    public string SelectedAgentName => _agent?.Name ?? "Agent";
    public string SelectedAgentInitial => string.IsNullOrWhiteSpace(_agent?.Name) ? "?" : _agent!.Name[..1].ToUpperInvariant();
    public string SessionShortId => _session is null ? "" : _session.Id[..Math.Min(8, _session.Id.Length)];
    /// <summary>The current conversation id, exposed for scroll-position caching.</summary>
    public string? SessionId => _session?.Id;
    public string ConnectionDetail => IsConnecting ? "connecting" : IsProcessing ? "working" : "idle";

    private AgentAcceptedInputs? _acceptedInputs;
    /// <summary>The connected agent's advertised <c>/info</c> capabilities. Null while unknown (never fetched, or the fetch failed) — attachment UI does not block on unknown capability, see <see cref="Services.AttachmentValidationService"/>.</summary>
    public AgentAcceptedInputs? AcceptedInputs
    {
        get => _acceptedInputs;
        private set => SetProperty(ref _acceptedInputs, value);
    }

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

    private double _messageFontSize = 14;
    public double MessageFontSize { get => _messageFontSize; private set => SetProperty(ref _messageFontSize, value); }

    private bool _enterToSend = true;
    public bool EnterToSend { get => _enterToSend; private set => SetProperty(ref _enterToSend, value); }

    public async Task LoadAsync()
    {
        var prefs = await AppServices.Preferences.LoadAsync();
        EnterToSend = prefs.EnterToSend;
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

        // Fire-and-forget: gates the attachment UI once it resolves, but a slow
        // or failed /info probe must never block the chat from opening.
        _ = RefreshAcceptedInputsAsync();
    }

    /// <summary>
    /// Fetches (or reuses the cached) <c>/info</c> response and updates
    /// <see cref="AcceptedInputs"/> so the composer can gate image/file
    /// attachments on the agent's real advertised capability.
    /// </summary>
    private async Task RefreshAcceptedInputsAsync()
    {
        if (_agent is null) return;

        string? infoJson;
        try
        {
            infoJson = await AgentInfoService.FetchAndPersistAsync(_agent).ConfigureAwait(false);
        }
        catch
        {
            return;
        }
        if (infoJson is null) return;

        var parsed = EndpointResolver.ParseAcceptedInputsFromInfoJson(infoJson);
        _dispatcher.TryEnqueue(() => AcceptedInputs = parsed);
    }

    /// <summary>Re-probes the agent's online status (bound to the offline bar's
    /// Recheck button). Awaits the probe so the bar can spin while it runs and
    /// show a "still offline" note if it resolves without reconnecting.</summary>
    public async Task RecheckPresenceAsync()
    {
        if (_agent is null) return;
        RecheckFailedMessage = "";
        _isManualRecheck = true;
        RaisePresenceProperties();
        try
        {
            await _presence.RefreshAsync(_agent);
        }
        finally
        {
            _isManualRecheck = false;
            RecheckFailedMessage = IsOffline
                ? "Still offline. The agent hasn't reconnected — try again in a moment."
                : "";
            RaisePresenceProperties();
        }
    }

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
                // Re-probe presence so a genuine drop surfaces the offline +
                // reconnect bar (ShowOfflineNotice), while a transient blip
                // simply re-confirms Online without a false "offline".
                if (_agent is not null) _ = _presence.RefreshAsync(_agent);
            });
        _connection.OnboardRequired += () =>
            _dispatcher.TryEnqueue(AddOnboardingBubble);
        _connection.AskUserRequested += r =>
            _dispatcher.TryEnqueue(() => AddAskUserBubble(r));
        _connection.ApprovalRequested += r =>
            _dispatcher.TryEnqueue(() => AddApprovalBubble(r));
        _connection.PlanReviewRequested += r =>
            _dispatcher.TryEnqueue(() => AddPlanReviewBubble(r));
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
        if (_agent is null || _session is null) return;

        // Reuse cached session data when re-opening a previously visited
        // session — skip the database round-trip.
        if (SessionCache.TryGetValue(_session.Id, out var cached)
            && cached.Agent.Id == _agent.Id)
        {
            Messages.Clear();
            foreach (var message in cached.Messages) Messages.Add(message);
            _createdAt = cached.CreatedAt;
            return;
        }

        Messages.Clear();
        var messages = await AppServices.Conversations.LoadMessagesAsync(_session.Id);
        _createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (messages.Count > 0)
        {
            foreach (var message in messages) Messages.Add(message);
            _createdAt = messages[0].Id > 0 ? messages[0].Id : _createdAt;
        }

        // Write to cache, evicting the oldest entry if at capacity.
        if (SessionCache.Count >= MaxCachedSessions)
        {
            var oldest = SessionCache.Keys.First();
            SessionCache.Remove(oldest);
        }
        SessionCache[_session.Id] = new CachedSession(
            _agent, _session, Messages.ToList(), _createdAt);
    }

    public async Task SendAsync(string text, IReadOnlyList<PendingAttachment>? attachments = null)
    {
        text = text?.Trim() ?? "";
        var hasAttachments = attachments is { Count: > 0 };
        if ((text.Length == 0 && !hasAttachments) || _agent is null || _session is null || IsProcessing) return;

        var userMessage = new ChatMessage { Id = NextId(), Role = ChatRole.User, Content = text };
        List<string>? imageDataUrls = null;
        List<OutgoingFileAttachment>? fileAttachments = null;

        if (hasAttachments)
        {
            imageDataUrls = new List<string>();
            fileAttachments = new List<OutgoingFileAttachment>();
            foreach (var attachment in attachments!)
            {
                userMessage.Attachments.Add(new ChatAttachment
                {
                    Kind = attachment.Kind,
                    FileName = attachment.FileName,
                    MimeType = attachment.MimeType,
                    SizeBytes = attachment.SizeBytes,
                    LocalCachePath = attachment.LocalPath,
                    Status = AttachmentStatus.Sent,
                });

                if (attachment.DataUrl is null) continue; // shouldn't happen (composer only sends Ready attachments), skip defensively
                if (attachment.Kind == AttachmentKind.Image) imageDataUrls.Add(attachment.DataUrl);
                else fileAttachments.Add(new OutgoingFileAttachment(attachment.FileName, attachment.DataUrl));
            }
            if (imageDataUrls.Count == 0) imageDataUrls = null;
            if (fileAttachments.Count == 0) fileAttachments = null;

            System.Diagnostics.Debug.WriteLine(
                $"[Attachment] sending images={imageDataUrls?.Count ?? 0} files={fileAttachments?.Count ?? 0}");
        }

        Messages.Add(userMessage);
        TouchSession(text);
        await PersistConversationAsync();

        IsProcessing = true;
        StatusText = _connection is null ? "Working..." : "Connecting...";
        var sent = false;
        var executionId = Guid.NewGuid().ToString();
        _currentExecutionId = executionId;
        _pendingTraces.Clear();
        _turnModel = null;
        _turnTokensIn = 0;
        _turnTokensOut = 0;
        _turnHasUsage = false;
        _turnDurationMs = 0;
        _turnContextPercent = null;
        _turnToolCallsCount = 0;
        await AppServices.Conversations.InsertExecutionAsync(
            executionId, _session.Id, null, text, "running");
        try
        {
            var reply = _connection is not null
                ? await _connection.SendInputAsync(text, _session.Id, images: imageDataUrls, files: fileAttachments)
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
            AddTurnSummaryBubble();
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
            // A failed turn (relay "Agent not connected", socket drop, timeout)
            // re-probes reachability: if the agent really is offline the presence
            // flip lights up the offline + reconnect bar; if it was transient the
            // probe re-confirms Online so we don't falsely mark it offline.
            _ = _presence.RefreshAsync(_agent);
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

            // Release the base64 payload now that the send attempt is over
            // (success or failure) — it must not linger as long-held string state.
            if (hasAttachments)
            {
                foreach (var attachment in attachments!) attachment.DataUrl = null;
            }

            // Refresh the cache so re-opening this session shows the latest messages.
            if (_session is not null)
                SessionCache[_session.Id] = new CachedSession(
                    _agent!, _session, Messages.ToList(), _createdAt);
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

    private void RaisePresenceProperties()
    {
        // A fresh probe (Checking) or a recovery (Online) clears any stale
        // "still offline" note before we re-notify the derived properties.
        if (Presence != AgentPresence.Offline) RecheckFailedMessage = "";

        OnPropertyChanged(nameof(Presence));
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(IsOffline));
        OnPropertyChanged(nameof(ShowOfflineNotice));
        OnPropertyChanged(nameof(IsCheckingPresence));
        OnPropertyChanged(nameof(PresenceDetail));
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
