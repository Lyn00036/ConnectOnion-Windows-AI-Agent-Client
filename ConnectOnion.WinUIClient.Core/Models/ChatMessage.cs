using System.Collections.ObjectModel;
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
    private bool _approvalExplainOpen;

    public ChatMessage()
    {
        Content = "";
        EventTitle = "";
        EventKind = "";
        EventEyebrow = "";
        OnboardInviteCode = "";
        FindQuery = "";
        ActiveFindMatchIndex = -1;
        MessageFontSize = 14;
        AskUserFreeText = "";
        ThinkingElapsedLabel = "";
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
    [NotifyPropertyChangedFor(nameof(TurnUsagePrimary))]
    [NotifyPropertyChangedFor(nameof(ApprovalTrustLabel))]
    [NotifyPropertyChangedFor(nameof(ApprovalTrustAccessibilityName))]
    [JsonPropertyName("eventTitle")]
    public partial string EventTitle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEventDetail))]
    [NotifyPropertyChangedFor(nameof(ActivityTimelineText))]
    [NotifyPropertyChangedFor(nameof(ActivityAccessibilityName))]
    [NotifyPropertyChangedFor(nameof(HasThoughtText))]
    [NotifyPropertyChangedFor(nameof(IsThinkingSingleThought))]
    [NotifyPropertyChangedFor(nameof(IsPlainSingleActivity))]
    [NotifyPropertyChangedFor(nameof(ApprovalAllowOnceAccessibilityName))]
    [JsonPropertyName("eventDetail")]
    public partial string? EventDetail { get; set; }

    /// <summary>Which event template renders this bubble: "activity", "tool", "ask_user", "approval", "plan_review", "diff_preview" (empty = not an event).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActivityEvent))]
    [NotifyPropertyChangedFor(nameof(IsDiffPreviewEvent))]
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
    [NotifyPropertyChangedFor(nameof(ShowAskUserOptions))]
    [NotifyPropertyChangedFor(nameof(ShowAskUserCustomAnswer))]
    [NotifyPropertyChangedFor(nameof(IsInteractiveExpanded))]
    [NotifyPropertyChangedFor(nameof(IsInteractiveCollapsed))]
    [NotifyPropertyChangedFor(nameof(InteractiveOutcome))]
    [NotifyPropertyChangedFor(nameof(InteractiveStatusLabel))]
    [NotifyPropertyChangedFor(nameof(InteractiveAccessibilityName))]
    [NotifyPropertyChangedFor(nameof(IsActivityFailed))]
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

    /// <summary>Small uppercase label shown before the title on tool cards.</summary>
    [ObservableProperty]
    [JsonPropertyName("eventEyebrow")]
    public partial string EventEyebrow { get; set; }

    /// <summary>Right-aligned meta (timing, duration, context sizes).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEventMeta))]
    [NotifyPropertyChangedFor(nameof(HasActivityTimelineMeta))]
    [NotifyPropertyChangedFor(nameof(TurnUsageSummary))]
    [NotifyPropertyChangedFor(nameof(TurnUsagePrimary))]
    [NotifyPropertyChangedFor(nameof(TurnUsageSecondary))]
    [NotifyPropertyChangedFor(nameof(HasTurnUsageSecondary))]
    [NotifyPropertyChangedFor(nameof(InteractiveOutcome))]
    [NotifyPropertyChangedFor(nameof(InteractiveStatusLabel))]
    [NotifyPropertyChangedFor(nameof(InteractiveAnswerText))]
    [NotifyPropertyChangedFor(nameof(HasInteractiveAnswer))]
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

    // --- Two-line split of the turn-usage row (display only) ---
    // The metadata reads as "model · duration" on top and "tokens · context · tools" beneath, so
    // the model and time — the two facts a reader scans for — aren't buried in a dense single line.
    // This is a purely presentational split of the already-built EventMeta (whose segments the
    // projection always orders duration-first): EventMeta itself, TurnUsageSummary and the
    // accessibility name are unchanged, so persistence and screen-reader output are untouched.

    private string[] TurnUsageSegments()
        => string.IsNullOrWhiteSpace(EventMeta)
            ? System.Array.Empty<string>()
            : EventMeta.Split(" · ");

    /// <summary>First line: the model, plus the leading metadata segment (the turn duration when
    /// the agent reported one).</summary>
    [JsonIgnore]
    public string TurnUsagePrimary
    {
        get
        {
            var model = string.IsNullOrWhiteSpace(EventTitle) ? "Turn usage" : EventTitle;
            var segments = TurnUsageSegments();
            return segments.Length > 0 ? $"{model} · {segments[0]}" : model;
        }
    }

    /// <summary>Second line: the remaining metadata segments (tokens, context, tool count), empty
    /// when there is only the model and one segment to show.</summary>
    [JsonIgnore]
    public string TurnUsageSecondary
    {
        get
        {
            var segments = TurnUsageSegments();
            return segments.Length > 1 ? string.Join(" · ", segments[1..]) : "";
        }
    }

    [JsonIgnore]
    public bool HasTurnUsageSecondary => TurnUsageSecondary.Length > 0;

    [JsonIgnore]
    public bool IsThinkingEvent => IsActivityEvent
        && string.Equals(EventTitle, "Thinking", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsThinkingRunning => IsThinkingEvent && Status == EventStatus.Running;

    [JsonIgnore]
    public string ActivityHeaderTitle => IsThinkingEvent
        ? IsThinkingRunning ? "Thinking..." : "Thought process"
        : IsTurnUsageEvent ? "Turn usage" : "Activity";

    /// <remarks>
    /// A thinking card falls back to <c>""</c>, never to <see cref="EventTitle"/>. The card an
    /// <c>llm_call</c> opens has no reasoning text at all — the host sends none — and EventTitle
    /// there is the internal marker "Thinking", so falling back to it printed the literal word
    /// "Thinking" as though the agent had thought it, and left that line sitting under the card
    /// after the call finished. An empty string pairs with <see cref="HasThoughtText"/>, which is
    /// what keeps the body out of the tree entirely in that case.
    /// </remarks>
    [JsonIgnore]
    public string ActivityTimelineText => IsThinkingEvent
        ? EventDetail ?? ""
        : IsTurnUsageEvent
            ? string.IsNullOrWhiteSpace(EventTitle) ? "Model" : EventTitle
            : string.IsNullOrWhiteSpace(EventDetail)
                ? EventTitle
                : $"{EventTitle} — {EventDetail}";

    [JsonIgnore]
    public bool HasActivityTimelineMeta => !string.IsNullOrWhiteSpace(EventMeta);

    /// <summary>
    /// The consecutive thoughts this card groups. The projection folds a run of <c>thinking</c>
    /// frames into one card, so a card carries one step or many; <see cref="HasMultipleThoughts"/>
    /// is what the template switches on. Non-thinking activities leave it empty and render from
    /// <see cref="ActivityTimelineText"/>.
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<string> ThoughtSteps { get; } = new();

    /// <summary>
    /// A lone thought is shown inline — folding a single line away behind a disclosure header
    /// costs a click to read one sentence and wraps it in more scaffold than content. A run of
    /// them is a different problem: several paragraphs of the agent's reasoning would bury the
    /// answer, so those collapse into one summarized card the user opens on purpose.
    /// </summary>
    [JsonIgnore]
    public bool HasMultipleThoughts => IsThinkingEvent && ThoughtSteps.Count > 1;

    [JsonIgnore]
    public bool IsSingleActivityEntry => !HasMultipleThoughts;

    /// <summary>
    /// Seconds elapsed since the model started working ("3s"), ticked once a second while the row
    /// is live and blank otherwise. It is a plain settable property rather than something computed
    /// from a start time because nothing re-reads a computed property on a schedule — the value
    /// has to be pushed for the UI to move. <see cref="ChatViewModel"/> owns the timer that
    /// pushes it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThinkingElapsed))]
    [JsonIgnore]
    public partial string ThinkingElapsedLabel { get; set; }

    [JsonIgnore]
    public bool HasThinkingElapsed => !string.IsNullOrEmpty(ThinkingElapsedLabel);

    /// <summary>Whether this card actually holds reasoning text. False for the bare indicator an
    /// <c>llm_call</c> opens, which reports only that the model is working.</summary>
    [JsonIgnore]
    public bool HasThoughtText => !string.IsNullOrWhiteSpace(ActivityTimelineText);

    /// <summary>One thought, rendered as an italic aside behind a grey rule.</summary>
    [JsonIgnore]
    public bool IsThinkingSingleThought => IsThinkingEvent && IsSingleActivityEntry && HasThoughtText;

    /// <summary>A single non-thinking activity (intent/eval/compact): plain text, no rule — it
    /// states a fact about the run rather than quoting the agent.</summary>
    [JsonIgnore]
    public bool IsPlainSingleActivity => !IsThinkingEvent && IsSingleActivityEntry && HasThoughtText;

    /// <summary>Trailing summary on a grouped card's header, sitting beside the title exactly as
    /// <c>ToolActivityViewModel.Summary</c> does. The step count is the whole reason the card is
    /// folded, so it is what the collapsed row advertises.</summary>
    [JsonIgnore]
    public string ThoughtStepsSummary => $"{ThoughtSteps.Count} steps";

    /// <summary>
    /// The only status worth its own line is failure: a finished activity is evidenced by its own
    /// content, and a running one by the spinner.
    /// </summary>
    [JsonIgnore]
    public bool IsActivityFailed => Status == EventStatus.Error;

    [JsonIgnore]
    public string ActivityAccessibilityName => HasMultipleThoughts
        ? $"{ActivityHeaderTitle}. {ThoughtStepsSummary}. {(IsActivityExpanded ? "Collapse" : "Expand")}"
        : $"{ActivityHeaderTitle}. {ActivityTimelineText}";

    public void ToggleActivityExpanded() => IsActivityExpanded = !IsActivityExpanded;

    /// <summary>Appends a thought to this card and keeps <see cref="EventDetail"/> in step, so the
    /// single-step rendering and the persisted row stay driven by one value.</summary>
    public void AddThought(string? content)
    {
        ThoughtSteps.Add(content ?? "");
        if (ThoughtSteps.Count == 1) EventDetail = content;
        OnPropertyChanged(nameof(ThoughtSteps));
        OnPropertyChanged(nameof(HasMultipleThoughts));
        OnPropertyChanged(nameof(IsSingleActivityEntry));
        OnPropertyChanged(nameof(HasThoughtText));
        OnPropertyChanged(nameof(IsThinkingSingleThought));
        OnPropertyChanged(nameof(IsPlainSingleActivity));
        OnPropertyChanged(nameof(ThoughtStepsSummary));
        OnPropertyChanged(nameof(ActivityAccessibilityName));
    }

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

    /// <summary>A read-only proposed file change. Not an interactive event: DiffWriter sends
    /// diff_preview for display only and asks for approval through a separate ask_user, so this
    /// card never carries buttons. See the diff_preview arm of ChatTurnProjection.Events.</summary>
    public bool IsDiffPreviewEvent => EventKind == "diff_preview";

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
    /// once", "Rejected", "Changes requested", "Answered: …"), <c>"Skipped"</c> for a card a run
    /// ended without an answer to, or the pending state while it still awaits the user.</summary>
    [JsonIgnore]
    public string InteractiveOutcome => IsAwaitingResponse
        ? "Waiting for you"
        : string.IsNullOrWhiteSpace(EventMeta) ? "Skipped" : EventMeta;

    /// <summary>The status chip on an interactive card's header: a short *status word only*
    /// ("Pending" / "Answered" / "Approved once" / "Rejected" / "Skipped"), never the full answer.
    /// The chosen answer itself is shown once in the card body (see <see cref="InteractiveAnswerText"/>),
    /// so the badge stripping everything after the first ":" ("Answered: delete test.txt" → "Answered")
    /// removes the answer-in-two-places duplication. Sentence case, not SHOUTING CAPS; the same
    /// string is what a screen reader gets.</summary>
    [JsonIgnore]
    public string InteractiveStatusLabel
    {
        get
        {
            if (IsAwaitingResponse) return "Pending";
            var outcome = InteractiveOutcome;
            var colon = outcome.IndexOf(':');
            return colon > 0 ? outcome[..colon] : outcome;
        }
    }

    /// <summary>The answer the user actually gave, split off an "Answered: …" outcome for the card's
    /// "Your answer" line. Empty for outcomes that carry no free-form answer (an approval's
    /// "Approved once", a plain "Rejected", a skipped card), which is exactly when that line hides.</summary>
    [JsonIgnore]
    public string InteractiveAnswerText
    {
        get
        {
            if (IsAwaitingResponse) return "";
            var outcome = InteractiveOutcome;
            var colon = outcome.IndexOf(':');
            return colon >= 0 && colon + 1 < outcome.Length ? outcome[(colon + 1)..].Trim() : "";
        }
    }

    [JsonIgnore]
    public bool HasInteractiveAnswer => InteractiveAnswerText.Length > 0;

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

    /// <summary>Label for the approval card's "trust this tool for the session" action. The tool
    /// name is <see cref="EventTitle"/> on an approval card, so this reads "Trust send".</summary>
    [JsonIgnore]
    public string ApprovalTrustLabel => $"Trust {EventTitle}";

    /// <summary>
    /// Whether the approval card's "Explain why?" panel is open. It is purely local disclosure —
    /// it reveals the arguments the agent already sent with the request and never answers the
    /// approval, so a user can inspect what a tool would actually do without spending their
    /// decision to find out. Not persisted: a resolved card has nothing left to inspect.
    /// </summary>
    [JsonIgnore]
    public bool IsApprovalExplainOpen => _approvalExplainOpen;

    // The approval menu rows split their meaning across a bold verb and a muted consequence, which
    // a screen reader would otherwise announce as two unrelated fragments — so each row names
    // itself in full.
    [JsonIgnore]
    public string ApprovalAllowOnceAccessibilityName
        => string.IsNullOrWhiteSpace(EventDetail) ? "Allow once" : $"Allow once. {EventDetail}";

    [JsonIgnore]
    public string ApprovalTrustAccessibilityName => $"{ApprovalTrustLabel} for this session";

    public void ToggleApprovalExplain()
    {
        _approvalExplainOpen = !_approvalExplainOpen;
        OnPropertyChanged(nameof(IsApprovalExplainOpen));
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
    /// Pixel font size for this bubble's message text, stamped by <c>ChatViewModel</c> from the
    /// user's "Message text size" preference. Bound directly by the user/agent message templates
    /// because <c>ListView.FontSize</c> does not cascade into <c>ListViewItemPresenter</c> content,
    /// so a per-message value is what actually reaches the text. Default matches the "Medium" step.
    /// </summary>
    [ObservableProperty]
    [JsonIgnore]
    public partial double MessageFontSize { get; set; }

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

    /// <summary>The turn's options, in either mode — the card that renders them is the same, and
    /// <see cref="AskUserMultiSelect"/> only decides what clicking one does.</summary>
    [JsonIgnore]
    public ObservableCollection<AskUserOptionEntry> AskUserOptionEntries { get; } = new();

    /// <summary>Dynamic named fields for an ask_user turn (bound to inline TextBoxes).</summary>
    [JsonIgnore]
    public ObservableCollection<AskUserFieldEntry> AskUserFields { get; } = new();

    [JsonIgnore]
    public bool AskUserMultiSelect { get; set; }

    [ObservableProperty]
    [JsonIgnore]
    public partial string AskUserFreeText { get; set; }

    /// <summary>Optional feedback typed into a plan_review turn before approving or requesting changes.</summary>
    [ObservableProperty]
    [JsonIgnore]
    public partial string PlanFeedback { get; set; }

    [JsonIgnore]
    public bool HasAskUserFields => AskUserFields.Count > 0;

    /// <summary>Whether the turn offers a list to pick from. Both modes render the same card and
    /// differ only in the glyph and in what a click does, so one flag covers them.</summary>
    [JsonIgnore]
    public bool HasAskUserOptions => AskUserOptionEntries.Count > 0;

    /// <summary>Gated on the turn still being open: once it is answered or skipped, the options
    /// are a record, not a control. Expanding a settled card must not offer buttons that resolve
    /// nothing — the socket wait they replied to is gone.</summary>
    [JsonIgnore]
    public bool ShowAskUserOptions => IsAwaitingResponse && HasAskUserOptions;

    /// <summary>The option the user picked in a single-select turn, or null. Derived from the
    /// entries rather than stored, so there is one source of truth for what is selected.</summary>
    [JsonIgnore]
    public string? SelectedOptionText
    {
        get
        {
            foreach (var entry in AskUserOptionEntries)
            {
                if (entry.IsChecked) return entry.Text;
            }
            return null;
        }
    }

    /// <summary>The free-text answer box only makes sense while still awaiting a response and
    /// when the turn has no structured fields. Gating on <see cref="IsAwaitingResponse"/> too
    /// keeps an empty box from reappearing on a resolved bubble reloaded from history (whose
    /// options/fields aren't persisted, so <see cref="HasAskUserFields"/> reads false).</summary>
    [JsonIgnore]
    public bool ShowAskUserFreeText => IsAwaitingResponse && !HasAskUserFields;

    /// <summary>The "or provide custom answer" divider only earns its place when there is
    /// something to be an alternative *to* — with no options on offer, the box is simply the
    /// answer and a divider announcing it is noise.</summary>
    [JsonIgnore]
    public bool ShowAskUserCustomAnswer
        => ShowAskUserFreeText && HasAskUserOptions;
}
