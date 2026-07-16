
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services.Runtime;

/// <summary>
/// Pure UI projection for raw tool stream frames. It owns correlation, result
/// classification, display-name/target mapping and sensitive-data redaction; views only bind
/// to the resulting activity. A result is never matched by tool name.
/// </summary>
public sealed class ToolActivityProjector
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "cookie", "set-cookie", "token", "access_token", "refresh_token",
        "api_key", "apikey", "password", "secret", "client_secret", "private_key",
    };
    private static readonly string[] WarningPatterns =
    {
        "access denied", "permission denied", "unauthorized", "forbidden", "403", "404",
        "empty result", "no content", "captcha", "rate limit", "redirect failed",
    };
    private static readonly string[] FailurePatterns =
    {
        "timeout", "connection refused", "failed", " error", "exception", "network error",
    };
    private static readonly string[] HighRiskTokens =
    {
        "send", "delete", "write", "remove", "command", "execute", "upload", "download",
        "payment", "purchase", "submit", "permission", "account", "database",
    };
    private static readonly string[] UrlPropertyNames = { "url", "uri", "href" };
    private static readonly string[] PathPropertyNames = { "path", "file", "file_path", "filename" };

    public ToolActivityViewModel Activity { get; }

    public ToolActivityProjector(ToolActivityViewModel activity) => Activity = activity;

    public ToolStepViewModel ApplyCall(AgentStreamEvent e)
    {
        using var doc = JsonDocument.Parse(e.RawJson);
        var root = doc.RootElement;
        var toolName = String(root, "name") ?? String(root, "tool") ?? "tool";
        var id = CorrelationId(root, e) ?? $"unmatched-call-{Activity.Steps.Count + 1}";
        var existing = Activity.Steps.FirstOrDefault(step => step.Id == id);
        if (existing is not null) return existing; // duplicate stream frame / page replay

        var args = ObjectJson(root, "args") ?? ObjectJson(root, "arguments") ?? "{}";
        var step = new ToolStepViewModel
        {
            Id = id,
            Sequence = Activity.Steps.Count + 1,
            ToolName = toolName,
            DisplayName = DisplayName(toolName),
            DisplayTarget = DisplayTarget(toolName, args),
            Arguments = SanitizeJson(args),
            StartedAt = Timestamp(root) ?? DateTimeOffset.UtcNow,
            Status = ToolStepStatus.Running,
            IsHighRisk = IsHighRisk(toolName),
        };
        Activity.Steps.Add(step);
        Activity.Status = ToolActivityStatus.Running;
        Activity.Summary = RunningSummary(step);
        Activity.IsExpanded = true;
        return step;
    }

    public ToolStepViewModel? ApplyResult(AgentStreamEvent e)
    {
        using var doc = JsonDocument.Parse(e.RawJson);
        var root = doc.RootElement;
        var id = CorrelationId(root, e);
        if (string.IsNullOrWhiteSpace(id)) return null; // never guess from the tool name
        var step = Activity.Steps.FirstOrDefault(item => item.Id == id);
        if (step is null) return null;

        // Capped before anything else touches it. A tool result is unbounded — a page scrape or a
        // file read is routinely hundreds of KB — and this string is retained for the life of the
        // conversation (in the message list, in ConversationCache, and serialized into the
        // message's event_args row). The card renders it in a 180px scroller, so nobody is reading
        // past this; the untruncated payload is already in trace_events if it's ever needed.
        var result = Cap(String(root, "result") ?? String(root, "message") ?? String(root, "error") ?? "");
        var transportStatus = String(root, "status");
        var classification = Classify(transportStatus, result);
        step.Status = classification.Status;
        step.Result = string.IsNullOrWhiteSpace(result) ? null : SanitizeText(result);
        step.Error = classification.Status == ToolStepStatus.Failed ? SanitizeText(result) : null;
        step.Summary = classification.Summary;
        step.CompletedAt = Timestamp(root) ?? DateTimeOffset.UtcNow;
        step.DurationMs = Number(root, "timing_ms") ?? Number(root, "duration_ms")
            ?? Math.Max(0, (step.CompletedAt.Value - step.StartedAt).TotalMilliseconds);
        Activity.Summary = RunningSummary(Activity.Steps.LastOrDefault(item => item.Status == ToolStepStatus.Running) ?? step);
        Activity.RefreshPresentation();
        return step;
    }

    public ToolStepViewModel AddBlocked(AgentStreamEvent e)
    {
        using var doc = JsonDocument.Parse(e.RawJson);
        var root = doc.RootElement;
        var toolName = String(root, "tool") ?? String(root, "name") ?? "tool";
        var step = new ToolStepViewModel
        {
            Id = CorrelationId(root, e) ?? $"blocked-{Activity.Steps.Count + 1}",
            Sequence = Activity.Steps.Count + 1,
            ToolName = toolName,
            DisplayName = DisplayName(toolName),
            DisplayTarget = DisplayTarget(toolName, ObjectJson(root, "args") ?? "{}"),
            Status = ToolStepStatus.Failed,
            Summary = FriendlyProblem(String(root, "message") ?? String(root, "reason") ?? "Tool action blocked"),
            Error = SanitizeText(String(root, "message") ?? String(root, "reason") ?? "Tool action blocked"),
            Result = SanitizeText(String(root, "command") ?? ""),
            IsHighRisk = IsHighRisk(toolName),
            StartedAt = Timestamp(root) ?? DateTimeOffset.UtcNow,
            CompletedAt = Timestamp(root) ?? DateTimeOffset.UtcNow,
        };
        Activity.Steps.Add(step);
        Activity.Status = ToolActivityStatus.Failed;
        Activity.ErrorSummary = step.Summary;
        Activity.Summary = $"Tool execution failed · view details";
        Activity.IsExpanded = true;
        return step;
    }

    public void WaitForConfirmation(bool permission = false)
    {
        Activity.Status = permission ? ToolActivityStatus.WaitingForPermission : ToolActivityStatus.WaitingForConfirmation;
        Activity.Summary = permission ? "Waiting for permission…" : "Waiting for your confirmation…";
        Activity.IsExpanded = true;
    }

    public void Complete(ToolActivityStatus? terminalStatus = null, string? error = null)
    {
        foreach (var running in Activity.Steps.Where(step => step.Status is ToolStepStatus.Running or ToolStepStatus.Pending))
        {
            running.Status = terminalStatus == ToolActivityStatus.Cancelled ? ToolStepStatus.Cancelled : ToolStepStatus.Warning;
            running.Summary ??= terminalStatus == ToolActivityStatus.Cancelled ? "Cancelled" : "No result returned";
            running.CompletedAt = DateTimeOffset.UtcNow;
            running.DurationMs ??= Math.Max(0, (running.CompletedAt.Value - running.StartedAt).TotalMilliseconds);
        }

        var hasFailed = Activity.Steps.Any(step => step.Status == ToolStepStatus.Failed);
        var hasWarning = Activity.Steps.Any(step => step.Status == ToolStepStatus.Warning);
        Activity.Status = terminalStatus ?? (hasFailed ? ToolActivityStatus.PartialSuccess : hasWarning ? ToolActivityStatus.PartialSuccess : ToolActivityStatus.Success);
        Activity.CompletedAt = DateTimeOffset.UtcNow;
        Activity.ErrorSummary = error is null ? Activity.ErrorSummary : FriendlyProblem(error);
        var count = Activity.Steps.Count;
        Activity.Summary = Activity.Status switch
        {
            ToolActivityStatus.Failed => $"Tool execution failed · {Activity.ErrorSummary ?? "view details"}",
            ToolActivityStatus.Cancelled => $"Tool execution cancelled · {count} steps",
            ToolActivityStatus.PartialSuccess => $"Tool execution completed · {Activity.Steps.Count(step => step.Status is ToolStepStatus.Warning or ToolStepStatus.Failed)} steps had issues",
            _ => $"Tool execution completed · {count} steps · {Activity.DurationLabel}",
        };
        Activity.IsExpanded = Activity.DisplayMode == ToolDisplayMode.Detailed || Activity.Status == ToolActivityStatus.Failed;
        Activity.RefreshPresentation();
    }

    public static string DisplayName(string toolName) => toolName.ToLowerInvariant() switch
    {
        "open_browser" => "Open browser", "go_to" or "navigate" => "Navigate to page", "get_text" => "Read page content",
        "click" => "Click element", "type_text" or "type" => "Type text", "take_screenshot" => "Take screenshot",
        "search_web" or "search" => "Search web", "read_file" => "Read file", "write_file" => "Write file",
        "send_email" => "Send email", "delete_file" => "Delete file", "run_command" or "execute_command" => "Run command",
        "upload_file" => "Upload file", "download_file" => "Download file", _ => ToReadableName(toolName),
    };

    public static bool IsHighRisk(string toolName)
    {
        var normalized = toolName.ToLowerInvariant();
        return HighRiskTokens.Any(token => normalized.Contains(token, StringComparison.Ordinal));
    }

    public static string SanitizeJson(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(Redact(doc.RootElement));
        }
        catch { return SanitizeText(raw); }
    }

    /// <summary>How much of a tool result the card keeps in memory. Generous next to what the
    /// scroller shows, small next to what a scrape returns.</summary>
    private const int MaxRetainedResultChars = 8 * 1024;

    private static string Cap(string value)
        => value.Length <= MaxRetainedResultChars
            ? value
            : value[..MaxRetainedResultChars] + "\n… (truncated)";

    public static string SanitizeText(string value)
        => Regex.Replace(value ?? "", "(?i)(authorization|cookie|token|api[_-]?key|password|secret)\\s*[:=]\\s*([^\\s,;]+)", "$1=[hidden]");

    private static object? Redact(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => SensitiveKeys.Contains(property.Name) ? "[hidden]" : Redact(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(Redact).ToList(),
        JsonValueKind.String => SanitizeText(element.GetString() ?? ""),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static string? CorrelationId(JsonElement root, AgentStreamEvent e)
        => String(root, "tool_id") ?? String(root, "toolCallId") ?? String(root, "call_id") ?? String(root, "callId") ?? String(root, "invocation_id") ?? String(root, "invocationId") ?? String(root, "id") ?? e.EventId;
    private static string? String(JsonElement root, string property) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static double? Number(JsonElement root, string property) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
    private static string? ObjectJson(JsonElement root, string property) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? value.GetRawText() : null;

    private static DateTimeOffset? Timestamp(JsonElement root)
    {
        var ts = Number(root, "ts");
        if (ts is null) return null;
        try { return ts > 1_000_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds((long)ts.Value) : DateTimeOffset.FromUnixTimeSeconds((long)ts.Value); }
        catch { return null; }
    }

    private static string DisplayTarget(string toolName, string args)
    {
        try
        {
            using var doc = JsonDocument.Parse(args);
            var root = doc.RootElement;
            foreach (var name in UrlPropertyNames)
            {
                if (String(root, name) is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.Host;
            }
            if (String(root, "query") is { Length: > 0 } query) return $"Search: {Truncate(query, 48)}";
            foreach (var name in PathPropertyNames)
            {
                if (String(root, name) is { Length: > 0 } path) return path.Replace('\\', '/').Split('/').Last();
            }
            if (String(root, "text") is { Length: > 0 } text) return Truncate(text, 48);
        }
        catch { }
        return "";
    }

    private static (ToolStepStatus Status, string Summary) Classify(string? transportStatus, string result)
    {
        var text = result ?? "";
        if (string.Equals(transportStatus, "error", StringComparison.OrdinalIgnoreCase) || FailurePatterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            return (ToolStepStatus.Failed, FriendlyProblem(text));
        if (string.IsNullOrWhiteSpace(text) || WarningPatterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            return (ToolStepStatus.Warning, string.IsNullOrWhiteSpace(text) ? "No content returned" : FriendlyProblem(text));
        return (ToolStepStatus.Success, ResultSummary(text));
    }

    private static string RunningSummary(ToolStepViewModel step)
    {
        var completed = step.Sequence - 1;
        return completed > 0 ? $"{completed} steps completed, {step.DisplayName}…" : $"{step.DisplayName}…";
    }
    private static string FriendlyProblem(string value)
    {
        if (value.Contains("access denied", StringComparison.OrdinalIgnoreCase) || value.Contains("forbidden", StringComparison.OrdinalIgnoreCase) || value.Contains("403")) return "Page access denied";
        if (value.Contains("permission denied", StringComparison.OrdinalIgnoreCase) || value.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)) return "Permission denied";
        if (value.Contains("timeout", StringComparison.OrdinalIgnoreCase)) return "Request timed out";
        if (value.Contains("connection refused", StringComparison.OrdinalIgnoreCase)) return "Connection refused";
        if (value.Contains("captcha", StringComparison.OrdinalIgnoreCase)) return "CAPTCHA required";
        if (value.Contains("rate limit", StringComparison.OrdinalIgnoreCase)) return "Rate limited";
        if (value.Contains("no content", StringComparison.OrdinalIgnoreCase) || value.Contains("empty", StringComparison.OrdinalIgnoreCase)) return "No content found";
        return Truncate(SanitizeText(value).Replace('\r', ' ').Replace('\n', ' '), 72);
    }
    private static string ResultSummary(string value) => Truncate(SanitizeText(value).Replace('\r', ' ').Replace('\n', ' '), 72);
    private static string ToReadableName(string value) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.Replace('_', ' ').Replace('-', ' '));
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..Math.Max(0, max - 1)] + "…";
}
