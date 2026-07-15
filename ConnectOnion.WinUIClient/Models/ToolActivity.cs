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
        IsExpanded = true;
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
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset StartedAt { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? CompletedAt { get; set; }

    [ObservableProperty]
    public partial string? ErrorSummary { get; set; }

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
    [JsonIgnore] public string AccessibilityName => $"Tool execution: {Summary}";
    [JsonIgnore] public double DurationSeconds => Math.Max(0, ((CompletedAt ?? DateTimeOffset.UtcNow) - StartedAt).TotalSeconds);
    [JsonIgnore] public string DurationLabel => DurationSeconds < 1 ? $"{DurationSeconds * 1000:0} ms" : $"{DurationSeconds:0.0} s";

    public void ToggleExpanded() => IsExpanded = !IsExpanded;
    public void RefreshPresentation() => OnPresentationChanged();

    private void OnPresentationChanged()
    {
        OnPropertyChanged(nameof(IsTerminal));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsTimelineVisible));
        OnPropertyChanged(nameof(IsDetailed));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(AccessibilityName));
        OnPropertyChanged(nameof(DurationLabel));
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
    [NotifyPropertyChangedFor(nameof(TimelineText))]
    public partial string? DisplayTarget { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    [NotifyPropertyChangedFor(nameof(TimelineText))]
    [NotifyPropertyChangedFor(nameof(HasTimelineText))]
    public partial string? Summary { get; set; }

    [ObservableProperty]
    public partial string? Arguments { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(ShowResult))]
    public partial string? Result { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowResult))]
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
    [JsonIgnore] public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
    [JsonIgnore] public string TimelineText => string.Join(" · ", new[] { DisplayTarget, Summary }.Where(value => !string.IsNullOrWhiteSpace(value)));
    [JsonIgnore] public bool HasTimelineText => !string.IsNullOrWhiteSpace(TimelineText);
    [JsonIgnore] public bool HasArguments => !string.IsNullOrWhiteSpace(Arguments);
    [JsonIgnore] public bool HasResult => !string.IsNullOrWhiteSpace(Result);
    // When a step failed, the error text already carries the failure detail and the Result often
    // mirrors it — showing both duplicates the same message. Suppress the Result body once an
    // error is present so the expanded step shows the failure exactly once.
    [JsonIgnore] public bool ShowResult => HasResult && !HasError;
    [JsonIgnore] public bool HasError => !string.IsNullOrWhiteSpace(Error);
    [JsonIgnore] public string DurationLabel => DurationMs is null ? "" : DurationMs < 1000 ? $"{DurationMs:0} ms" : $"{DurationMs / 1000:0.0} s";
    [JsonIgnore] public string AccessibilityName => $"{StatusGlyph} {DisplayName}{(HasSummary ? ", " + Summary : "")}";

    public void ToggleExpanded() => IsExpanded = !IsExpanded;
    private void OnPresentationChanged()
    {
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(AccessibilityName));
    }
}
