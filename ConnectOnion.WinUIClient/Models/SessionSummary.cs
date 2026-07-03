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
    private string _createdAt = "";
    private string _updatedAt = "";

    [JsonPropertyName("id")]
    public string Id { get => _id; set => SetProperty(ref _id, value); }

    [JsonPropertyName("agentId")]
    public string AgentId { get => _agentId; set => SetProperty(ref _agentId, value); }

    [JsonPropertyName("title")]
    public string Title { get => _title; set => SetProperty(ref _title, value); }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get => _createdAt; set => SetProperty(ref _createdAt, value); }

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get => _updatedAt; set => SetProperty(ref _updatedAt, value); }
}
