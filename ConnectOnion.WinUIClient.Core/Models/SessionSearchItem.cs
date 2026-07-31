using System;
using System.Linq;
using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// WinUI-free projection consumed by the shell's conversation search overlay.
/// </summary>
public sealed class SessionSearchItem
{
    public string SessionId { get; init; } = "";
    public string AgentId { get; init; } = "";
    public string Title { get; init; } = "";
    public string AgentName { get; init; } = "";
    public string UpdatedAt { get; init; } = "";

    public string AgentDisplayName => FriendlyAgentName.From(AgentName);
    public string AccessibilityName => $"{Title}, {AgentDisplayName}";

    public bool Matches(string query)
    {
        var terms = query.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return true;

        var searchable = $"{Title}\n{AgentName}\n{AgentDisplayName}";
        return terms.All(term =>
            searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
