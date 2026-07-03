using System.Text.Json.Serialization;
using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Models;

public enum ChatRole
{
    User,
    Agent,
    Event,
}

/// <summary>
/// A single rendered chat bubble. Mirrors the TypeScript <c>ChatMessage</c> type.
/// </summary>
public sealed class ChatMessage : ObservableObject
{
    private string _onboardInviteCode = "";
    private string _content = "";
    private string _eventTitle = "";
    private string? _eventDetail;
    private bool _isOnboardingSubmitted;

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("role")]
    public ChatRole Role { get; set; }

    [JsonPropertyName("content")]
    public string Content { get => _content; set => SetProperty(ref _content, value); }

    [JsonPropertyName("eventTitle")]
    public string EventTitle { get => _eventTitle; set => SetProperty(ref _eventTitle, value); }

    [JsonPropertyName("eventDetail")]
    public string? EventDetail { get => _eventDetail; set => SetProperty(ref _eventDetail, value); }

    [JsonPropertyName("agentName")]
    public string? AgentName { get; set; }

    [JsonPropertyName("isOnboarding")]
    public bool IsOnboarding { get; set; }

    [JsonIgnore]
    public string OnboardInviteCode
    {
        get => _onboardInviteCode;
        set => SetProperty(ref _onboardInviteCode, value);
    }

    [JsonIgnore]
    public bool IsOnboardingInputEnabled => IsOnboarding && !IsOnboardingSubmitted;

    [JsonIgnore]
    public bool IsUser => Role == ChatRole.User;

    [JsonIgnore]
    public bool IsAgent => Role == ChatRole.Agent;

    [JsonIgnore]
    public bool IsEvent => Role == ChatRole.Event;

    [JsonIgnore]
    public bool IsOnboardingSubmitted
    {
        get => _isOnboardingSubmitted;
        set
        {
            if (!SetProperty(ref _isOnboardingSubmitted, value)) return;
            OnPropertyChanged(nameof(IsOnboardingInputEnabled));
        }
    }
}
