using System.Text.Json.Serialization;
using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// A saved agent connection. Mirrors the TypeScript <c>AgentConfig</c> type.
/// Observable so the editor form and the agent list stay in sync via two-way
/// binding; System.Text.Json serializes the public properties and ignores the
/// change notifications.
/// </summary>
public sealed class AgentConfig : ObservableObject
{
    private string _id = "";
    private string _name = "";
    private string _address = "";
    private string? _directUrl;
    private string? _inviteCode;

    [JsonPropertyName("id")]
    public string Id { get => _id; set => SetProperty(ref _id, value); }

    [JsonPropertyName("name")]
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    [JsonPropertyName("address")]
    public string Address { get => _address; set => SetProperty(ref _address, value); }

    [JsonPropertyName("directUrl")]
    public string? DirectUrl { get => _directUrl; set => SetProperty(ref _directUrl, value); }

    /// <summary>Invite code submitted automatically if the agent's trust gate asks for onboarding.</summary>
    [JsonPropertyName("inviteCode")]
    public string? InviteCode { get => _inviteCode; set => SetProperty(ref _inviteCode, value); }

    /// <summary>Cached /info response JSON, persisted across restarts.</summary>
    [JsonPropertyName("infoJson")]
    public string? InfoJson { get; set; }

    /// <summary>Timestamp of the last successful /info fetch.</summary>
    [JsonPropertyName("infoUpdatedAt")]
    public string? InfoUpdatedAt { get; set; }
}
