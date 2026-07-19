using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>Controls how an assistant turn's tool activity is rendered.</summary>
public enum ToolDisplayMode { Hidden, Compact, Detailed }

public enum ToolActivityStatus
{
    Running,
    Success,
    PartialSuccess,
    Failed,
    Cancelled,
    WaitingForConfirmation,
    WaitingForPermission,
}

public enum ToolStepStatus { Pending, Running, Success, Warning, Failed, Cancelled }

/// <summary>
/// Durable, UI-facing aggregate for every tool invocation in one assistant turn. It is
/// deliberately independent of a page: the stream projector mutates it and the existing
/// message repository serializes it with the chat message, so re-entering a session shows
/// exactly the same compact timeline.
/// </summary>
public sealed partial class ToolActivityViewModel : Common.ObservableObject
{
    public ToolActivityViewModel()
    {
        Status = ToolActivityStatus.Running;
        DisplayMode = ToolDisplayMode.Compact;
        // Starts collapsed; ToolActivityProjector.ApplyAutoExpansion opens it as soon as a tool
        // actually starts on a live view and folds it again when the run finishes. The seed
        // matters for the headless paths (persistence, migration) that never call the projector's
        // live rules — those must write the card closed, so reopening an old conversation does
        // not replay every past run at full height.
        IsExpanded = false;
        Summary = "Running tools…";
        StartedAt = DateTimeOffset.UtcNow;
    }

    public string TurnId { get; set; } = Guid.NewGuid().ToString("N");

    public ObservableCollection<ToolStepViewModel> Steps { get; set; } = new();

    [ObservableProperty]
    public partial ToolActivityStatus Status { get; set; }

    partial void OnStatusChanged(ToolActivityStatus value) => OnPresentationChanged();

    [ObservableProperty]
    public partial ToolDisplayMode DisplayMode { get; set; }

