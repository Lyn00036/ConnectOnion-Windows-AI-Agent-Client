using System;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// <see cref="ChatMessage"/>: the state and commands for an <c>approval_needed</c> card, now
/// rendered <b>inside</b> the turn's tool-activity card (see <c>ToolActivityViewModel.Approval</c>)
/// rather than as a standalone bubble.
///
/// <para>The network round-trip lives in the view model (it needs the live socket); this partial
/// only holds the display projection and the phase machine, and routes a clicked decision to a
/// <see cref="ApprovalResponder"/> the view model sets. The commands flip to
/// <see cref="ApprovalCardPhase.Submitting"/> <i>synchronously</i> before awaiting, so a double-click
/// or an Enter-repeat cannot send twice.</para>
/// </summary>
public sealed partial class ChatMessage
{
    // ---- Request display (populated once by the projection) ----

    /// <summary>The extracted target/operation for the approval summary, built by
    /// <c>ApprovalTargetFormatter</c> from the request's arguments. The card renders this, never the
    /// raw <see cref="EventArgs"/> blob (which stays behind "View operation details").</summary>
    [JsonIgnore]
    public ApprovalTarget ApprovalTargetInfo { get; set; } = ApprovalTarget.Empty;

    [JsonIgnore] public bool HasApprovalTarget => ApprovalTargetInfo.HasTarget;
    [JsonIgnore] public string ApprovalTargetText => ApprovalTargetInfo.Target;
    [JsonIgnore] public string ApprovalTargetTooltip => ApprovalTargetInfo.FullTarget;
    [JsonIgnore] public ApprovalTargetKind ApprovalTargetKind => ApprovalTargetInfo.Kind;
    [JsonIgnore] public bool IsApprovalCommand => ApprovalTargetKind == ApprovalTargetKind.Command;
    [JsonIgnore] public bool ShowApprovalNonCommandTarget => HasApprovalTarget && !IsApprovalCommand;
    [JsonIgnore] public string ApprovalCommandText => IsApprovalCommand ? ApprovalTargetInfo.FullTarget : "";
    [JsonIgnore] public bool HasApprovalCommand => ApprovalCommandText.Length > 0;
    [JsonIgnore] public bool ShowApprovalCommandToggle
        => ApprovalCommandText.Length > 160 || ApprovalCommandText.Count(c => c == '\n') >= 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApprovalCommandMaxLines))]
    [NotifyPropertyChangedFor(nameof(ApprovalCommandToggleLabel))]
    [JsonIgnore]
    public partial bool IsApprovalCommandExpanded { get; set; }

    [JsonIgnore] public int ApprovalCommandMaxLines => IsApprovalCommandExpanded ? 0 : 3;
    [JsonIgnore] public string ApprovalCommandToggleLabel => IsApprovalCommandExpanded
        ? CoreStrings.Get("ApprovalCollapseCommand", "Collapse command")
        : CoreStrings.Get("ApprovalShowFullCommand", "Show full command");

    [RelayCommand]
    private void ToggleApprovalCommand() => IsApprovalCommandExpanded = !IsApprovalCommandExpanded;

    /// <summary>Set true by the projection when this approval is embedded in a tool-activity card,
    /// so the template selector renders no standalone bubble for it (it still lives in the message
    /// list, which the persist path relies on for answer alignment).</summary>
    [JsonIgnore]
    public bool IsEmbeddedApproval { get; set; }

    /// <summary>The tool-activity card this approval is embedded in, so the view model can clear the
    /// card's parked "waiting" status the moment a decision is sent (before the turn's own Complete
    /// runs). Set by the projection; <c>[JsonIgnore]</c> — approvals aren't persisted.</summary>
    [JsonIgnore]
    public ToolActivityViewModel? OwnerToolActivity { get; set; }

    [JsonIgnore] public string ApprovalPromptTitle => IsApprovalCommand
        ? CoreStrings.Get("ApprovalTitleCommand", "Approve command execution?")
        : ApprovalTargetKind == ApprovalTargetKind.File
            ? CoreStrings.Get("ApprovalTitleFile", "Approve file operation?")
            : CoreStrings.Get("ApprovalTitleGeneric", "Approve this operation?");

    [JsonIgnore] public string ApprovalPromptSubtitle => IsApprovalCommand
        ? CoreStrings.Get(
            "ApprovalSubtitleCommand",
            "This command can modify or delete files on the remote server.")
        : CoreStrings.Get(
            "ApprovalSubtitleGeneric",
            "Review the operation and its risk before allowing it to continue.");

    /// <summary>"This tool wants to modify:" — the operation line above the target chip. Empty when
    /// no target could be extracted, in which case <see cref="ShowApprovalFallback"/> is shown.</summary>
    [JsonIgnore]
    public string ApprovalOperationLine => HasApprovalTarget
        ? $"This tool wants to {ApprovalTargetInfo.OperationVerb}:"
        : "";

    /// <summary>The generic line shown when no concrete target is available.</summary>
    [JsonIgnore]
    public string ApprovalFallbackLine => CoreStrings.Get(
        "ApprovalFallbackLine",
        "This operation requires your approval before it can continue.");

    [JsonIgnore] public bool ShowApprovalFallback => !HasApprovalTarget;

    /// <summary>Compact preview of the remaining tools in the same approval batch.</summary>
    [JsonIgnore] public string ApprovalBatchSummary { get; set; } = "";
    [JsonIgnore] public bool HasApprovalBatch => !string.IsNullOrWhiteSpace(ApprovalBatchSummary);

    // ---- "Explain this risk" / "View operation details" (local disclosure) ----

    private bool _approvalDetailsOpen;

    /// <summary>Whether the "View operation details" panel (the formatted arguments) is open.
    /// Purely local — it reveals what the agent already sent and never answers the approval.</summary>
    [JsonIgnore] public bool IsApprovalDetailsOpen => _approvalDetailsOpen;

    [RelayCommand]
    private void ToggleApprovalDetails()
    {
        _approvalDetailsOpen = !_approvalDetailsOpen;
        OnPropertyChanged(nameof(IsApprovalDetailsOpen));
        OnPropertyChanged(nameof(ApprovalDetailsLabel));
    }

    [JsonIgnore] public string ApprovalDetailsLabel => IsApprovalDetailsOpen
        ? CoreStrings.Get("ApprovalHideDetails", "Hide operation details")
        : CoreStrings.Get("ApprovalShowDetails", "Operation details");

    /// <summary>"Explain this risk" reuses the same local disclosure as the risk description. It
    /// answers nothing — inspecting a request never costs the user their decision.</summary>
    [RelayCommand]
    private void ExplainApprovalRisk() => ToggleApprovalExplain();

    /// <summary>The text the "Explain this risk" panel shows: the agent's own description when it
    /// sent one, otherwise a generic caution so the link is never a dead end (an approval frequently
    /// carries no description, and a link that reveals nothing reads as broken).</summary>
    [JsonIgnore]
    public string ApprovalRiskText => IsApprovalCommand
        ? CoreStrings.Get(
            "ApprovalRiskCommand", "Risk: This command may delete or modify server data.")
        : CoreStrings.Get(
            "ApprovalRiskGeneric", "Risk: This operation may change remote server data.");

    /// <summary>The panel is shown whenever explain is open — it always has text now (see
    /// <see cref="ApprovalRiskText"/>). <see cref="ToggleApprovalExplain"/> raises this so the panel
    /// actually appears when the link is clicked.</summary>
    [JsonIgnore] public bool ShowApprovalRisk => IsApprovalExplainOpen;

    // ---- Phase machine ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsApprovalWaiting))]
    [NotifyPropertyChangedFor(nameof(IsApprovalSubmitting))]
    [NotifyPropertyChangedFor(nameof(AreApprovalRequestControlsEnabled))]
    [NotifyPropertyChangedFor(nameof(IsApprovalPending))]
    [NotifyPropertyChangedFor(nameof(IsApprovalResolved))]
    [NotifyPropertyChangedFor(nameof(IsApprovalApprovedResult))]
    [NotifyPropertyChangedFor(nameof(IsApprovalRejectedResult))]
    [NotifyPropertyChangedFor(nameof(IsApprovalStoppedResult))]
    [NotifyPropertyChangedFor(nameof(IsApprovalExplanationResult))]
    [NotifyPropertyChangedFor(nameof(IsApprovalFailedResult))]
    [NotifyPropertyChangedFor(nameof(ShowApprovalRequestBody))]
    [NotifyPropertyChangedFor(nameof(IsApprovalActionable))]
    [NotifyPropertyChangedFor(nameof(ApprovalResolvedText))]
    [NotifyCanExecuteChangedFor(nameof(AllowOnceCommand))]
    [NotifyCanExecuteChangedFor(nameof(TrustSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExplainCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryApprovalCommand))]
    [JsonIgnore]
    public partial ApprovalCardPhase ApprovalPhase { get; set; }

    /// <summary>The decision currently being sent, so the spinner shows the right verb and Retry
    /// re-issues the same action.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApprovalSubmittingVerb))]
    [JsonIgnore]
    public partial ApprovalAction ApprovalPendingAction { get; set; }

    [ObservableProperty]
    [JsonIgnore]
    public partial string? ApprovalErrorText { get; set; }

    [JsonIgnore] public bool IsApprovalWaiting => ApprovalPhase == ApprovalCardPhase.Waiting;
    [JsonIgnore] public bool IsApprovalSubmitting => ApprovalPhase == ApprovalCardPhase.Submitting;
    [JsonIgnore] public bool AreApprovalRequestControlsEnabled => !IsApprovalSubmitting;
    /// <summary>Still blocking the turn (awaiting the user or mid-send), so the card's amber
    /// "Waiting for approval" status stays lit until a decision resolves.</summary>
    [JsonIgnore] public bool IsApprovalPending
        => ApprovalPhase is ApprovalCardPhase.Waiting or ApprovalCardPhase.Submitting
            or ApprovalCardPhase.Failed;
    [JsonIgnore] public bool IsApprovalFailedResult => ApprovalPhase == ApprovalCardPhase.Failed;
    [JsonIgnore] public bool IsApprovalApprovedResult
        => ApprovalPhase is ApprovalCardPhase.ApprovedOnce or ApprovalCardPhase.TrustedSession;
    [JsonIgnore] public bool IsApprovalRejectedResult => ApprovalPhase == ApprovalCardPhase.Rejected;
    [JsonIgnore] public bool IsApprovalStoppedResult => ApprovalPhase == ApprovalCardPhase.Stopped;
    [JsonIgnore] public bool IsApprovalExplanationResult => ApprovalPhase == ApprovalCardPhase.ExplanationRequested;
    [JsonIgnore] public bool IsApprovalResolved
        => ApprovalPhase is ApprovalCardPhase.ApprovedOnce or ApprovalCardPhase.TrustedSession
            or ApprovalCardPhase.Rejected or ApprovalCardPhase.Stopped
            or ApprovalCardPhase.ExplanationRequested;

    /// <summary>The request summary (title / operation / target / disclosure) shows until a decision
    /// resolves — through the submitting and failed phases too, so the user keeps their context.</summary>
    [JsonIgnore] public bool ShowApprovalRequestBody => !IsApprovalResolved;

    /// <summary>The decision buttons are shown while awaiting the user or after a failed submit
    /// (which offers a Retry); they give way to a spinner while submitting and to the resolved line
    /// once decided.</summary>
    [JsonIgnore] public bool IsApprovalActionable
        => ApprovalPhase is ApprovalCardPhase.Waiting or ApprovalCardPhase.Failed;

    /// <summary>The compact resolved line inside the card once a decision is made.</summary>
    [JsonIgnore]
    public string ApprovalResolvedText => ApprovalPhase switch
    {
        ApprovalCardPhase.ApprovedOnce => CoreStrings.Get("ApprovalResolvedOnce", "Approved once"),
        ApprovalCardPhase.TrustedSession
            => CoreStrings.Get("ApprovalResolvedSession", "Allowed for this session"),
        ApprovalCardPhase.Rejected => CoreStrings.Get("ApprovalResolvedDeclined", "Declined"),
        ApprovalCardPhase.Stopped => CoreStrings.Get("ApprovalResolvedStopped", "Task stopped"),
        ApprovalCardPhase.ExplanationRequested
            => CoreStrings.Get("ApprovalResolvedExplain", "Explanation requested"),
        _ => "",
    };

    [JsonIgnore]
    public string ApprovalSubmittingVerb => ApprovalPendingAction switch
    {
        ApprovalAction.Reject => CoreStrings.Get("ApprovalSubmittingReject", "Rejecting…"),
        ApprovalAction.Stop => CoreStrings.Get("ApprovalSubmittingStop", "Stopping…"),
        ApprovalAction.Explain
            => CoreStrings.Get("ApprovalSubmittingExplain", "Requesting explanation…"),
        _ => CoreStrings.Get("ApprovalSubmittingApprove", "Approving…"),
    };

    [ObservableProperty]
    [JsonIgnore]
    public partial string ApprovalFeedback { get; set; }

    public void CloseApprovalDisclosures()
    {
        if (_approvalDetailsOpen)
        {
            _approvalDetailsOpen = false;
            OnPropertyChanged(nameof(IsApprovalDetailsOpen));
            OnPropertyChanged(nameof(ApprovalDetailsLabel));
        }
        IsApprovalCommandExpanded = false;
    }

    // ---- Decision routing ----

    /// <summary>Set by the live view model; performs the network round-trip for a decision. Null on
    /// the headless persist path, where the decision controls never render.</summary>
    private Func<ChatMessage, ApprovalAction, Task>? _approvalResponder;

    [JsonIgnore]
    public Func<ChatMessage, ApprovalAction, Task>? ApprovalResponder
    {
        get => _approvalResponder;
        set
        {
            if (ReferenceEquals(_approvalResponder, value)) return;
            _approvalResponder = value;

            // The live projection adds the approval to the Tool Activity before its backing
            // message reaches IChatProjectionTarget.Add, where the responder is wired. XAML can
            // therefore query CanExecute once while this is still null. Commands do not
            // automatically observe a delegate property, so explicitly invalidate all of them
            // when the live responder arrives (or is detached during cleanup).
            AllowOnceCommand.NotifyCanExecuteChanged();
            TrustSessionCommand.NotifyCanExecuteChanged();
            RejectCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            ExplainCommand.NotifyCanExecuteChanged();
            RetryApprovalCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Actionable while awaiting the user, or after a failed submit (so Retry works). The
    /// commands' <c>CanExecute</c>, so a rebuilt view re-queries it on <see cref="ApprovalPhase"/>
    /// changes.</summary>
    private bool CanRunApproval => ApprovalResponder is not null
        && ApprovalPhase is ApprovalCardPhase.Waiting or ApprovalCardPhase.Failed;

    [RelayCommand(CanExecute = nameof(CanRunApproval))]
    private Task AllowOnce() => RespondApproval(ApprovalAction.AllowOnce);

    [RelayCommand(CanExecute = nameof(CanRunApproval))]
    private Task TrustSession() => RespondApproval(ApprovalAction.TrustSession);

    [RelayCommand(CanExecute = nameof(CanRunApproval))]
    private Task Reject() => RespondApproval(ApprovalAction.Reject);

    [RelayCommand(CanExecute = nameof(CanRunApproval))]
    private Task Stop() => RespondApproval(ApprovalAction.Stop);

    [RelayCommand(CanExecute = nameof(CanRunApproval))]
    private Task Explain()
    {
        // Deliberately NOT localized: this is the feedback field of the wire frame, read by the
        // agent's model, not shown to the user. Translating it would ask a remote agent to
        // reason in whatever language the client happens to be set to.
        ApprovalFeedback = "Explain why this operation is required, what it will do, and what risks it has.";
        return RespondApproval(ApprovalAction.Explain);
    }

    [RelayCommand(CanExecute = nameof(CanRunApproval))]
    private Task RetryApproval() => RespondApproval(ApprovalPendingAction);

    /// <summary>Flips to <see cref="ApprovalCardPhase.Submitting"/> before awaiting, so a second rapid
    /// click sees the guard closed and cannot send twice; then hands off to the view model.</summary>
    private Task RespondApproval(ApprovalAction action)
    {
        if (ApprovalResponder is not { } responder) return Task.CompletedTask;
        if (ApprovalPhase is not (ApprovalCardPhase.Waiting or ApprovalCardPhase.Failed)) return Task.CompletedTask;

        ApprovalPendingAction = action;
        ApprovalErrorText = null;
        ApprovalPhase = ApprovalCardPhase.Submitting;
        return responder(this, action);
    }

    /// <summary>Called by the view model once a decision has been sent successfully, mapping the
    /// action to its resolved phase.</summary>
    public void CompleteApproval(ApprovalAction action)
        => ApprovalPhase = action switch
        {
            ApprovalAction.AllowOnce => ApprovalCardPhase.ApprovedOnce,
            ApprovalAction.TrustSession => ApprovalCardPhase.TrustedSession,
            ApprovalAction.Reject => ApprovalCardPhase.Rejected,
            ApprovalAction.Stop => ApprovalCardPhase.Stopped,
            ApprovalAction.Explain => ApprovalCardPhase.ExplanationRequested,
            _ => ApprovalCardPhase.ApprovedOnce,
        };

    /// <summary>Called by the view model when sending the decision threw: the controls come back
    /// with the error and a Retry, so the UI never stalls on "Approving…".</summary>
    public void FailApproval(string message)
    {
        ApprovalErrorText = message;
        ApprovalPhase = ApprovalCardPhase.Failed;
    }

    // ---- Accessibility (screen readers read the tool + scope, never an icon alone) ----

    // These are AutomationProperties.Name values: user-visible text that a screen reader speaks,
    // localized like any other string. They are also the only thing a screen-reader user has to
    // tell the four decision buttons apart, so the tool name stays interpolated in — a
    // translation may move the placeholder but must not drop it.
    [JsonIgnore] public string ApprovalAllowOnceName
        => FormatWithTool("ApprovalAllowOnceName", "Allow {0} once");
    [JsonIgnore] public string ApprovalTrustName
        => FormatWithTool("ApprovalTrustName", "Trust {0} for this session");
    [JsonIgnore] public string ApprovalRejectName
        => FormatWithTool("ApprovalRejectName", "Decline {0}; the agent may continue");
    [JsonIgnore] public string ApprovalStopName => CoreStrings.Get(
        "ApprovalStopName", "Stop the entire agent task, not only this command");
    [JsonIgnore] public string ApprovalExplainName
        => CoreStrings.Get("ApprovalExplainName", "Explain approval risk");
    [JsonIgnore] public string ApprovalRequestExplanationName => CoreStrings.Get(
        "ApprovalRequestExplanationName",
        "Reject this operation and ask the agent to explain it");
    [JsonIgnore] public string ApprovalDetailsName
        => CoreStrings.Get("ApprovalDetailsName", "View operation details");

    private string FormatWithTool(string key, string fallbackFormat)
    {
        var format = CoreStrings.Get(key, fallbackFormat);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, EventTitle);
        }
        catch (FormatException)
        {
            // A malformed translation must not leave a decision button unlabelled.
            return string.Format(CultureInfo.InvariantCulture, fallbackFormat, EventTitle);
        }
    }
}
