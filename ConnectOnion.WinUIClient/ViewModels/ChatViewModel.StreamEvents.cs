using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Services.Attachments;

namespace ConnectOnion.WinUIClient.ViewModels;

/// <summary>
/// Interactive-turn responses and inbound-image resolution for <see cref="ChatViewModel"/>.
/// The stream → bubble mapping itself now lives in <see cref="Services.Runtime.ChatTurnProjection"/>
/// (shared with app-level persistence); this file only handles the parts that need a live
/// socket for <b>this</b> page: sending the user's answer/approval and caching agent images.
///
/// Interactive bubbles are built by the projection from buffered events; answering one sends
/// the response on the app-owned connection (fetched from the run manager's connection
/// registry). The live control state is intentionally not persisted — once the socket's
/// io.receive() wait is gone, answering is meaningless — matching the reference client.
/// </summary>
public sealed partial class ChatViewModel
{
    private AgentConnectionService? CurrentConnection
        => _session is null ? null : AppServices.RunManager.Connections.Get(_session.Id);

    /// <summary>Hands the just-resolved interactive card's result to the app-level manager so it
    /// survives into persisted history. The live stream never echoes the user's reply, so the
    /// manager can only stamp the chosen result (or "no selection") onto its re-projected card
    /// if we record it here at the moment of answering.</summary>
    private void RecordInteractiveAnswer(ChatMessage message)
    {
        if (_session is null || string.IsNullOrEmpty(message.EventMeta)) return;
        AppServices.RunManager.RecordInteractiveAnswer(_session.Id, message.EventMeta, message.Status);
        // The agent is no longer parked on this turn, so Stop must stop treating it as an open
        // gate it could halt through — from here on the agent is back to being un-interruptible.
        AppServices.RunManager.ClearPendingInteractive(_session.Id);
    }

    public async Task SubmitOnboardInviteCodeAsync(ChatMessage message)
    {
        var connection = CurrentConnection;
        if (connection is null) return;

        var inviteCode = message.OnboardInviteCode.Trim();
        if (inviteCode.Length == 0) return;

        message.IsOnboardingSubmitted = true;
        message.Content = "Invite code submitted. Waiting for the agent to finish onboarding...";
        StatusText = "Submitting invite code...";
        OnPropertyChanged(nameof(Messages));

        try
        {
            await connection.SubmitOnboardInviteCodeAsync(inviteCode);
        }
        catch (Exception ex)
        {
            message.IsOnboardingSubmitted = false;
            message.Content = $"Invite code submission failed: {ex.Message}";
            StatusText = "";
        }
    }

    /// <summary>Submits an ask_user bubble's answer: dynamic fields → JSON, multi-select →
    /// comma-joined checked options, else free text or the selected radio option.</summary>
    public async Task SubmitAskUserAnswerAsync(ChatMessage message)
    {
        if (message.Status != EventStatus.Running) return;

        string answer;
        if (message.AskUserFields.Count > 0)
        {
            var values = new Dictionary<string, string>();
            foreach (var field in message.AskUserFields) values[field.Name] = field.Value;
            answer = JsonSerializer.Serialize(values, AppJsonContext.Default.DictionaryStringString);
        }
        else if (message.AskUserMultiSelect)
        {
            var selected = message.AskUserOptionEntries.Where(o => o.IsChecked).Select(o => o.Text).ToList();
            answer = selected.Count > 0 ? string.Join(", ", selected) : message.AskUserFreeText.Trim();
        }
        else
        {
            answer = !string.IsNullOrWhiteSpace(message.AskUserFreeText)
                ? message.AskUserFreeText.Trim()
                : message.SelectedAskUserOption ?? "";
        }

        if (string.IsNullOrEmpty(answer)) return;

        message.EventMeta = $"Answered: {answer}";
        message.Status = EventStatus.Done;
        RecordInteractiveAnswer(message);
        StatusText = "";
        await (CurrentConnection?.RespondAskUserAsync(answer) ?? Task.CompletedTask);
    }

    /// <summary>
    /// <paramref name="scope"/> is "once" or "session". <paramref name="rejectMode"/> decides what
    /// a rejection means to the agent and the two are genuinely different actions, not a wording
    /// choice: <see cref="ApprovalRejectModes.Hard"/> (Reject) halts the agent's loop and hands
    /// the conversation back to the user, while <see cref="ApprovalRejectModes.Soft"/> (Skip step)
    /// declines this one tool and lets it carry on looking for another way.
    /// </summary>
    public async Task SubmitApprovalAsync(
        ChatMessage message, bool approved, string scope,
        string rejectMode = ApprovalRejectModes.Hard)
    {
        if (message.Status != EventStatus.Running) return;

        message.EventMeta = approved
            ? (scope == "session" ? "Approved for session" : "Approved once")
            : rejectMode == ApprovalRejectModes.Soft ? "Skipped" : "Rejected";
        message.Status = approved ? EventStatus.Done : EventStatus.Error;
        RecordInteractiveAnswer(message);
        StatusText = "";
        await (CurrentConnection?.RespondApprovalAsync(approved, scope, rejectMode) ?? Task.CompletedTask);
    }

    /// <summary>Approving with no feedback sends the SDK's default "proceed" message;
    /// requesting changes with no feedback is a no-op.</summary>
    public async Task SubmitPlanReviewAsync(ChatMessage message, bool approved)
    {
        if (message.Status != EventStatus.Running) return;

        var feedback = message.PlanFeedback.Trim();
        string responseMessage;
        if (approved)
        {
            responseMessage = feedback.Length == 0 ? "Plan approved. Proceed with implementation." : feedback;
            message.EventMeta = "Plan approved";
            message.Status = EventStatus.Done;
        }
        else
        {
            if (feedback.Length == 0) return;
            responseMessage = feedback;
            message.EventMeta = "Changes requested";
            message.Status = EventStatus.Error;
        }

        RecordInteractiveAnswer(message);
        StatusText = "";
        await (CurrentConnection?.RespondPlanReviewAsync(responseMessage) ?? Task.CompletedTask);
    }

    /// <summary>
    /// Decodes/downloads an inbound image off the UI thread and caches it to disk so the
    /// message list and SQLite only ever hold a file path, never the base64 payload. Failure
    /// marks the attachment Failed rather than throwing.
    /// </summary>
    private async Task ResolveAgentImageAsync(string dataUrl, ChatAttachment attachment)
    {
        var resolved = await AttachmentImageCacheService
            .ResolveAndCacheAsync(dataUrl, AppServices.Http)
            .ConfigureAwait(false);

        _dispatcher.TryEnqueue(() =>
        {
            if (resolved is { } value)
            {
                attachment.LocalCachePath = value.LocalPath;
                attachment.MimeType = value.MimeType;
                attachment.Status = AttachmentStatus.Sent;
            }
            else
            {
                attachment.Status = AttachmentStatus.Failed;
                attachment.Error = "Image could not be loaded.";
            }
        });
    }
}
