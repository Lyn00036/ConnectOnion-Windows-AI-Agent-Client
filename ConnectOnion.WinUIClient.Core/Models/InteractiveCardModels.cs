using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace ConnectOnion.WinUIClient.Models;

public enum InteractiveCardPhase
{
    Pending,
    Submitting,
    Submitted,
    Completed,
    Cancelled,
    Rejected,
    Expired,
    ConnectionLost,
    Error,
}

public enum InteractiveVisualTone
{
    Neutral,
    Warning,
    Success,
    Danger,
}

public enum PlanReviewAction
{
    Approve,
    RequestChanges,
    Reject,
}

public enum DiffLineKind
{
    Context,
    Addition,
    Deletion,
    Hunk,
    FileHeader,
}

/// <summary>The durable outcome of a proposed file change. This is deliberately separate from
/// the card's expansion flag: state answers what happened to the file, expansion only answers
/// what the user currently wants to see.</summary>
public enum DiffChangeState
{
    Preview,
    Pending,
    Applying,
    Applied,
    Rejected,
    Failed,
    PartiallyApplied,
    Disconnected,
    Unconfirmed,
}

public sealed partial class DiffLineModel : Common.ObservableObject
{
    public int? OldLineNumber { get; set; }
    public int? NewLineNumber { get; set; }
    public string Marker { get; set; } = " ";
    public string Content { get; set; } = "";
    public string RawText { get; set; } = "";
    public DiffLineKind Kind { get; set; }
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial bool IsWrapped { get; set; }
    public string AccessibilityName =>
        $"{Kind}. Old line {OldLineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}, new line {NewLineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}. {Content}";
}

public sealed partial class DiffFileModel : Common.ObservableObject
{
    public string Path { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string DisplayPath => DiffPathFormatter.MiddleEllipsis(FullPath);
    public string FileName => DiffPathFormatter.FileName(FullPath);
    public int Additions { get; set; }
    public int Deletions { get; set; }
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial bool IsExpanded { get; set; }
    public bool ShowHeader { get; set; }
    public ObservableCollection<DiffLineModel> Lines { get; } = new();
    public string ChangeSummary => $"+{Additions}  −{Deletions}";
}

internal static class DiffPathFormatter
{
    public static string MiddleEllipsis(string path, int maximum = 64)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maximum) return path;
        var tailLength = Math.Min(36, maximum / 2 + 4);
        var headLength = maximum - tailLength - 1;
        return $"{path[..headLength]}…{path[^tailLength..]}";
    }

    public static string FileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Proposed changes";
        var index = path.LastIndexOfAny(['/', '\\']);
        return index >= 0 && index < path.Length - 1 ? path[(index + 1)..] : path;
    }

    public static string ContextPath(string path, int segments = 4)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= segments) return path;
        return $"…/{string.Join('/', parts[^segments..])}";
    }
}

/// <summary>Presentation-only parser for the real diff_preview payload. It never creates a
/// response and treats ordinary/Markdown text as context unless unified-diff markers prove the
/// body is a diff.</summary>
public sealed class DiffPreviewModel
{
    public const int MaxPreviewLines = 240;

