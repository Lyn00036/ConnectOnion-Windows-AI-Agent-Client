using System.Text.Json;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services.Runtime;

/// <summary>Pure builders for the documented interactive response values.</summary>
public static class InteractiveResponseBuilder
{
    public static object? BuildAskUserAnswer(ChatMessage message)
    {
        if (message.AskUserFields.Count > 0)
        {
            var values = new Dictionary<string, string>();
            foreach (var entry in message.AskUserFields) values[entry.Name] = entry.Value.Trim();
            return JsonSerializer.Serialize(values);
        }

        if (!string.IsNullOrWhiteSpace(message.AskUserFreeText))
            return message.AskUserFreeText.Trim();

        var selected = message.AskUserOptionEntries
            .Where(option => option.IsChecked)
            .Select(option => option.Text)
            .ToList();
        if (selected.Count == 0) return null;
        return message.AskUserMultiSelect ? selected.ToArray() : selected[0];
    }

    public static PlanReviewResponse? BuildPlanReviewResponse(
        PlanReviewAction action, string? feedback)
    {
        var trimmed = feedback?.Trim() ?? "";
        return action switch
        {
            PlanReviewAction.Approve => new PlanReviewResponse(
                trimmed,
                "Plan approved", false),
            PlanReviewAction.RequestChanges when trimmed.Length > 0 => new PlanReviewResponse(
                trimmed, "Changes requested", false),
            PlanReviewAction.Reject => new PlanReviewResponse(
                $"rejected: {(trimmed.Length == 0 ? "No reason provided" : trimmed)}",
                "Plan rejected", true),
            _ => null,
        };
    }
}

public sealed record PlanReviewResponse(string Message, string Outcome, bool Rejected);
