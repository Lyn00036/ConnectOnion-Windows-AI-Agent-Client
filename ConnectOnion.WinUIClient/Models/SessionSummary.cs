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
    private string _mode = Protocol.AgentModes.Safe;

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
    /// The approval mode this conversation's turns run under — one of <see cref="Protocol.AgentModes"/>.
    /// Client-owned: the host will not remember our choice between turns, so it is re-asserted with
    /// every INPUT. An unknown value (a mode a future host introduced, or a hand-edited row) falls
    /// back to Safe rather than being sent on the wire, where the host would ignore it anyway.
    /// </summary>
    [JsonPropertyName("mode")]
    public string Mode
    {
        get => _mode;
        set => SetProperty(ref _mode, Protocol.AgentModes.IsValid(value) ? value : Protocol.AgentModes.Safe);
    }

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