    private static readonly Regex Hunk = new(
        "^@@ -(?<old>\\d+)(?:,(?<oldCount>\\d+))? \\+(?<new>\\d+)(?:,(?<newCount>\\d+))? @@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ObservableCollection<DiffFileModel> Files { get; } = new();
    public string RawText { get; private set; } = "";
    public bool IsUnifiedDiff { get; private set; }
    public int OmittedLineCount { get; private set; }
    public bool IsPreviewTruncated => OmittedLineCount > 0;
    public string TruncationLabel => Common.CoreStrings.Format(
        "DiffPreviewTruncated", "Preview shortened; {0} additional lines are available when copied.", OmittedLineCount);
    public int Additions => Files.Sum(file => file.Additions);
    public int Deletions => Files.Sum(file => file.Deletions);
    public string FileSummary => Files.Count == 1 ? Files[0].FileName : $"{Files.Count} files changed";
    public string ChangeSummary => $"+{Additions}  −{Deletions}";
    public bool IsWrapped { get; set; }

    public static DiffPreviewModel Parse(string path, string? preview, bool isNewFile = false)
    {
        var model = new DiffPreviewModel { RawText = preview ?? "" };
        var lines = Normalize(preview).ToList();
        model.IsUnifiedDiff = isNewFile
            || lines.Any(line => line.StartsWith("@@", StringComparison.Ordinal))
            || (lines.Any(line => line.StartsWith("--- ", StringComparison.Ordinal))
                && lines.Any(line => line.StartsWith("+++ ", StringComparison.Ordinal)));

        var current = NewFile(path);
        model.Files.Add(current);
        var currentHasContent = false;
        var oldLine = 0;
        var newLine = isNewFile ? 1 : 0;

        foreach (var raw in lines)
        {
            if (raw.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                var nextPath = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path;
                nextPath = TrimDiffPrefix(nextPath);
                if (!currentHasContent && current.Additions == 0 && current.Deletions == 0)
                {
                    current.Path = nextPath;
                    current.FullPath = nextPath;
                }
                else
                {
                    current = NewFile(nextPath);
                    model.Files.Add(current);
                    currentHasContent = false;
                }
                currentHasContent = true;
                model.AddPreviewLine(current, Line(raw, DiffLineKind.FileHeader));
                continue;
            }

            var hunk = model.IsUnifiedDiff ? Hunk.Match(raw) : Match.Empty;
            if (hunk.Success)
            {
                oldLine = int.Parse(hunk.Groups["old"].Value, System.Globalization.CultureInfo.InvariantCulture);
                newLine = int.Parse(hunk.Groups["new"].Value, System.Globalization.CultureInfo.InvariantCulture);
                currentHasContent = true;
                model.AddPreviewLine(current, Line(raw, DiffLineKind.Hunk));
                continue;
            }

            if (!model.IsUnifiedDiff || raw.StartsWith("--- ", StringComparison.Ordinal)
                || raw.StartsWith("+++ ", StringComparison.Ordinal))
            {
                currentHasContent = true;
                model.AddPreviewLine(current, Line(raw,
                    model.IsUnifiedDiff && (raw.StartsWith("--- ", StringComparison.Ordinal)
                        || raw.StartsWith("+++ ", StringComparison.Ordinal))
                        ? DiffLineKind.FileHeader : DiffLineKind.Context));
                continue;
            }

            if (raw.StartsWith('+'))
            {
                currentHasContent = true;
                model.AddPreviewLine(current, Line(raw, DiffLineKind.Addition, null, newLine++));
                current.Additions++;
            }
            else if (raw.StartsWith('-'))
            {
                currentHasContent = true;
                model.AddPreviewLine(current, Line(raw, DiffLineKind.Deletion, oldLine++, null));
                current.Deletions++;
            }
            else
            {
                currentHasContent = true;
                model.AddPreviewLine(current, Line(raw, DiffLineKind.Context, oldLine++, newLine++));
            }
        }

        if (model.Files.Count > 0) model.Files[0].IsExpanded = true;
        var showHeaders = model.Files.Count > 1;
        foreach (var file in model.Files) file.ShowHeader = showHeaders;
        return model;
    }

    private int _previewLineCount;

    private void AddPreviewLine(DiffFileModel file, DiffLineModel line)
    {
        if (_previewLineCount < MaxPreviewLines)
        {
            file.Lines.Add(line);
            _previewLineCount++;
        }
        else
        {
            OmittedLineCount++;
        }
    }

    private static IEnumerable<string> Normalize(string? text)
        => (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static DiffFileModel NewFile(string path)
    {
        var clean = TrimDiffPrefix(string.IsNullOrWhiteSpace(path)
            ? Common.CoreStrings.Get("DiffUnnamedFile", "Proposed changes")
            : path);
        return new DiffFileModel { Path = clean, FullPath = clean };
    }

    private static string TrimDiffPrefix(string path)
        => path.StartsWith("a/", StringComparison.Ordinal) || path.StartsWith("b/", StringComparison.Ordinal)
            ? path[2..] : path;

    private static DiffLineModel Line(
        string raw, DiffLineKind kind, int? oldLine = null, int? newLine = null)
    {
        var hasMarker = kind is DiffLineKind.Addition or DiffLineKind.Deletion;
        return new DiffLineModel
        {
            RawText = raw,
            Marker = hasMarker && raw.Length > 0 ? raw[..1] : " ",
            Content = hasMarker && raw.Length > 0 ? raw[1..] : raw,
            Kind = kind,
            OldLineNumber = oldLine,
            NewLineNumber = newLine,
        };
    }
}
