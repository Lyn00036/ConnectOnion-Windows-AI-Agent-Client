using System.Collections.ObjectModel;
using System;
using System.Globalization;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

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
/// Observable properties use <c>[ObservableProperty]</c> partial properties; dependent
/// computed properties are refreshed via <c>[NotifyPropertyChangedFor]</c>, and setter
/// side-effects run in the generated <c>partial void On*Changed</c> hooks. Non-null
/// defaults are seeded in the ctor because partial-property declarations can't carry one.
/// </summary>
public sealed partial class ChatMessage : Common.ObservableObject
{
    private bool? _interactiveExpandOverride;

    public ChatMessage()
    {
        Content = "";
        EventTitle = "";
        EventKind = "";
        EventEyebrow = "";
        OnboardInviteCode = "";
        IsActivityExpanded = true;
        FindQuery = "";
        ActiveFindMatchIndex = -1;
        AskUserFreeText = "";
        PlanFeedback = "";
        EditDraft = "";
        CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Attachments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAttachments));
            OnPropertyChanged(nameof(CanEdit));
        };
    }

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("role")]
    public ChatRole Role { get; set; }

    [ObservableProperty]
    [JsonPropertyName("content")]
    public partial string Content { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeLabel))]
    [JsonPropertyName("createdAt")]
    public partial long CreatedAtUnixMs { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUserActions))]
    [NotifyPropertyChangedFor(nameof(UserActionsOpacity))]
    [JsonIgnore]
    public partial bool AreUserActionsVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUserActions))]
    [NotifyPropertyChangedFor(nameof(UserActionsOpacity))]
    [JsonIgnore]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveEdit))]
    [JsonIgnore]
    public partial string EditDraft { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivityHeaderTitle))]
    [NotifyPropertyChangedFor(nameof(IsThinkingRunning))]
    [NotifyPropertyChangedFor(nameof(ActivityTimelineText))]
    [NotifyPropertyChangedFor(nameof(ActivityAccessibilityName))]
    [NotifyPropertyChangedFor(nameof(TurnUsageSummary))]
    [JsonPropertyName("eventTitle")]
    public partial string EventTitle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEventDetail))]
    [NotifyPropertyChangedFor(nameof(ActivityTimelineText))]
    [JsonPropertyName("eventDetail")]
    public partial string? EventDetail { get; set; }

    /// <summary>Which event template renders this bubble: "activity", "tool", "ask_user", "approval", "plan_review" (empty = not an event).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActivityEvent))]
    [NotifyPropertyChangedFor(nameof(IsToolEvent))]
    [NotifyPropertyChangedFor(nameof(IsToolActivityEvent))]
    [NotifyPropertyChangedFor(nameof(IsAskUserEvent))]
    [NotifyPropertyChangedFor(nameof(IsApprovalEvent))]
    [NotifyPropertyChangedFor(nameof(IsPlanReviewEvent))]
    [NotifyPropertyChangedFor(nameof(IsInteractiveEvent))]
    [NotifyPropertyChangedFor(nameof(IsInteractiveExpanded))]
    [NotifyPropertyChangedFor(nameof(IsInteractiveCollapsed))]
    [JsonPropertyName("eventKind")]
    public partial string EventKind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBadge))]
    [NotifyPropertyChangedFor(nameof(IsAwaitingResponse))]
    [NotifyPropertyChangedFor(nameof(ShowAskUserFreeText))]
    [NotifyPropertyChangedFor(nameof(IsInteractiveExpanded))]
    [NotifyPropertyChangedFor(nameof(IsInteractiveCollapsed))]
    [NotifyPropertyChangedFor(nameof(InteractiveOutcome))]
    [NotifyPropertyChangedFor(nameof(InteractiveAccessibilityName))]
    [NotifyPropertyChangedFor(nameof(ActivityStatusGlyph))]
    [NotifyPropertyChangedFor(nameof(ActivityStatusLabel))]
    [NotifyPropertyChangedFor(nameof(ActivityHeaderTitle))]
    [NotifyPropertyChangedFor(nameof(IsThinkingRunning))]
    [NotifyPropertyChangedFor(nameof(ActivityAccessibilityName))]
    [JsonPropertyName("eventStatus")]
    public partial EventStatus Status { get; set; }

    // Answering an interactive turn folds the card away again. Dropping any manual toggle the user
    // made while it was pending is deliberate: the default for a resolved card is collapsed, and
    // they can always re-open it. Runs before the [NotifyPropertyChangedFor] notifications above,
    // so IsInteractiveExpanded re-reads the cleared override.
    partial void OnStatusChanged(EventStatus value) => _interactiveExpandOverride = null;

    // A thought process is supporting context, so it starts folded whether it was just streamed
    // or reconstructed from persistence. Handle both property assignment orders used by object
    // initializers/deserializers: either EventKind or EventTitle may arrive first.
    partial void OnEventTitleChanged(string value)
    {
        if (EventKind == "activity"
            && string.Equals(value, "Thinking", StringComparison.OrdinalIgnoreCase))
        {
            IsActivityExpanded = false;
        }
    }

    partial void OnEventKindChanged(string value)
    {
        if (value == "activity"
            && string.Equals(EventTitle, "Thinking", StringComparison.OrdinalIgnoreCase))
        {
            IsActivityExpanded = false;
        }
    }

    /// <summary>Small uppercase label shown before the title on tool cards.</summary>
    [ObservableProperty]
    [JsonPropertyName("eventEyebrow")]
    public partial string EventEyebrow { get; set; }

    /// <summary>Right-aligned meta (timing, duration, context sizes).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEventMeta))]
    [NotifyPropertyChangedFor(nameof(HasActivityTimelineMeta))]
    [NotifyPropertyChangedFor(nameof(TurnUsageSummary))]
    [NotifyPropertyChangedFor(nameof(InteractiveOutcome))]
    [NotifyPropertyChangedFor(nameof(InteractiveAccessibilityName))]
    [JsonPropertyName("eventMeta")]
    public partial string? EventMeta { get; set; }

    /// <summary>Pretty-printed tool arguments (collapsible).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEventArgs))]
    [JsonPropertyName("eventArgs")]
    public partial string? EventArgs { get; set; }

    /// <summary>Tool result / blocked command (collapsible).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEventResult))]
    [JsonPropertyName("eventResult")]
    public partial string? EventResult { get; set; }

    /// <summary>Server-side correlation id, used to merge running/finished events. Transient.</summary>
    [JsonIgnore]
    public string? EventKey { get; set; }

    /// <summary>One compact, durable timeline for every tool call in an assistant turn.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToolActivityEvent))]
    [JsonIgnore]
    public partial ToolActivityViewModel? ToolActivity { get; set; }

    [JsonPropertyName("agentName")]
    public string? AgentName { get; set; }

    [JsonPropertyName("isOnboarding")]
    public bool IsOnboarding { get; set; }

    [ObservableProperty]
    [JsonIgnore]
    public partial string OnboardInviteCode { get; set; }

    [JsonIgnore]
    public bool IsOnboardingInputEnabled => IsOnboarding && !IsOnboardingSubmitted;

    [JsonIgnore]
    public bool IsUser => Role == ChatRole.User;

    [JsonIgnore]
    public bool IsAgent => Role == ChatRole.Agent;

    [JsonIgnore]
    public bool IsEvent => Role == ChatRole.Event;

    [JsonIgnore]
    public string TimeLabel => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUnixMs)
        .ToLocalTime()
        .ToString("h:mm tt", CultureInfo.CurrentCulture);

    [JsonIgnore]
    public bool CanEdit => IsUser && !HasAttachments && !string.IsNullOrWhiteSpace(Content);

    [JsonIgnore]
    public bool CanSaveEdit => !string.IsNullOrWhiteSpace(EditDraft);

    [JsonIgnore]
    public bool ShowUserActions => AreUserActionsVisible && !IsEditing;

    [JsonIgnore]
    public double UserActionsOpacity => ShowUserActions ? 1d : 0d;

    public void BeginEditing()
    {
        if (!CanEdit) return;
        EditDraft = Content;
        AreUserActionsVisible = false;
        IsEditing = true;
    }

    public void CancelEditing()
    {
        IsEditing = false;
        EditDraft = "";
    }

    /// <summary>Compact card with a colored status dot (thinking, intent, eval, compact, …).</summary>
    [JsonIgnore]
    public bool IsActivityEvent => EventKind == "activity";

    [JsonIgnore]
    public bool IsTurnUsageEvent => IsActivityEvent && EventKey == "turn_usage";

    [JsonIgnore]
    public string TurnUsageSummary => string.IsNullOrWhiteSpace(EventMeta)
        ? EventTitle
        : $"{EventTitle} · {EventMeta}";

    [JsonIgnore]
    public bool IsThinkingEvent => IsActivityEvent
        && string.Equals(EventTitle, "Thinking", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsThinkingRunning => IsThinkingEvent && Status == EventStatus.Running;

    [JsonIgnore]
    public string ActivityHeaderTitle => IsThinkingEvent
        ? IsThinkingRunning ? "Thinking..." : "Thought process"
        : IsTurnUsageEvent ? "Turn usage" : "Activity";

    [JsonIgnore]
    public string ActivityTimelineText => IsThinkingEvent
        ? string.IsNullOrWhiteSpace(EventDetail) ? EventTitle : EventDetail
        : IsTurnUsageEvent
            ? string.IsNullOrWhiteSpace(EventTitle) ? "Model" : EventTitle
            : string.IsNullOrWhiteSpace(EventDetail)
                ? EventTitle
                : $"{EventTitle} — {EventDetail}";

    [JsonIgnore]
    public bool HasActivityTimelineMeta => !string.IsNullOrWhiteSpace(EventMeta);

    [JsonIgnore]
    public string ActivityStatusGlyph => Status switch
    {
        EventStatus.Running => "◌",
        EventStatus.Error => "✕",
        _ => "✓",
    };

    [JsonIgnore]
    public string ActivityStatusLabel => Status switch
    {
        EventStatus.Running => "Working...",
        EventStatus.Error => "Failed",
        _ => "Done",
    };

    [JsonIgnore]
    public string ActivityAccessibilityName
        => $"{ActivityHeaderTitle}. {ActivityStatusLabel}. {(IsActivityExpanded ? "Collapse" : "Expand")}";

    public void ToggleActivityExpanded() => IsActivityExpanded = !IsActivityExpanded;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnboardingInputEnabled))]
    [JsonIgnore]
    public partial bool IsOnboardingSubmitted { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivityAccessibilityName))]
    [JsonIgnore]
    public partial bool IsActivityExpanded { get; set; }

    [ObservableProperty]
    [JsonIgnore]
    public partial string FindQuery { get; set; }

    [ObservableProperty]
    [JsonIgnore]
    public partial int ActiveFindMatchIndex { get; set; }

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

    [ObservableProperty]
    [JsonIgnore]
    public partial string? SelectedAskUserOption { get; set; }

    [ObservableProperty]
    [JsonIgnore]
    public partial string AskUserFreeText { get; set; }

    /// <summary>Optional feedback typed into a plan_review turn before approving or requesting changes.</summary>
    [ObservableProperty]
    [JsonIgnore]
    public partial string PlanFeedback { get; set; }

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
}
