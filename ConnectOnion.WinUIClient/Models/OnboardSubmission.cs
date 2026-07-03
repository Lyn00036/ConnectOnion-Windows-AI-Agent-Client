using System.Text.Json.Serialization;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// Trust-gate onboarding input. Mirrors the TypeScript <c>OnboardSubmission</c> type.
/// </summary>
public sealed class OnboardSubmission
{
    [JsonPropertyName("inviteCode")]
    public string? InviteCode { get; set; }

    [JsonPropertyName("payment")]
    public double? Payment { get; set; }
}