    partial void OnDisplayModeChanged(ToolDisplayMode value)
    {
        if (value == ToolDisplayMode.Detailed) IsExpanded = true;
        OnPresentationChanged();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimelineVisible))]
    [NotifyPropertyChangedFor(nameof(AccessibilityName))]
    [NotifyPropertyChangedFor(nameof(ShowCollapsedError))]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset StartedAt { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? CompletedAt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastErrorMessage))]
    [NotifyPropertyChangedFor(nameof(ShowCollapsedError))]
    public partial string? ErrorSummary { get; set; }

    /// <summary>
    /// What actually went wrong, for the one line the collapsed card shows under its header. The
    /// last failed step wins over <see cref="ErrorSummary"/> because that field is only set when
    /// a tool is blocked or the whole turn dies — a single failed step among several leaves it
    /// null, and the header then says nothing more useful than "1 steps had issues".
    ///
    /// First line only: a tool error is routinely a multi-line traceback, and the rest of it is
    /// one click away in the step's own Error block.
    /// </summary>
    [JsonIgnore]
    public string? LastErrorMessage
    {
        get
        {
            for (var i = Steps.Count - 1; i >= 0; i--)
            {
                if (Steps[i].Status != ToolStepStatus.Failed) continue;
                if (FirstLine(Steps[i].Error ?? Steps[i].Summary) is { } stepText) return stepText;
            }
            return FirstLine(ErrorSummary);
        }
    }

    /// <summary>The error line belongs to the collapsed card only. Once open, the failed step
    /// shows the same text in full in its own Error block, and printing it twice made the top of
    /// an expanded card read as two different failures.</summary>
    [JsonIgnore]
    public bool ShowCollapsedError => !IsExpanded && LastErrorMessage is not null;

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = text.AsSpan().Trim();
        var end = line.IndexOfAny('\r', '\n');
        return (end < 0 ? line : line[..end]).Trim().ToString() is { Length: > 0 } result ? result : null;
    }

    [JsonIgnore] public bool IsTerminal => Status is ToolActivityStatus.Success or ToolActivityStatus.PartialSuccess or ToolActivityStatus.Failed or ToolActivityStatus.Cancelled;
    [JsonIgnore] public bool IsVisible => DisplayMode != ToolDisplayMode.Hidden || !IsTerminal || Status is ToolActivityStatus.Failed or ToolActivityStatus.PartialSuccess or ToolActivityStatus.WaitingForConfirmation or ToolActivityStatus.WaitingForPermission || Steps.Any(step => step.IsHighRisk);
    // Only IsExpanded gates the timeline. Status and DisplayMode choose the *default* expansion
    // (see ToolActivityProjector), never override it — ORing "Failed" in here left the header
    // toggle dead for exactly the runs a user most wants to collapse.
    [JsonIgnore] public bool IsTimelineVisible => IsVisible && IsExpanded;
    [JsonIgnore] public bool IsDetailed => DisplayMode == ToolDisplayMode.Detailed;
    [JsonIgnore] public string StatusGlyph => Status switch
    {
        ToolActivityStatus.Success => "✓",
        ToolActivityStatus.PartialSuccess => "⚠",
        ToolActivityStatus.Failed => "✕",
        ToolActivityStatus.Cancelled => "⊘",
        ToolActivityStatus.WaitingForConfirmation => "?",
        ToolActivityStatus.WaitingForPermission => "⚿",
        _ => "◌",
    };
    [JsonIgnore] public string CompletionLabel => Status switch
    {
        ToolActivityStatus.Success => "Done",
        ToolActivityStatus.PartialSuccess => "Completed with warnings",
        ToolActivityStatus.Failed => "Failed",
        ToolActivityStatus.Cancelled => "Cancelled",
        ToolActivityStatus.WaitingForConfirmation => "Waiting for confirmation",
        ToolActivityStatus.WaitingForPermission => "Waiting for permission",
        _ => "Working...",
    };
    [JsonIgnore] public string AccessibilityName
        => $"Tool activity. {Summary}. {CompletionLabel}. {(IsExpanded ? "Collapse" : "Expand")}";
    [JsonIgnore] public double DurationSeconds => Math.Max(0, ((CompletedAt ?? DateTimeOffset.UtcNow) - StartedAt).TotalSeconds);
    [JsonIgnore] public string DurationLabel => DurationSeconds < 1 ? $"{DurationSeconds * 1000:0} ms" : $"{DurationSeconds:0.0} s";

    /// <summary>
    /// True once the user has opened or closed this card by hand. Auto-expansion stops applying
    /// from that moment for the life of the card.
    /// <para>This is what makes "expand while running" safe. Without it, a user who folds a noisy
    /// card mid-turn has it thrown back open by the very next <c>tool_call</c> — the card fights
    /// them once per tool, which is exactly why auto-expanding on call was removed before.
    /// Not persisted: it is a property of this viewing session, not of the turn.</para>
    /// </summary>
    [JsonIgnore]
    public bool HasUserExpansionOverride { get; private set; }

    public void ToggleExpanded()
    {
        HasUserExpansionOverride = true;
        IsExpanded = !IsExpanded;
    }

    public void RefreshPresentation() => OnPresentationChanged();

    private void OnPresentationChanged()
    {
        OnPropertyChanged(nameof(IsTerminal));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsTimelineVisible));
        OnPropertyChanged(nameof(IsDetailed));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(CompletionLabel));
        OnPropertyChanged(nameof(AccessibilityName));
        OnPropertyChanged(nameof(DurationLabel));
        // Both read through Steps, which raises nothing on the activity when a step's own status
        // flips. ApplyResult calls RefreshPresentation right after settling a step, so this is
        // where a newly failed step reaches the collapsed error line.
        OnPropertyChanged(nameof(LastErrorMessage));
        OnPropertyChanged(nameof(ShowCollapsedError));
    }
}

