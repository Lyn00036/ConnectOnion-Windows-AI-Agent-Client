using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using ConnectOnion.WinUIClient.Common;

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
public sealed class ToolActivityViewModel : ObservableObject
{
    private ToolActivityStatus _status = ToolActivityStatus.Running;
    private ToolDisplayMode _displayMode = ToolDisplayMode.Compact;
    private bool _isExpanded = true;
    private string _summary = "Running tools…";
    private DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _completedAt;
    private string? _errorSummary;

    public string TurnId { get; set; } = Guid.NewGuid().ToString("N");
    public ObservableCollection<ToolStepViewModel> Steps { get; set; } = new();

    public ToolActivityStatus Status
    {
        get => _status;
        set
        {
            if (!SetProperty(ref _status, value)) return;
            OnPresentationChanged();
        }
    }

    public ToolDisplayMode DisplayMode
    {
        get => _displayMode;
        set
        {
            if (!SetProperty(ref _displayMode, value)) return;
            if (value == ToolDisplayMode.Detailed) IsExpanded = true;
            OnPresentationChanged();
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value)) OnPropertyChanged(nameof(IsTimelineVisible));
        }
    }

    public string Summary { get => _summary; set => SetProperty(ref _summary, value); }
    public DateTimeOffset StartedAt { get => _startedAt; set => SetProperty(ref _startedAt, value); }
    public DateTimeOffset? CompletedAt { get => _completedAt; set => SetProperty(ref _completedAt, value); }
    public string? ErrorSummary { get => _errorSummary; set => SetProperty(ref _errorSummary, value); }

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

public sealed class ToolStepViewModel : ObservableObject
{
    private ToolStepStatus _status = ToolStepStatus.Running;
    private bool _isExpanded;
    private string _displayName = "Tool";
    private string? _displayTarget;
    private string? _summary;
    private string? _arguments;
    private string? _result;
    private string? _error;
    private DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _completedAt;
    private double? _durationMs;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Sequence { get; set; }
    public string ToolName { get; set; } = "tool";
    public bool IsHighRisk { get; set; }

    public ToolStepStatus Status
    {
        get => _status;
        set
        {
            if (!SetProperty(ref _status, value)) return;
            OnPresentationChanged();
        }
    }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string? DisplayTarget { get => _displayTarget; set { if (SetProperty(ref _displayTarget, value)) OnPropertyChanged(nameof(TimelineText)); } }
    public string? Summary { get => _summary; set { if (SetProperty(ref _summary, value)) { OnPropertyChanged(nameof(HasSummary)); OnPropertyChanged(nameof(TimelineText)); OnPropertyChanged(nameof(HasTimelineText)); } } }
    public string? Arguments { get => _arguments; set => SetProperty(ref _arguments, value); }
    public string? Result { get => _result; set => SetProperty(ref _result, value); }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public DateTimeOffset StartedAt { get => _startedAt; set => SetProperty(ref _startedAt, value); }
    public DateTimeOffset? CompletedAt { get => _completedAt; set => SetProperty(ref _completedAt, value); }
    public double? DurationMs { get => _durationMs; set { if (SetProperty(ref _durationMs, value)) OnPropertyChanged(nameof(DurationLabel)); } }
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

    [JsonIgnore] public string StatusGlyph => Status switch { ToolStepStatus.Success => "✓", ToolStepStatus.Warning => "⚠", ToolStepStatus.Failed => "✕", ToolStepStatus.Cancelled => "⊘", ToolStepStatus.Pending => "·", _ => "◌" };
    [JsonIgnore] public bool HasDisplayTarget => !string.IsNullOrWhiteSpace(DisplayTarget);
    [JsonIgnore] public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
    [JsonIgnore] public string TimelineText => string.Join(" · ", new[] { DisplayTarget, Summary }.Where(value => !string.IsNullOrWhiteSpace(value)));
    [JsonIgnore] public bool HasTimelineText => !string.IsNullOrWhiteSpace(TimelineText);
    [JsonIgnore] public bool HasArguments => !string.IsNullOrWhiteSpace(Arguments);
    [JsonIgnore] public bool HasResult => !string.IsNullOrWhiteSpace(Result);
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
