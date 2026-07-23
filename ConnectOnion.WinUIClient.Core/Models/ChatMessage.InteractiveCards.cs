using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ConnectOnion.WinUIClient.Models;

/// <summary><see cref="ChatMessage"/> presentation state shared by ask_user and plan_review.</summary>
public sealed partial class ChatMessage
{
    private const string DiffStatePrefix = "diff-state:";
    private DiffPreviewModel? _diffPreview;
    private DiffChangeState? _presentedDiffState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInteractiveSubmitting))]
    [NotifyPropertyChangedFor(nameof(IsInteractiveEditable))]
    [NotifyPropertyChangedFor(nameof(InteractiveStateLabel))]
    [NotifyPropertyChangedFor(nameof(InteractiveStateDescription))]
    [NotifyPropertyChangedFor(nameof(ShowInteractiveError))]
    [JsonIgnore]
    public partial InteractiveCardPhase InteractivePhase { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInteractiveError))]
    [JsonIgnore]
    public partial string? InteractiveErrorText { get; set; }

    [ObservableProperty]
    [JsonIgnore]
    public partial bool IsPlanFeedbackOpen { get; set; }

    [ObservableProperty]
    [JsonIgnore]
    public partial bool IsPlanRejectConfirmOpen { get; set; }

    [JsonIgnore] public bool HasPlayedInteractiveEntrance { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiffViewportHeight))]
    [NotifyPropertyChangedFor(nameof(DiffExpandLabel))]
    [JsonIgnore]
    public partial bool IsDiffExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiffWrapLabel))]
    [JsonIgnore]
    public partial bool IsDiffWrapped { get; set; }

    [JsonIgnore] public bool IsInteractiveSubmitting => InteractivePhase == InteractiveCardPhase.Submitting;
    [JsonIgnore] public bool IsInteractiveEditable => Status == EventStatus.Running
        && InteractivePhase is InteractiveCardPhase.Pending or InteractiveCardPhase.Error;
    [JsonIgnore] public bool ShowInteractiveError => !string.IsNullOrWhiteSpace(InteractiveErrorText);
    [JsonIgnore] public bool IsConnectionLost => InteractivePhase == InteractiveCardPhase.ConnectionLost;
    [JsonIgnore] public bool IsInteractiveReadOnly => !IsInteractiveEditable && !IsInteractiveSubmitting;
    [JsonIgnore] public bool ShowInteractiveResult => Status != EventStatus.Running;
    [JsonIgnore] public bool ShowInteractiveActions => Status == EventStatus.Running;
    [JsonIgnore] public bool ShowAskUserFields => Status == EventStatus.Running && HasAskUserFields;
    [JsonIgnore] public bool ShowAskUserPendingContent => Status == EventStatus.Running;
    [JsonIgnore] public bool ShowAskUserStatusBadge => Status == EventStatus.Running;
    [JsonIgnore] public bool IsAskUserCompact => Status != EventStatus.Running;
    [JsonIgnore] public InteractiveVisualTone AskUserChromeTone
        => Status == EventStatus.Running ? InteractiveVisualTone.Warning : InteractiveVisualTone.Neutral;
    [JsonIgnore] public InteractiveVisualTone AskUserIconTone => Status == EventStatus.Running
        ? InteractiveVisualTone.Warning
        : IsRejectedAskUserResult ? InteractiveVisualTone.Danger : InteractiveVisualTone.Success;
    [JsonIgnore] public string AskUserIconGlyph => Status == EventStatus.Running
        ? "\uE897" : IsRejectedAskUserResult ? "\uE711" : "\uE73E";
    [JsonIgnore] public bool IsFileChangeApproval => TryGetAskUserTargetPath(out _);
    [JsonIgnore] public string AskUserTargetPath => TryGetAskUserTargetPath(out var path) ? path : "";
    [JsonIgnore] public string AskUserTargetFileName => DiffPathFormatter.FileName(AskUserTargetPath);
    [JsonIgnore] public string AskUserContextPath => DiffPathFormatter.ContextPath(AskUserTargetPath);
    [JsonIgnore] public string AskUserDisplayQuestion => IsFileChangeApproval
        ? $"Apply this change to {AskUserTargetFileName}?" : EventTitle;
    [JsonIgnore] public string AskUserCardTitle => Status == EventStatus.Running
        ? IsFileChangeApproval ? "Approval required" : "Agent needs your input"
        : AskUserResolvedTitle;
    [JsonIgnore] public string AskUserCardSubtitle => Status == EventStatus.Running
        ? IsFileChangeApproval ? "Review how the agent should continue." : "Choose how the agent should continue."
        : AskUserResolvedSubtitle;
    [JsonIgnore] public string AskUserSubmitLabel
    {
        get
        {
            if (IsInteractiveSubmitting) return "Submitting…";
            return AskUserOptionEntries.FirstOrDefault(option => option.IsChecked)?.Text switch
            {
                "Yes, apply this change" => "Apply once",
                "Yes to all (auto-approve)" => "Apply all",
                "No, reject and give feedback" => "Reject",
                _ => "Submit",
            };
        }
    }
    [JsonIgnore] public string PlanReviewSubmitLabel => IsInteractiveSubmitting ? "Submitting…" : "Approve plan";
    [JsonIgnore] public string AskUserResultTitle => InteractiveOutcome switch
    {
        "Skipped" => "Question skipped",
        "Rejected" => "Question rejected",
        _ when HasInteractiveAnswer => "Response submitted",
        _ => "Request completed",
    };

    [JsonIgnore]
    public string InteractiveStateLabel =>
        InteractivePhase == InteractiveCardPhase.Pending && Status != EventStatus.Running
            ? HistoricalInteractiveStateLabel
            : EffectiveInteractivePhase switch
    {
        InteractiveCardPhase.Pending => "Waiting",
        InteractiveCardPhase.Submitting => "Submitting",
        InteractiveCardPhase.Submitted => "Submitted",
        InteractiveCardPhase.Completed => "Completed",
        InteractiveCardPhase.Cancelled => "Cancelled",
        InteractiveCardPhase.Rejected => "Rejected",
        InteractiveCardPhase.Expired => "Expired",
        InteractiveCardPhase.ConnectionLost => "Connection lost",
        InteractiveCardPhase.Error => "Try again",
        _ => "Waiting",
    };

    private string HistoricalInteractiveStateLabel
        => EventMeta switch
        {
            "Skipped" => "Skipped",
            "Rejected" => "Rejected",
            { } value when value.StartsWith("Answered:", StringComparison.Ordinal) => "Submitted",
            { } value when value.Contains("approved", StringComparison.OrdinalIgnoreCase) => "Approved",
            _ => Status == EventStatus.Error ? "Rejected" : "Completed",
        };

    [JsonIgnore]
    public string InteractiveStateDescription =>
        InteractivePhase == InteractiveCardPhase.Pending && Status != EventStatus.Running
            ? HistoricalInteractiveStateDescription
            : EffectiveInteractivePhase switch
    {
        InteractiveCardPhase.Pending => "The agent is paused until you respond.",
        InteractiveCardPhase.Submitting => "Sending your response…",
        InteractiveCardPhase.Submitted => "Your response was sent.",
        InteractiveCardPhase.Completed => "This request has been completed.",
        InteractiveCardPhase.Cancelled => "This request was cancelled.",
        InteractiveCardPhase.Rejected => "The request was rejected.",
        InteractiveCardPhase.Expired => "This request is no longer active.",
        InteractiveCardPhase.ConnectionLost => "Your input is preserved. Reconnect before responding.",
        InteractiveCardPhase.Error => "The response was not sent. Your input is preserved.",
        _ => "",
    };

    private string HistoricalInteractiveStateDescription
        => EventMeta switch
        {
            "Skipped" => "No response was submitted.",
            "Rejected" => "This request was rejected.",
            { } value when value.StartsWith("Answered:", StringComparison.Ordinal) => "Your response was submitted.",
            _ => "This request has been completed.",
        };

    private InteractiveCardPhase EffectiveInteractivePhase
        => InteractivePhase == InteractiveCardPhase.Pending && Status != EventStatus.Running
            ? Status == EventStatus.Error ? InteractiveCardPhase.Rejected : InteractiveCardPhase.Completed
            : InteractivePhase;

    [JsonIgnore] public int AskUserSelectedCount => AskUserOptionEntries.Count(option => option.IsChecked);
    [JsonIgnore] public string AskUserSelectionSummary => AskUserMultiSelect
        ? $"{AskUserSelectedCount} selected"
        : "Select one option";
    [JsonIgnore] public string AskUserRequirementText => HasAskUserFields
        ? "Complete the requested fields."
        : HasAskUserOptions
            ? AskUserMultiSelect ? "Choose one or more options." : "Choose one option."
            : "Enter your response.";
    [JsonIgnore] public bool ShowAskUserSelectionSummary
        => ShowAskUserOptions && AskUserMultiSelect;

    [JsonIgnore]
    public bool CanSubmitAskUser
    {
        get
        {
            if (!IsInteractiveEditable) return false;
            if (AskUserFields.Count > 0)
                return AskUserFields.All(entry => !entry.Required || !string.IsNullOrWhiteSpace(entry.Value));
            return AskUserSelectedCount > 0 || !string.IsNullOrWhiteSpace(AskUserFreeText);
        }
    }

    [JsonIgnore] public DiffPreviewModel DiffPreview
        => _diffPreview ??= DiffPreviewModel.Parse(EventTitle, EventDetail,
            string.Equals(EventEyebrow, "CREATE", StringComparison.Ordinal));
    [JsonIgnore] public string DiffFileName => DiffPathFormatter.FileName(EventTitle);
    [JsonIgnore] public string DiffContextPath => DiffPathFormatter.ContextPath(EventTitle);
    [JsonIgnore] public string DiffDisplayPath => DiffPathFormatter.MiddleEllipsis(EventTitle);
    // The card itself folds; an open diff keeps a bounded inner viewport so one large file never
    // disables chat-list virtualization by becoming the height of the whole document.
    [JsonIgnore] public double DiffViewportHeight => 360;
    [JsonIgnore] public bool CanExpandDiff => DiffPreview.Files.Sum(file => file.Lines.Count) > 12;
    [JsonIgnore] public bool CanWrapDiff => DiffPreview.Files
        .SelectMany(file => file.Lines)
        .Any(line => line.Content.Length > 100);
    [JsonIgnore] public string DiffExpandLabel => IsDiffExpanded ? "Collapse" : "Expand";
    [JsonIgnore] public string DiffWrapLabel => IsDiffWrapped ? "No wrap" : "Wrap";
    [JsonIgnore] public DiffChangeState DiffState => ParseDiffState(EventResult);
    [JsonIgnore] public bool ShowDiffBody => IsDiffExpanded;
    [JsonIgnore] public bool CanToggleDiffVisibility => DiffState != DiffChangeState.Pending;
    [JsonIgnore] public bool ShowDiffProblem => DiffState is DiffChangeState.Failed
        or DiffChangeState.PartiallyApplied or DiffChangeState.Disconnected or DiffChangeState.Unconfirmed;
    [JsonIgnore] public string DiffCardTitle => DiffState switch
    {
        DiffChangeState.Applied => $"Changes applied to {DiffFileName}",
        DiffChangeState.Rejected => "Proposed changes rejected",
        DiffChangeState.Failed => "Changes could not be applied",
        DiffChangeState.PartiallyApplied => "Changes may be partially applied",
        DiffChangeState.Disconnected => "Connection lost while applying changes",
        DiffChangeState.Unconfirmed => "Change result could not be confirmed",
        DiffChangeState.Applying => "Applying file changes",
        DiffChangeState.Preview => "Review file changes",
        _ => "Review file changes",
    };
    [JsonIgnore] public string DiffCardSubtitle => DiffState == DiffChangeState.Applied
        ? ""
        : DiffState == DiffChangeState.Rejected
            ? DiffFileName
            : DiffPreview.FileSummary;
    [JsonIgnore] public string DiffHeaderMeta => DiffPreview.ChangeSummary;
    [JsonIgnore] public string DiffIconGlyph => DiffState switch
    {
        DiffChangeState.Applied => "\uE73E",
        DiffChangeState.Rejected or DiffChangeState.Failed => "\uE711",
        DiffChangeState.PartiallyApplied or DiffChangeState.Disconnected or DiffChangeState.Unconfirmed => "\uE7BA",
        _ => "\uE8A5",
    };
    [JsonIgnore] public InteractiveVisualTone DiffIconTone => DiffState switch
    {
        DiffChangeState.Applied => InteractiveVisualTone.Success,
        DiffChangeState.Rejected or DiffChangeState.Failed => InteractiveVisualTone.Danger,
        DiffChangeState.Pending or DiffChangeState.Applying or DiffChangeState.PartiallyApplied
            or DiffChangeState.Disconnected or DiffChangeState.Unconfirmed => InteractiveVisualTone.Warning,
        _ => InteractiveVisualTone.Neutral,
    };
    [JsonIgnore] public InteractiveVisualTone DiffChromeTone
        => DiffState == DiffChangeState.Pending ? InteractiveVisualTone.Warning : InteractiveVisualTone.Neutral;
    [JsonIgnore] public string DiffToggleLabel => IsDiffExpanded
        ? "Hide changes"
        : DiffState == DiffChangeState.Rejected ? "View proposed changes" : "View changes";
    [JsonIgnore] public string DiffProblemText => DiffState switch
    {
        DiffChangeState.Failed => "The file tool reported an error. Review the proposed changes and tool details.",
        DiffChangeState.PartiallyApplied => "The tool reported a partial result. Verify the file before continuing.",
        DiffChangeState.Disconnected => "The connection ended before the file result was confirmed.",
        _ => "The turn ended without a successful file result. The proposed changes are shown below.",
    };
    [JsonIgnore] public string DiffAccessibilityName
        => $"{DiffCardTitle}. {DiffFileName}. {DiffPreview.ChangeSummary}. {(IsDiffExpanded ? "Changes visible" : DiffToggleLabel)}.";
    [JsonIgnore] public bool CanSendPlanFeedback => IsInteractiveEditable && !string.IsNullOrWhiteSpace(PlanFeedback);

    public bool ValidateAskUser()
    {
        var valid = true;
        foreach (var field in AskUserFields)
        {
            field.ValidationError = field.Required && string.IsNullOrWhiteSpace(field.Value)
                ? $"{field.Label} is required." : null;
            valid &= field.ValidationError is null;
        }
        if (AskUserFields.Count == 0 && AskUserSelectedCount == 0 && string.IsNullOrWhiteSpace(AskUserFreeText))
        {
            InteractiveErrorText = "Choose an option or enter an answer.";
            valid = false;
        }
        else if (valid)
        {
            InteractiveErrorText = null;
        }
        return valid;
    }

    public bool TryBeginInteractiveSubmit()
    {
        if (!IsInteractiveEditable) return false;
        InteractiveErrorText = null;
        InteractivePhase = InteractiveCardPhase.Submitting;
        return true;
    }

    public void CompleteInteractiveSubmit(string outcome, bool rejected = false)
    {
        IsPlanFeedbackOpen = false;
        IsPlanRejectConfirmOpen = false;
        EventMeta = outcome;
        InteractivePhase = rejected ? InteractiveCardPhase.Rejected : InteractiveCardPhase.Submitted;
        Status = rejected ? EventStatus.Error : EventStatus.Done;
    }

    public void FailInteractiveSubmit(string message, bool connectionLost)
    {
        InteractiveErrorText = message;
        InteractivePhase = connectionLost ? InteractiveCardPhase.ConnectionLost : InteractiveCardPhase.Error;
    }

    public void RevalidateInteractiveRequest()
    {
        if (Status != EventStatus.Running || InteractivePhase != InteractiveCardPhase.ConnectionLost) return;
        InteractiveErrorText = null;
        InteractivePhase = InteractiveCardPhase.Pending;
    }

    public void MarkInteractiveConnectionLost()
    {
        if (Status != EventStatus.Running || InteractivePhase == InteractiveCardPhase.Submitting) return;
        InteractiveErrorText = "Connection lost before this response was submitted.";
        InteractivePhase = InteractiveCardPhase.ConnectionLost;
    }

    public void NotifyAskUserInputChanged()
    {
        InteractiveErrorText = null;
        OnPropertyChanged(nameof(AskUserSelectedCount));
        OnPropertyChanged(nameof(AskUserSelectionSummary));
        OnPropertyChanged(nameof(CanSubmitAskUser));
        OnPropertyChanged(nameof(AskUserSubmitLabel));
    }

    partial void OnAskUserFreeTextChanged(string value) => NotifyAskUserInputChanged();

    partial void OnPlanFeedbackChanged(string value)
        => OnPropertyChanged(nameof(CanSendPlanFeedback));

    partial void OnInteractivePhaseChanged(InteractiveCardPhase value)
    {
        OnPropertyChanged(nameof(AskUserSubmitLabel));
        OnPropertyChanged(nameof(PlanReviewSubmitLabel));
        OnPropertyChanged(nameof(AskUserResultTitle));
        NotifyInteractivePresentationChanged();
    }

    partial void OnEventMetaChanged(string? value)
    {
        if (IsAskUserEvent && RelatedDiffPreview is not null)
            RelatedDiffPreview.ApplyDiffApprovalAnswer(value);
        NotifyInteractivePresentationChanged();
    }
    partial void OnEventTitleChanged(string value)
    {
        _diffPreview = null;
        NotifyInteractivePresentationChanged();
        NotifyDiffPresentationChanged();
    }
    partial void OnEventEyebrowChanged(string value) => _diffPreview = null;
    partial void OnEventKindChanged(string value)
    {
        if (value == "diff_preview") SynchronizeDiffPresentation();
    }
    partial void OnEventResultChanged(string? value)
    {
        if (IsDiffPreviewEvent) SynchronizeDiffPresentation();
    }
    partial void OnIsDiffExpandedChanged(bool value) => NotifyDiffPresentationChanged();

    internal void NotifyInteractivePresentationChanged()
    {
        OnPropertyChanged(nameof(ShowAskUserPendingContent));
        OnPropertyChanged(nameof(ShowAskUserStatusBadge));
        OnPropertyChanged(nameof(IsAskUserCompact));
        OnPropertyChanged(nameof(AskUserChromeTone));
        OnPropertyChanged(nameof(AskUserIconTone));
        OnPropertyChanged(nameof(AskUserIconGlyph));
        OnPropertyChanged(nameof(IsFileChangeApproval));
        OnPropertyChanged(nameof(AskUserTargetPath));
        OnPropertyChanged(nameof(AskUserTargetFileName));
        OnPropertyChanged(nameof(AskUserContextPath));
        OnPropertyChanged(nameof(AskUserDisplayQuestion));
        OnPropertyChanged(nameof(AskUserCardTitle));
        OnPropertyChanged(nameof(AskUserCardSubtitle));
        OnPropertyChanged(nameof(AskUserSubmitLabel));
    }

    private bool IsRejectedAskUserResult
        => string.Equals(InteractiveOutcome, "Rejected", StringComparison.OrdinalIgnoreCase)
            || InteractiveAnswerText.StartsWith("No, reject", StringComparison.OrdinalIgnoreCase);

    private string AskUserResolvedTitle
        => InteractiveAnswerText switch
        {
            "Yes, apply this change" => "Change approved",
            "Yes to all (auto-approve)" => "Similar changes approved",
            { } answer when answer.StartsWith("No, reject", StringComparison.OrdinalIgnoreCase) => "Change rejected",
            _ when string.Equals(InteractiveOutcome, "Rejected", StringComparison.OrdinalIgnoreCase) => "Request rejected",
            _ when string.Equals(InteractiveOutcome, "Skipped", StringComparison.OrdinalIgnoreCase) => "Request closed",
            _ => "Response submitted",
        };

    private string AskUserResolvedSubtitle
        => InteractiveAnswerText switch
        {
            "Yes, apply this change" when IsFileChangeApproval => $"Applied once to {AskUserTargetFileName}",
            "Yes to all (auto-approve)" => "Auto-approval enabled for this session",
            { } answer when answer.StartsWith("No, reject", StringComparison.OrdinalIgnoreCase) => "Feedback requested from the agent",
            { Length: > 0 } answer => answer,
            _ when string.Equals(InteractiveOutcome, "Skipped", StringComparison.OrdinalIgnoreCase) => "No response was submitted",
            _ => "Response sent to the agent",
        };

    private bool TryGetAskUserTargetPath(out string path)
    {
        path = "";
        var question = EventTitle.Trim();
        string? prefix = question.StartsWith("Apply changes to ", StringComparison.OrdinalIgnoreCase)
            ? "Apply changes to "
            : question.StartsWith("Apply this change to ", StringComparison.OrdinalIgnoreCase)
                ? "Apply this change to " : null;
        if (prefix is null) return false;
        path = question[prefix.Length..].Trim().TrimEnd('?');
        return path.Length > 0;
    }

    /// <summary>The ask_user card paired with this diff. Transient by design: replay reconstructs
    /// the relation from event order, while the durable outcome lives on the diff itself.</summary>
    [JsonIgnore] public ChatMessage? RelatedDiffPreview { get; set; }

    public void ToggleDiffExpanded()
    {
        if (!CanToggleDiffVisibility) return;
        IsDiffExpanded = !IsDiffExpanded;
    }

    public void SetDiffState(DiffChangeState state)
    {
        if (!IsDiffPreviewEvent) return;
        EventResult = DiffStatePrefix + state;
    }

    public void ApplyDiffApprovalAnswer(string? outcome)
    {
        if (!IsDiffPreviewEvent || string.IsNullOrWhiteSpace(outcome)) return;
        var answer = outcome.StartsWith("Answered: ", StringComparison.Ordinal)
            ? outcome[10..] : outcome;
        if (answer.StartsWith("No, reject", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "Skipped", StringComparison.OrdinalIgnoreCase))
        {
            SetDiffState(DiffChangeState.Rejected);
            return;
        }
        if ((answer == "Yes, apply this change" || answer == "Yes to all (auto-approve)")
            && DiffState is DiffChangeState.Pending or DiffChangeState.Preview)
        {
            SetDiffState(DiffChangeState.Applying);
        }
    }

    private static DiffChangeState ParseDiffState(string? value)
        => value is not null && value.StartsWith(DiffStatePrefix, StringComparison.Ordinal)
            && Enum.TryParse<DiffChangeState>(value[DiffStatePrefix.Length..], out var state)
                ? state
                : DiffChangeState.Unconfirmed;

    private void SynchronizeDiffPresentation()
    {
        var state = DiffState;
        if (_presentedDiffState == state) return;
        _presentedDiffState = state;
        IsDiffExpanded = state is not (DiffChangeState.Applied or DiffChangeState.Rejected);
        NotifyDiffPresentationChanged();
    }

    private void NotifyDiffPresentationChanged()
    {
        OnPropertyChanged(nameof(DiffState));
        OnPropertyChanged(nameof(ShowDiffBody));
        OnPropertyChanged(nameof(CanToggleDiffVisibility));
        OnPropertyChanged(nameof(ShowDiffProblem));
        OnPropertyChanged(nameof(DiffCardTitle));
        OnPropertyChanged(nameof(DiffCardSubtitle));
        OnPropertyChanged(nameof(DiffHeaderMeta));
        OnPropertyChanged(nameof(DiffIconGlyph));
        OnPropertyChanged(nameof(DiffIconTone));
        OnPropertyChanged(nameof(DiffChromeTone));
        OnPropertyChanged(nameof(DiffToggleLabel));
        OnPropertyChanged(nameof(DiffProblemText));
        OnPropertyChanged(nameof(DiffAccessibilityName));
    }
    public void ToggleDiffWrap()
    {
        IsDiffWrapped = !IsDiffWrapped;
        DiffPreview.IsWrapped = IsDiffWrapped;
        foreach (var file in DiffPreview.Files)
        foreach (var line in file.Lines)
            line.IsWrapped = IsDiffWrapped;
    }
    public void OpenPlanFeedback() => IsPlanFeedbackOpen = true;
    public void ClosePlanFeedback() => IsPlanFeedbackOpen = false;
    public void OpenPlanRejectConfirmation() => IsPlanRejectConfirmOpen = true;
    public void ClosePlanRejectConfirmation() => IsPlanRejectConfirmOpen = false;
}