public sealed partial class ToolStepViewModel : Common.ObservableObject
{
    public ToolStepViewModel()
    {
        Status = ToolStepStatus.Running;
        DisplayName = "Tool";
        StartedAt = DateTimeOffset.UtcNow;
    }

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Sequence { get; set; }
    public string ToolName { get; set; } = "tool";
    public bool IsHighRisk { get; set; }

    [ObservableProperty]
    public partial ToolStepStatus Status { get; set; }

    partial void OnStatusChanged(ToolStepStatus value) => OnPresentationChanged();

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDisplayTarget))]
    public partial string? DisplayTarget { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    [NotifyPropertyChangedFor(nameof(AccessibilityName))]
    public partial string? Summary { get; set; }

    [ObservableProperty]
    public partial string? Arguments { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    [NotifyPropertyChangedFor(nameof(DetailLabel))]
    public partial string? Result { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    [NotifyPropertyChangedFor(nameof(DetailLabel))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset StartedAt { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? CompletedAt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationLabel))]
    public partial double? DurationMs { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [JsonIgnore] public string StatusGlyph => Status switch { ToolStepStatus.Success => "✓", ToolStepStatus.Warning => "⚠", ToolStepStatus.Failed => "✕", ToolStepStatus.Cancelled => "⊘", ToolStepStatus.Pending => "·", _ => "◌" };
    [JsonIgnore] public bool HasDisplayTarget => !string.IsNullOrWhiteSpace(DisplayTarget);
    /// <summary>Still true (and still fed to <see cref="AccessibilityName"/>) even though the
    /// collapsed row no longer prints the summary — the result digest reads usefully to a screen
    /// reader, which has no pointer to expand the step with.</summary>
    [JsonIgnore] public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
    /// <summary>Currently bound by nothing: the step detail no longer renders an Input block
    /// (see <c>ToolActivityView.xaml</c>). Kept because <see cref="Arguments"/> is still
    /// captured and persisted, so restoring that block is a XAML-only change.</summary>
    [JsonIgnore] public bool HasArguments => !string.IsNullOrWhiteSpace(Arguments);
    [JsonIgnore] public bool HasResult => !string.IsNullOrWhiteSpace(Result);
    [JsonIgnore] public bool HasError => !string.IsNullOrWhiteSpace(Error);

    // ---- The step's one output block ----
    //
    // Result and Error are not two different things to show. On a failed step the projector
    // writes the *same* string into both (ToolActivityProjector.ApplyResult), so rendering a
    // "Result" card and an "Error" card meant printing one message twice in two styles. The UI
    // therefore renders exactly one block, and only its styling changes with Status.
    //
    // Both fields stay on the model and in storage: Error is what marks a step as failed for
    // anything reading the data rather than looking at it, and collapsing them here would lose
    // that distinction.

    /// <summary>The single body to render. Prefers <see cref="Error"/> because on a failure it
    /// is the authoritative text; falls back to <see cref="Result"/> for a normal step.</summary>
    [JsonIgnore] public string? DetailText => HasError ? Error : Result;

    [JsonIgnore] public bool HasDetail => !string.IsNullOrWhiteSpace(DetailText);

    /// <summary>Heading over the block. The accent colour beside it comes from
    /// <see cref="Status"/> via <c>ToolStatusToBrushConverter</c>, so a failed step reads as an
    /// error from its chrome and this label without needing a second card.</summary>
    [JsonIgnore] public string DetailLabel => HasError ? "Error" : "Result";

    [JsonIgnore] public string DurationLabel => DurationMs is null ? "" : DurationMs < 1000 ? $"{DurationMs:0} ms" : $"{DurationMs / 1000:0.0} s";
    [JsonIgnore] public string AccessibilityName => $"{StatusGlyph} {DisplayName}{(HasSummary ? ", " + Summary : "")}";

    public void ToggleExpanded() => IsExpanded = !IsExpanded;
    private void OnPresentationChanged()
    {
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(AccessibilityName));
    }
}
