using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// A saved agent connection. Mirrors the TypeScript <c>AgentConfig</c> type.
/// Observable so the editor form and the agent list stay in sync via two-way
/// binding; System.Text.Json serializes the public properties and ignores the
/// change notifications. Uses <c>[ObservableProperty]</c> on partial properties
/// (the WinUI-recommended form); non-null string defaults are set in the ctor
/// because a partial-property declaration can't carry an initializer.
/// </summary>
public sealed partial class AgentConfig : Common.ObservableObject
{
    public AgentConfig()
    {
        Id = "";
        Name = "";
        Address = "";
    }

    [ObservableProperty]
    [JsonPropertyName("id")]
    public partial string Id { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [JsonPropertyName("name")]
    public partial string Name { get; set; }

    /// <summary>User-facing form of <see cref="Name"/>. The stored and transmitted value remains
    /// unchanged; every UI surface uses this same projection as the sidebar.</summary>
    [JsonIgnore]
    public string DisplayName => FriendlyAgentName.From(Name);

    [ObservableProperty]
    [JsonPropertyName("address")]
    public partial string Address { get; set; }

    [ObservableProperty]
    [JsonPropertyName("directUrl")]
    public partial string? DirectUrl { get; set; }

    /// <summary>Invite code submitted automatically if the agent's trust gate asks for onboarding.</summary>
    [ObservableProperty]
    [JsonPropertyName("inviteCode")]
    public partial string? InviteCode { get; set; }

    /// <summary>Cached /info response JSON, persisted across restarts.</summary>
    [JsonPropertyName("infoJson")]
    public string? InfoJson { get; set; }

    /// <summary>Timestamp of the last successful /info fetch.</summary>
    [JsonPropertyName("infoUpdatedAt")]
    public string? InfoUpdatedAt { get; set; }
}
