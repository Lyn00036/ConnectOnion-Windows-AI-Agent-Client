using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// A locally indexed conversation belonging to one saved agent. Mirrors the
/// TypeScript <c>SessionSummary</c> type. Timestamps are ISO-8601 strings.
/// Observable so touch/rename updates reflect in the list live.
/// </summary>
public sealed class SessionSummary : ObservableObject
{
    private string _id = "";
    private string _agentId = "";
    private string _title = "";
    private string? _remoteSessionId;
    private string? _lastProcessedEventId;
    private string _createdAt = "";
    private string _updatedAt = "";
    private bool _isPinned;

    [JsonPropertyName("id")]
    public string Id { get => _id; set => SetProperty(ref _id, value); }

    [JsonPropertyName("agentId")]
    public string AgentId { get => _agentId; set => SetProperty(ref _agentId, value); }

    [JsonPropertyName("title")]
    public string Title { get => _title; set => SetProperty(ref _title, value); }

    [JsonPropertyName("remoteSessionId")]
    public string? RemoteSessionId { get => _remoteSessionId; set => SetProperty(ref _remoteSessionId, value); }

    [JsonPropertyName("lastProcessedEventId")]
    public string? LastProcessedEventId { get => _lastProcessedEventId; set => SetProperty(ref _lastProcessedEventId, value); }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get => _createdAt; set => SetProperty(ref _createdAt, value); }

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get => _updatedAt; set => SetProperty(ref _updatedAt, value); }

    [JsonPropertyName("isPinned")]
    public bool IsPinned { get => _isPinned; set => SetProperty(ref _isPinned, value); }

    /// <summary>
    /// Builds a fresh "Conversation N" session for an agent, numbering it after
    /// the agent's existing conversations. This is the single source of truth for
    /// new-session shape (id/title/timestamps) — callers still Add/Insert it into
    /// their list and persist. <paramref name="existing"/> is only read to compute N.
    /// </summary>
    public static SessionSummary NewConversation(string agentId, IEnumerable<SessionSummary> existing)
    {
        var count = existing.Count(s => s.AgentId == agentId) + 1;
        var timestamp = DateTime.UtcNow.ToString("o");
        return new SessionSummary
        {
            Id = Guid.NewGuid().ToString(),
            AgentId = agentId,
            Title = $"Conversation {count}",
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
    }
}
