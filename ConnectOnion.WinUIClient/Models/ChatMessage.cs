using System.Text.Json.Serialization;
using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Models;

public enum ChatRole
{
    User,
    Agent,
    Event,
}

/// <summary>Lifecycle of an event bubble, mirrored from the SDK's ChatItem status.</summary>
public enum EventStatus
{
    Running,
    Done,
    Error,
}

/// <summary>
/// A single rendered chat bubble. Mirrors the TypeScript <c>ChatMessage</c> type.
/// </summary>
public sealed class ChatMessage : ObservableObject
{
    private string _onboardInviteCode = "";
    private string _content = "";
    private string _eventTitle = "";
    private string? _eventDetail;
    private bool _isOnboardingSubmitted;
    private string _eventKind = "";
    private EventStatus _status;
    private string _eventEyebrow = "";
    private string? _eventMeta;
    private string? _eventArgs;
    private string? _eventResult;

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("role")]
    public ChatRole Role { get; set; }

    [JsonPropertyName("content")]
    public string Content { get => _content; set => SetProperty(ref _content, value); }

    [JsonPropertyName("eventTitle")]
    public string EventTitle { get => _eventTitle; set => SetProperty(ref _eventTitle, value); }

    [JsonPropertyName("eventDetail")]
    public string? EventDetail
    {
        get => _eventDetail;
        set { if (SetProperty(ref _eventDetail, value)) OnPropertyChanged(nameof(HasEventDetail)); }
    }

    /// <summary>Which event template renders this bubble: "activity" or "tool" (empty = not an event).</summary>
    [JsonPropertyName("eventKind")]
    public string EventKind
    {
        get => _eventKind;
        set
        {
            if (!SetProperty(ref _eventKind, value)) return;
            OnPropertyChanged(nameof(IsActivityEvent));
            OnPropertyChanged(nameof(IsToolEvent));
        }
    }

    [JsonPropertyName("eventStatus")]
    public EventStatus Status
    {
        get => _status;
        set { if (SetProperty(ref _status, value)) OnPropertyChanged(nameof(StatusBadge)); }
    }

    /// <summary>Small uppercase label shown before the title on tool cards.</summary>
    [JsonPropertyName("eventEyebrow")]
    public string EventEyebrow { get => _eventEyebrow; set => SetProperty(ref _eventEyebrow, value); }

    /// <summary>Right-aligned meta (timing, duration, context sizes).</summary>
    [JsonPropertyName("eventMeta")]
    public string? EventMeta
    {
        get => _eventMeta;
        set { if (SetProperty(ref _eventMeta, value)) OnPropertyChanged(nameof(HasEventMeta)); }
    }

    /// <summary>Pretty-printed tool arguments (collapsible).</summary>
    [JsonPropertyName("eventArgs")]
    public string? EventArgs
    {
        get => _eventArgs;
        set { if (SetProperty(ref _eventArgs, value)) OnPropertyChanged(nameof(HasEventArgs)); }
    }

    /// <summary>Tool result / blocked command (collapsible).</summary>
    [JsonPropertyName("eventResult")]
    public string? EventResult
    {
        get => _eventResult;
        set { if (SetProperty(ref _eventResult, value)) OnPropertyChanged(nameof(HasEventResult)); }
    }

    /// <summary>Server-side correlation id, used to merge running/finished events. Transient.</summary>
    [JsonIgnore]
    public string? EventKey { get; set; }

    [JsonPropertyName("agentName")]
    public string? AgentName { get; set; }

    [JsonPropertyName("isOnboarding")]
    public bool IsOnboarding { get; set; }

    [JsonIgnore]
    public string OnboardInviteCode
    {
        get => _onboardInviteCode;
        set => SetProperty(ref _onboardInviteCode, value);
    }

    [JsonIgnore]
    public bool IsOnboardingInputEnabled => IsOnboarding && !IsOnboardingSubmitted;

    [JsonIgnore]
    public bool IsUser => Role == ChatRole.User;

    [JsonIgnore]
    public bool IsAgent => Role == ChatRole.Agent;

    [JsonIgnore]
    public bool IsEvent => Role == ChatRole.Event;

    /// <summary>Compact card with a colored status dot (thinking, intent, eval, compact, …).</summary>
    [JsonIgnore]
    public bool IsActivityEvent => EventKind == "activity";

    /// <summary>Tool card with a status badge and collapsible arguments/result.</summary>
    [JsonIgnore]
    public bool IsToolEvent => EventKind == "tool";

    [JsonIgnore]
    public bool HasEventDetail => !string.IsNullOrEmpty(EventDetail);

    [JsonIgnore]
    public bool HasEventMeta => !string.IsNullOrEmpty(EventMeta);

    [JsonIgnore]
    public bool HasEventArgs => !string.IsNullOrEmpty(EventArgs);

    [JsonIgnore]
    public bool HasEventResult => !string.IsNullOrEmpty(EventResult);

    /// <summary>Lower-case status word shown on tool badges.</summary>
    [JsonIgnore]
    public string StatusBadge => Status switch
    {
        EventStatus.Running => "running",
        EventStatus.Error => "error",
        _ => "done",
    };

    [JsonIgnore]
    public bool IsOnboardingSubmitted
    {
        get => _isOnboardingSubmitted;
        set
        {
            if (!SetProperty(ref _isOnboardingSubmitted, value)) return;
            OnPropertyChanged(nameof(IsOnboardingInputEnabled));
        }
    }
}
