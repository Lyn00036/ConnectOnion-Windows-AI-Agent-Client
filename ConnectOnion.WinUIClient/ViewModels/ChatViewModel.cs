using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
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

    private AgentConfig? _agent;
    private SessionSummary? _session;
    private SessionsState _sessionsState = new();
    private long _createdAt;

    // Real transport when the agent exposes a Direct URL; null falls back to the
    // simulated reply so the UI still works for agents without one.
    private AgentConnectionService? _connection;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    private bool _hasAgent;
    public bool HasAgent { get => _hasAgent; private set => SetProperty(ref _hasAgent, value); }

    private bool _isProcessing;
    public bool IsProcessing
    {
        get => _isProcessing;
        private set
        {
            if (!SetProperty(ref _isProcessing, value)) return;
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(ConnectionDetail));
        }
    }

    public bool CanSend => HasAgent && !IsProcessing;
    public bool HasConnection => _connection is not null;
    public string SelectedAgentName => _agent?.Name ?? "Agent";
    public string SelectedAgentInitial => string.IsNullOrWhiteSpace(_agent?.Name) ? "?" : _agent!.Name[..1].ToUpperInvariant();
    public string SessionShortId => _session is null ? "" : _session.Id[..Math.Min(8, _session.Id.Length)];
    public string ConnectionLabel => HasConnection ? "Connected" : "Disconnected";
    public string ConnectionDetail => IsProcessing ? "working" : "idle";

    private string _headerText = "Chat";
    public string HeaderText { get => _headerText; private set => SetProperty(ref _headerText, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

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

        var identity = IdentityStore.EnsureIdentity();
        _connection = new AgentConnectionService(_agent.Address, _agent.DirectUrl, identity, AppServices.Http);
        _connection.StreamEvent += e =>
            _dispatcher.TryEnqueue(() => AddEventBubble(e));
        _connection.ConnectionLost += ex =>
            _dispatcher.TryEnqueue(() => StatusText = $"Disconnected: {ex.Message}");
        _connection.OnboardRequired += () =>
            _dispatcher.TryEnqueue(AddOnboardingBubble);
        _connection.AskUserRequested += r =>
            _dispatcher.TryEnqueue(() => AskUserRequested?.Invoke(r));
        _connection.ApprovalRequested += r =>
            _dispatcher.TryEnqueue(() => ApprovalRequested?.Invoke(r));
        _connection.PlanReviewRequested += r =>
            _dispatcher.TryEnqueue(() => PlanReviewRequested?.Invoke(r));
        OnPropertyChanged(nameof(HasConnection));
        OnPropertyChanged(nameof(ConnectionLabel));
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

        var recent = _sessionsState.Sessions
            .Where(s => s.AgentId == agentId)
            .OrderByDescending(s => s.UpdatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
        if (recent is not null)
        {
            _sessionsState.ActiveSessionId = recent.Id;
            await AppServices.Sessions.SaveAsync(_sessionsState);
            return recent;
        }

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

        var json = await AppServices.Conversations.ReadEnvelopeAsync(_agent.Address, _session.Id);
        _createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (json is null) return;

        try
        {
            var envelope = JsonSerializer.Deserialize(json, AppJsonContext.Default.ChatEnvelope);
            if (envelope?.State.Messages is { } messages)
            {
                foreach (var message in messages) Messages.Add(message);
                if (envelope.State.CreatedAt > 0) _createdAt = envelope.State.CreatedAt;
            }
        }
        catch
        {
            // Corrupt blob: start clean rather than block the conversation.
        }
    }

    public async Task SendAsync(string text)
    {
        text = text?.Trim() ?? "";
        if (text.Length == 0 || _agent is null || _session is null || IsProcessing) return;

        Messages.Add(new ChatMessage { Id = NextId(), Role = ChatRole.User, Content = text });
        TouchSession(text);
        await PersistConversationAsync();

        IsProcessing = true;
        StatusText = "Working...";
        try
        {
            var reply = _connection is not null
                ? await _connection.SendInputAsync(text, _session.Id)
                : await SimulateReplyAsync(text);

            Messages.Add(new ChatMessage
            {
                Id = NextId(),
                Role = ChatRole.Agent,
                Content = reply,
                AgentName = _agent.Name,
            });
            await PersistConversationAsync();
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage
            {
                Id = NextId(),
                Role = ChatRole.Agent,
                Content = $"[connection error] {ex.Message}",
                AgentName = _agent.Name,
            });
            await PersistConversationAsync();
        }
        finally
        {
            IsProcessing = false;
            StatusText = "";
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

    public Task ReconnectAsync()
    {
        if (_agent is null) return Task.CompletedTask;

        StatusText = "Reconnecting...";
        SetupConnection();
        StatusText = HasConnection ? "" : "No agent endpoint configured";
        return Task.CompletedTask;
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

    private void AddEventBubble(AgentStreamEvent e)
    {
        var title = e.Type switch
        {
            "thinking" => "Thinking",
            "llm_call" => "Model call",
            "llm_result" => "Model response",
            "tool_call" => "Tool call",
            "tool_result" => "Tool result",
            "assistant" => "Assistant update",
            "ONBOARD_REQUIRED" => "Waiting for invite code",
            "ONBOARD_SUCCESS" => "Onboarding complete",
            _ => e.Type.Replace('_', ' '),
        };

        StatusText = e.Description;
        Messages.Add(new ChatMessage
        {
            Id = NextId(),
            Role = ChatRole.Event,
            EventTitle = title,
            EventDetail = e.Description,
        });
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

    private Task PersistConversationAsync()
    {
        if (_agent is null || _session is null) return Task.CompletedTask;

        var envelope = new ChatEnvelope
        {
            Version = 0,
            State = new ChatEnvelopeState
            {
                Messages = Messages.Where(message => !message.IsOnboarding && !message.IsEvent).ToList(),
                CreatedAt = _createdAt,
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            },
        };
        var json = JsonSerializer.Serialize(envelope, AppJsonContext.Default.ChatEnvelope);
        return AppServices.Conversations.WriteEnvelopeAsync(_agent.Address, _session.Id, json);
    }
}
