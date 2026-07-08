using System.Collections.ObjectModel;
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
    private const int CollapseThreshold = 600;
    private const int CollapsedMaxLines = 12;

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
    private bool _isContentExpanded;
    private string _findQuery = "";
    private int _activeFindMatchIndex = -1;

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("role")]
    public ChatRole Role { get; set; }

    [JsonPropertyName("content")]
    public string Content
    {
        get => _content;
        set
        {
            if (!SetProperty(ref _content, value)) return;
            OnPropertyChanged(nameof(IsCollapsible));
            OnPropertyChanged(nameof(IsContentCollapsed));
            OnPropertyChanged(nameof(CollapseButtonText));
            OnPropertyChanged(nameof(RenderedMaxLines));
        }
    }

    [JsonPropertyName("eventTitle")]
    public string EventTitle { get => _eventTitle; set => SetProperty(ref _eventTitle, value); }

    [JsonPropertyName("eventDetail")]
    public string? EventDetail
    {
        get => _eventDetail;
        set { if (SetProperty(ref _eventDetail, value)) OnPropertyChanged(nameof(HasEventDetail)); }
    }

    /// <summary>Which event template renders this bubble: "activity", "tool", "ask_user", "approval", "plan_review" (empty = not an event).</summary>
    [JsonPropertyName("eventKind")]
    public string EventKind
    {
        get => _eventKind;
        set
        {
            if (!SetProperty(ref _eventKind, value)) return;
            OnPropertyChanged(nameof(IsActivityEvent));
            OnPropertyChanged(nameof(IsToolEvent));
            OnPropertyChanged(nameof(IsAskUserEvent));
            OnPropertyChanged(nameof(IsApprovalEvent));
            OnPropertyChanged(nameof(IsPlanReviewEvent));
        }
    }

    [JsonPropertyName("eventStatus")]
    public EventStatus Status
    {
        get => _status;
        set
        {
            if (!SetProperty(ref _status, value)) return;
            OnPropertyChanged(nameof(StatusBadge));
            OnPropertyChanged(nameof(IsAwaitingResponse));
        }
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

    [JsonIgnore]
    public bool IsCollapsible => IsAgent && !string.IsNullOrWhiteSpace(Content) && Content.Length > CollapseThreshold;

    [JsonIgnore]
    public bool IsContentCollapsed => IsCollapsible && !_isContentExpanded;

    [JsonIgnore]
    public string CollapseButtonText => IsContentCollapsed ? "Expand" : "Collapse";

    [JsonIgnore]
    public int RenderedMaxLines => IsContentCollapsed ? CollapsedMaxLines : 0;

    /// <summary>Compact card with a colored status dot (thinking, intent, eval, compact, …).</summary>
    [JsonIgnore]
    public bool IsActivityEvent => EventKind == "activity";

    /// <summary>Tool card with a status badge and collapsible arguments/result.</summary>
    [JsonIgnore]
    public bool IsToolEvent => EventKind == "tool";

    /// <summary>An <c>ask_user</c> turn: question + inline answer controls.</summary>
    [JsonIgnore]
    public bool IsAskUserEvent => EventKind == "ask_user";

    /// <summary>An <c>approval_needed</c> turn: tool + arguments + approve/reject buttons.</summary>
    [JsonIgnore]
    public bool IsApprovalEvent => EventKind == "approval";

    /// <summary>A <c>plan_review</c> turn: plan content + approve/request-changes buttons.</summary>
    [JsonIgnore]
    public bool IsPlanReviewEvent => EventKind == "plan_review";

    /// <summary>
    /// True while an ask_user/approval/plan_review turn is still waiting on the
    /// user (reuses <see cref="EventStatus.Running"/> the same way tool cards do
    /// for "in progress" — different EventKind values, so no semantic clash).
    /// Once answered the bubble shows its resolved <see cref="EventMeta"/> instead
    /// of the interactive controls.
    /// </summary>
    [JsonIgnore]
    public bool IsAwaitingResponse => Status == EventStatus.Running;

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

    [JsonIgnore]
    public bool IsContentExpanded
    {
        get => _isContentExpanded;
        set
        {
            if (!SetProperty(ref _isContentExpanded, value)) return;
            OnPropertyChanged(nameof(IsContentCollapsed));
            OnPropertyChanged(nameof(CollapseButtonText));
            OnPropertyChanged(nameof(RenderedMaxLines));
        }
    }

    [JsonIgnore]
    public string FindQuery
    {
        get => _findQuery;
        set => SetProperty(ref _findQuery, value);
    }

    [JsonIgnore]
    public int ActiveFindMatchIndex
    {
        get => _activeFindMatchIndex;
        set => SetProperty(ref _activeFindMatchIndex, value);
    }

    /// <summary>
    /// Attachments sent by the user on this message, or images the agent produced
    /// via <c>agent_image</c> and merged onto this bubble. Empty for plain text
    /// and for event/activity/tool bubbles.
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<ChatAttachment> Attachments { get; } = new();

    [JsonIgnore]
    public bool HasAttachments => Attachments.Count > 0;

    // ---- Interactive turn state (ask_user / approval / plan_review) ----
    // None of this is persisted to SQLite (ConversationRepository only maps the
    // generic Event* columns above) — it's only meaningful while a live
    // WebSocket connection can still receive the response. Reloaded history
    // shows the resolved snapshot (EventTitle/EventDetail/EventArgs/EventMeta/
    // Status) with no live controls, which is correct: the server-side
    // io.receive() wait that this answers no longer exists after a restart.

    /// <summary>Single-select options for an ask_user turn (bound to inline RadioButtons).</summary>
    [JsonIgnore]
    public ObservableCollection<string> AskUserOptions { get; } = new();

    /// <summary>Multi-select options for an ask_user turn (bound to inline CheckBoxes).</summary>
    [JsonIgnore]
    public ObservableCollection<AskUserOptionEntry> AskUserOptionEntries { get; } = new();

    /// <summary>Dynamic named fields for an ask_user turn (bound to inline TextBoxes).</summary>
    [JsonIgnore]
    public ObservableCollection<AskUserFieldEntry> AskUserFields { get; } = new();

    [JsonIgnore]
    public bool AskUserMultiSelect { get; set; }

    private string? _selectedAskUserOption;
    [JsonIgnore]
    public string? SelectedAskUserOption { get => _selectedAskUserOption; set => SetProperty(ref _selectedAskUserOption, value); }

    private string _askUserFreeText = "";
    [JsonIgnore]
    public string AskUserFreeText { get => _askUserFreeText; set => SetProperty(ref _askUserFreeText, value); }

    private string _planFeedback = "";
    /// <summary>Optional feedback typed into a plan_review turn before approving or requesting changes.</summary>
    [JsonIgnore]
    public string PlanFeedback { get => _planFeedback; set => SetProperty(ref _planFeedback, value); }

    [JsonIgnore]
    public bool HasAskUserFields => AskUserFields.Count > 0;

    [JsonIgnore]
    public bool HasAskUserOptions => !AskUserMultiSelect && AskUserOptions.Count > 0;

    [JsonIgnore]
    public bool HasAskUserMultiOptions => AskUserMultiSelect && AskUserOptionEntries.Count > 0;

    public ChatMessage()
    {
        Attachments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAttachments));
    }
}
