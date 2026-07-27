using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// A saved agent connection. Mirrors the TypeScript <c>AgentConfig</c> type.
/// Observable so the editor form and the agent list stay in sync via two-way
/// binding; System.Text.Json serializes the public properties and ignores the
/// change notifications.
/// </summary>
public sealed partial class AgentConfig : Common.ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("id")]
    private string id = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [property: JsonPropertyName("name")]
    private string name = "";

    /// <summary>
    /// User-facing form of <see cref="Name"/>.
    /// The stored and transmitted value remains unchanged; every UI surface
    /// uses this same projection as the sidebar.
    /// </summary>
    [JsonIgnore]
    public string DisplayName => FriendlyAgentName.From(Name);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRelayOnly))]
    [property: JsonPropertyName("address")]
    private string address = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRelayOnly))]
    [property: JsonPropertyName("directUrl")]
    private string? directUrl;

    /// <summary>
    /// True when this saved entry resolves through the relay address rather
    /// than a user-supplied direct endpoint. Only these agents have a stable
    /// public web-chat URL worth sharing.
    /// </summary>
    [JsonIgnore]
    public bool IsRelayOnly =>
        !string.IsNullOrWhiteSpace(Address)
        && string.IsNullOrWhiteSpace(DirectUrl);

    /// <summary>
    /// Relative path of the user-selected local agent icon.
    /// Null or empty means the default name-initial avatar is used.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomIcon))]
    [property: JsonPropertyName("iconPath")]
    private string? iconPath;

    /// <summary>
    /// True when this agent has a custom local icon.
    /// </summary>
    [JsonIgnore]
    public bool HasCustomIcon =>
        !string.IsNullOrWhiteSpace(IconPath);

    /// <summary>
    /// Invite code submitted automatically if the agent's trust gate
    /// asks for onboarding.
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("inviteCode")]
    private string? inviteCode;

    /// <summary>
    /// Cached /info response JSON, persisted across restarts.
    /// </summary>
    [JsonPropertyName("infoJson")]
    public string? InfoJson { get; set; }

    /// <summary>
    /// Timestamp of the last successful /info fetch.
    /// </summary>
    [JsonPropertyName("infoUpdatedAt")]
    public string? InfoUpdatedAt { get; set; }
}
