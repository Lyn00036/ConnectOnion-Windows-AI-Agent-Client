using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// A locally indexed conversation belonging to one saved agent. Mirrors the
/// TypeScript <c>SessionSummary</c> type. Timestamps are ISO-8601 strings.
/// Observable so touch/rename updates reflect in the list live.
/// </summary>
public sealed partial class SessionSummary : Common.ObservableObject
{
    public SessionSummary()
    {
        Id = "";
        AgentId = "";
        Title = "";
        CreatedAt = "";
        UpdatedAt = "";
    }

    [ObservableProperty]
    [JsonPropertyName("id")]
    public partial string Id { get; set; }

    [ObservableProperty]
    [JsonPropertyName("agentId")]
    public partial string AgentId { get; set; }

    [ObservableProperty]
    [JsonPropertyName("title")]
    public partial string Title { get; set; }

    [ObservableProperty]
    [JsonPropertyName("remoteSessionId")]
    public partial string? RemoteSessionId { get; set; }

    [ObservableProperty]
    [JsonPropertyName("lastProcessedEventId")]
    public partial string? LastProcessedEventId { get; set; }

    [ObservableProperty]
    [JsonPropertyName("createdAt")]
    public partial string CreatedAt { get; set; }

    [ObservableProperty]
    [JsonPropertyName("updatedAt")]
    public partial string UpdatedAt { get; set; }

    [ObservableProperty]
    [JsonPropertyName("isPinned")]
    public partial bool IsPinned { get; set; }

    private string _mode = Protocol.AgentModes.Safe;

    /// <summary>
    /// The approval mode this conversation's turns run under — one of <see cref="Protocol.AgentModes"/>.
    /// Client-owned: the host will not remember our choice between turns, so it is re-asserted with
    /// every INPUT. An unknown value (a mode a future host introduced, or a hand-edited row) falls
    /// back to Safe rather than being sent on the wire, where the host would ignore it anyway.
    /// Kept hand-written because the setter normalizes the incoming value, which the
    /// <c>[ObservableProperty]</c> generator deliberately cannot express.
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
