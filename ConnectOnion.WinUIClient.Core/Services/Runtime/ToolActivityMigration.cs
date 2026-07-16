using System;
using System.Collections.Generic;
using System.Linq;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services.Runtime;

/// <summary>Adapts already-persisted legacy <c>EventKind == "tool"</c> bubbles into the
/// timeline model at load time. No database migration is needed; the next normal save writes
/// the new durable <c>tool_activity</c> payload.</summary>
public static class ToolActivityMigration
{
    public static IReadOnlyList<ChatMessage> UpgradeLegacyToolMessages(IReadOnlyList<ChatMessage> source)
    {
        var upgraded = new List<ChatMessage>();
        for (var i = 0; i < source.Count;)
        {
            if (!source[i].IsToolEvent)
            {
                upgraded.Add(source[i++]);
                continue;
            }

            var legacy = new List<ChatMessage>();
            while (i < source.Count && source[i].IsToolEvent) legacy.Add(source[i++]);
            var activity = new ToolActivityViewModel { IsExpanded = false, DisplayMode = ToolDisplayMode.Compact };
            var projector = new ToolActivityProjector(activity);
            foreach (var old in legacy)
            {
                var step = new ToolStepViewModel
                {
                    Id = old.EventKey ?? $"legacy-{old.Id}",
                    Sequence = activity.Steps.Count + 1,
                    ToolName = old.EventTitle,
                    DisplayName = ToolActivityProjector.DisplayName(old.EventTitle),
                    DisplayTarget = "",
                    Arguments = string.IsNullOrWhiteSpace(old.EventArgs) ? null : ToolActivityProjector.SanitizeJson(old.EventArgs),
                    Result = string.IsNullOrWhiteSpace(old.EventResult) ? null : ToolActivityProjector.SanitizeText(old.EventResult),
                    Summary = old.EventDetail,
                    DurationMs = ParseDuration(old.EventMeta),
                    Status = old.Status == EventStatus.Error ? ToolStepStatus.Failed : old.Status == EventStatus.Running ? ToolStepStatus.Running : ToolStepStatus.Success,
                    IsHighRisk = ToolActivityProjector.IsHighRisk(old.EventTitle),
                };
                activity.Steps.Add(step);
            }
            projector.Complete();
            upgraded.Add(new ChatMessage
            {
                Id = legacy[0].Id,
                Role = ChatRole.Event,
                EventKind = "tool_activity",
                EventTitle = "Tool execution",
                ToolActivity = activity,
            });
        }
        return upgraded;
    }

    private static double? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var number = new string(value.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return double.TryParse(number, out var parsed) ? value.Contains('s') ? parsed * 1000 : parsed : null;
    }
}
