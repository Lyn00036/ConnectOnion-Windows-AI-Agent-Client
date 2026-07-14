using System;
using System.Collections.Generic;
using System.Linq;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services;

public static class SessionSelection
{
    public static SessionSummary? FindExisting(
        IReadOnlyList<SessionSummary> sessions,
        string? activeSessionId,
        string agentId)
    {
        var active = sessions.FirstOrDefault(
            session => session.Id == activeSessionId && session.AgentId == agentId);
        if (active is not null)
        {
            return active;
        }

        return sessions
            .Where(session => session.AgentId == agentId)
            .OrderByDescending(session => session.UpdatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
