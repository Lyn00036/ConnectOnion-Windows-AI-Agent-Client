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
    private bool _isContentExpanded = true;
    private bool? _interactiveExpandOverride;
    private string _findQuery = "";
    private int _activeFindMatchIndex = -1;
    private ToolActivityViewModel? _toolActivity;

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
            OnPropertyChanged(nameof(IsToolActivityEvent));
            OnPropertyChanged(nameof(IsAskUserEvent));
            OnPropertyChanged(nameof(IsApprovalEvent));
            OnPropertyChanged(nameof(IsPlanReviewEvent));
            OnPropertyChanged(nameof(IsInteractiveEvent));
            OnPropertyChanged(nameof(IsInteractiveExpanded));
            OnPropertyChanged(nameof(IsInteractiveCollapsed));
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
            OnPropertyChanged(nameof(ShowAskUserFreeText));

            // Answering an interactive turn folds the card away again. Dropping any manual
            // toggle the user made while it was pending is deliberate: the default for a
            // resolved card is collapsed, and they can always re-open it.
            _interactiveExpandOverride = null;
            OnPropertyChanged(nameof(IsInteractiveExpanded));
            OnPropertyChanged(nameof(IsInteractiveCollapsed));
            OnPropertyChanged(nameof(InteractiveOutcome));
            OnPropertyChanged(nameof(InteractiveAccessibilityName));
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
        set
        {
            if (!SetProperty(ref _eventMeta, value)) return;
            OnPropertyChanged(nameof(HasEventMeta));
            OnPropertyChanged(nameof(InteractiveOutcome));
            OnPropertyChanged(nameof(InteractiveAccessibilityName));
        }
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

    /// <summary>One compact, durable timeline for every tool call in an assistant turn.</summary>
    [JsonIgnore]
    public ToolActivityViewModel? ToolActivity
    {
        get => _toolActivity;
        set
        {
            if (!SetProperty(ref _toolActivity, value)) return;
            OnPropertyChanged(nameof(IsToolActivityEvent));
        }
    }

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

    [JsonIgnore]
    public bool IsToolActivityEvent => EventKind == "tool_activity" && ToolActivity is not null;

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

    /// <summary>Any of the three cards that can be answered inline.</summary>
    [JsonIgnore]
    public bool IsInteractiveEvent => IsAskUserEvent || IsApprovalEvent || IsPlanReviewEvent;

    /// <summary>
    /// Collapse state of an interactive card, modelled on the tool-activity card. It is derived,
    /// not stored: a card is open exactly while it is still waiting on the user, and folds itself
    /// away once answered — which also means history reloaded from SQLite (every card there is
    /// resolved) comes back collapsed. <see cref="ToggleInteractiveExpanded"/> sets the override
    /// so either state can be opened or folded by hand; the override is dropped whenever
    /// <see cref="Status"/> changes, so answering always re-collapses.
    /// </summary>
    [JsonIgnore]
    public bool IsInteractiveExpanded => _interactiveExpandOverride ?? IsAwaitingResponse;

    [JsonIgnore]
    public bool IsInteractiveCollapsed => !IsInteractiveExpanded;

    /// <summary>The one-line result shown on a collapsed card: the recorded answer ("Approved
    /// once", "Rejected", "Changes requested", "Answered: …"), <c>"No selection"</c> for a card
    /// a run sealed unanswered, or the pending state while it still awaits the user.</summary>
    [JsonIgnore]
    public string InteractiveOutcome => IsAwaitingResponse
        ? "Waiting for you"
        : string.IsNullOrWhiteSpace(EventMeta) ? "No selection" : EventMeta;

    /// <summary>Spoken name for the collapse toggle, so the state is not conveyed by chevron alone.</summary>
    [JsonIgnore]
    public string InteractiveAccessibilityName
        => $"{EventEyebrow}: {EventTitle}. {InteractiveOutcome}. {(IsInteractiveExpanded ? "Collapse" : "Expand")}";

    public void ToggleInteractiveExpanded()
    {
        _interactiveExpandOverride = !IsInteractiveExpanded;
        OnPropertyChanged(nameof(IsInteractiveExpanded));
        OnPropertyChanged(nameof(IsInteractiveCollapsed));
        OnPropertyChanged(nameof(InteractiveAccessibilityName));
    }

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

    /// <summary>The free-text answer box only makes sense while still awaiting a response and
    /// when the turn has no structured fields. Gating on <see cref="IsAwaitingResponse"/> too
    /// keeps an empty box from reappearing on a resolved bubble reloaded from history (whose
    /// options/fields aren't persisted, so <see cref="HasAskUserFields"/> reads false).</summary>
    [JsonIgnore]
    public bool ShowAskUserFreeText => IsAwaitingResponse && !HasAskUserFields;

    public ChatMessage()
    {
        Attachments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAttachments));
    }
}
