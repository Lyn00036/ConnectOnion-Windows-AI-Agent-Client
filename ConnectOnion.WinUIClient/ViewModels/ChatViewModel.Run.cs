using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConnectOnion.Protocol;
using ConnectOnion.Protocol.Runtime;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Services.Attachments;
using ConnectOnion.WinUIClient.Services.Notifications;
using ConnectOnion.WinUIClient.Services.Runtime;
using ConnectOnion.WinUIClient.Data;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace ConnectOnion.WinUIClient.ViewModels;

/// <summary>
/// <see cref="ChatViewModel"/>: the live turn — submitting a send, stopping it, switching mode,
/// and folding the app-level run's snapshots back into this page's message list. Everything here
/// is downstream of <c>AgentSessionManager</c>; the view model owns none of it, it only mirrors
/// the run's state onto the UI thread.
/// </summary>
public sealed partial class ChatViewModel
{
    public async Task SendAsync(string text, IReadOnlyList<PendingAttachment>? attachments = null)
    {
        var hasAttachments = attachments is { Count: > 0 };
        text = AttachmentPromptService.Resolve(text, attachments);
        if ((text.Length == 0 && !hasAttachments) || _agent is null || _session is null || IsProcessing) return;

        // A new send supersedes any prior failure — the old attempt is no longer retryable.
        CanRetry = false;

        // Optimistic user bubble for instant feedback. The manager also persists an
        // equivalent user message (this page won't reload during the session, so there is
        // no double; a re-opened page loads the persisted one).
        var userMessage = new ChatMessage { Id = NextId(), Role = ChatRole.User, Content = text };
        if (hasAttachments)
        {
            foreach (var attachment in attachments!)
            {
                userMessage.Attachments.Add(new ChatAttachment
                {
                    Kind = attachment.Kind,
                    FileName = attachment.FileName,
                    MimeType = attachment.MimeType,
                    SizeBytes = attachment.SizeBytes,
                    LocalCachePath = attachment.LocalPath,
                    Status = AttachmentStatus.Sent,
                });
            }
        }
        Messages.Add(userMessage);
        TouchSession(text);

        IsProcessing = true;
        CanStop = false;
        IsStopping = false;
        StatusText = "Connecting...";
        try
        {
            // Hand off to the app-level manager. It persists the user message, opens/reuses
            // the connection, and starts the run; the reply arrives back through our
            // subscription (ApplyRunSnapshot), not by awaiting here. The mode goes with every
            // send because the host does not keep ours between turns.
            var result = await _runManager.SendMessageAsync(_agent, _session.Id, text, attachments, CurrentMode);
            // Held so the send can be taken back while it is still queued — see CancelSendAsync.
            // Dropped the moment the run starts sending, because from then on it cannot be undone.
            _pendingSend = new PendingSend(result, text, attachments ?? Array.Empty<PendingAttachment>(), userMessage);
        }
        catch (InvalidOperationException)
        {
            // A run for this conversation is already active — ignore the duplicate send.
        }
        catch (Exception ex)
        {
            LogSendFailure(_logger, _session?.Id, ex);
            IsProcessing = false;
            CanStop = false;
            StatusText = $"Send failed: {UserFacingError(ex)}";
        }
    }

    /// <summary>The message currently in flight, retained only while it can still be taken back.</summary>
    private sealed record PendingSend(
        SendMessageResult Result,
        string Text,
        IReadOnlyList<PendingAttachment> Attachments,
        ChatMessage Bubble);

    private PendingSend? _pendingSend;

    /// <summary>
    /// Raised when a send is cancelled before it left, carrying what was on the composer so the
    /// page can put it back. The view model does not touch the composer itself — that is a
    /// control, and this type is meant to stay WinUI-free of it.
    /// </summary>
    public event Action<string, IReadOnlyList<PendingAttachment>>? SendCancelled;

    /// <summary>
    /// True while the message has been submitted but not yet handed to the agent, which is the
    /// only window in which it can be un-sent. The run reaches Running off the connection's
    /// InputSent event, so anything short of Running means the INPUT frame has not gone out.
    /// </summary>
    [ObservableProperty]
    public partial bool CanCancelSend { get; private set; }

    /// <summary>
    /// Takes back a message that has not reached the agent: cancels the run, erases the bubble and
    /// the rows written for it, and hands the text and attachments back to the composer.
    ///
    /// Silently does nothing if the window has closed in the meantime — the manager re-checks the
    /// run's status and refuses, and this is a race the user can lose by a millisecond, so losing
    /// it must simply leave the send running rather than half-undo it.
    /// </summary>
    public async Task CancelSendAsync()
    {
        if (_session is null || _pendingSend is not { } pending) return;

        _pendingSend = null;
        CanCancelSend = false;

        var cancelled = await _runManager.CancelSendAsync(
            _session.Id, pending.Result.RunId, pending.Result.PersistedMessageId);
        if (!cancelled) return;

        Messages.Remove(pending.Bubble);
        IsProcessing = false;
        CanStop = false;
        IsStopping = false;
        StatusText = "";
        SendCancelled?.Invoke(pending.Text, pending.Attachments);
    }

    /// <summary>Sends one graceful-stop request and keeps rendering until OUTPUT/ERROR.
    /// Repeated clicks are ignored while the current iteration finishes.</summary>
    public async Task StopAsync()
    {
        if (_session is null || !CanStop || IsStopping) return;

        IsStopping = true;
        StatusText = "Stopping...";
        try
        {
            var outcome = await _runManager.RequestStopAsync(_session.Id);
            if (outcome == StopOutcome.NotRunning)
            {
                IsStopping = false;
                CanStop = false;
                StatusText = "";
            }
        }
        catch (Exception ex)
        {
            IsStopping = false;
            StatusText = $"Stop failed: {UserFacingError(ex)}";
        }
    }

    /// <summary>Switches the conversation's approval mode: remembered locally, pushed to a live
    /// turn if there is one, and re-sent with the next message either way.</summary>
    public async Task SetModeAsync(string mode)
    {
        if (_session is null || !AgentModes.IsValid(mode) || mode == CurrentMode) return;

        CurrentMode = mode;
        _session.Mode = mode;
        await _runManager.SetModeAsync(_session.Id, mode);
        await _sessions.UpdateSessionAsync(_session);
    }

    /// <summary>Applies a run snapshot to this page's message list on the UI thread. Replays
    /// any events not yet projected (so a resubscribing page catches up), updates busy/status,
    /// and finalizes on terminal states — all idempotent per <see cref="ConversationRunSnapshot.Sequence"/>.</summary>
    private void ApplyRunSnapshot(ConversationRunSnapshot snapshot)
    {
        if (snapshot.RunId == _historyLoadedRunId)
        {
            // The DB restore already contains this run's events, final reply, and summary.
            // We still mirror its busy/status state until the terminal notification arrives.
            IsProcessing = !snapshot.IsTerminal;
            CanStop = !snapshot.IsTerminal && snapshot.Status == ConversationRunStatus.Running;
            IsStopping = HasPendingStop(snapshot);
            StatusText = IsStopping ? "Stopping..." : StatusTextFor(snapshot);
            if (snapshot.IsTerminal)
            {
                CanStop = false;
                IsStopping = false;
                CanRetry = snapshot.Status == ConversationRunStatus.Failed;
                _historyLoadedRunId = null;
                StoreSessionCache();
            }
            return;
        }

        if (snapshot.RunId != _liveRunId)
        {
            if (snapshot.IsTerminal)
            {
                // The run finished before this page attached; the DB load already reflects
                // the whole turn, so just clear the busy state — never re-append it. A failed
                // run stays retryable, so reopening a failed conversation still offers Retry.
                IsProcessing = false;
                CanStop = false;
                IsStopping = false;
                StatusText = "";
                CanRetry = snapshot.Status == ConversationRunStatus.Failed;
                return;
            }

            // First time we see this (active) run — start a fresh projection over our list,
            // which already holds the persisted history + optimistic user message.
            _liveRunId = snapshot.RunId;
            _liveProjection = new ChatTurnProjection(this);
            _appliedEventCount = 0;
        }

        var events = snapshot.Events;
        for (var i = _appliedEventCount; i < events.Count; i++)
        {
            _liveProjection!.Apply(events[i]);
            FollowAgentModeChange(events[i]);
            FollowConnectionPhase(events[i]);
        }
        _appliedEventCount = events.Count;

        IsProcessing = !snapshot.IsTerminal;
        CanStop = !snapshot.IsTerminal && snapshot.Status == ConversationRunStatus.Running;
        IsStopping = HasPendingStop(snapshot);
        // Queued/Connecting is exactly "submitted but the INPUT frame has not gone out", which is
        // the only state an un-send is honest in. Clearing _pendingSend as soon as that passes
        // means a later Stop can never mistake a running turn for a cancellable one.
        CanCancelSend = _pendingSend is not null && !snapshot.IsTerminal
            && snapshot.Status is ConversationRunStatus.Queued or ConversationRunStatus.Connecting;
        if (!CanCancelSend) _pendingSend = null;
        StatusText = IsStopping ? "Stopping..." : StatusTextFor(snapshot);

        // The snapshot owns the phase for a live turn, except while reconnecting: the run stays
        // Running with a dead socket underneath it, and reporting that as Running would tell the
        // user everything is fine in the one moment it is not.
        if (!snapshot.IsTerminal && ConnectionPhase != ConnectionPhase.Reconnecting)
        {
            ConnectionPhase = snapshot.Status switch
            {
                ConversationRunStatus.Queued or ConversationRunStatus.Connecting
                    => ConnectionPhase.Connecting,
                // Waiting is set by the interactive events and must survive the snapshots that
                // arrive while the card sits unanswered.
                ConversationRunStatus.Running when ConnectionPhase == ConnectionPhase.Waiting
                    => ConnectionPhase.Waiting,
                _ => ConnectionPhase.Running,
            };
        }

        if (!snapshot.IsTerminal) return;

        // Terminal: mirror what the manager persisted so the live view matches the DB.
        if (snapshot.Status == ConversationRunStatus.Completed)
        {
            _liveProjection!.AppendCompletedTurn(snapshot.PartialContent);
            CanRetry = false;
            _ = PersistSessionMetadataAsync();
        }
        else if (AgentSessionManager.IsAbandonedResume(snapshot))
        {
            // A resume probe that found the host no longer running this turn. It settles quietly
            // rather than posting a connection-error bubble for a connection that was never the
            // problem.
            _liveProjection?.CompleteToolActivity(ToolActivityStatus.Cancelled);
            StatusText = "";
            CanRetry = false;
            // The manager may have just recovered the turn's reply out of the CONNECTED frame and
            // written it to SQLite — it persists before publishing this terminal snapshot, so the
            // rows are already committed. Reload so the page shows them instead of the truncated
            // history it loaded moments ago. Cheap, and a no-op when nothing was recovered.
            _ = ReloadAfterRecoveryAsync();
        }
        else if (snapshot.Status == ConversationRunStatus.Failed)
        {
            _liveProjection?.CompleteToolActivity(ToolActivityStatus.Failed, snapshot.ErrorMessage);
            // A run that failed while an invite code was in flight never got its
            // ONBOARD_SUCCESS, so hand the input back rather than leaving a card that
            // says "waiting" over a box the user can no longer type in.
            ReopenPendingOnboarding();
            Messages.Add(new ChatMessage
            {
                Id = NextId(),
                Role = ChatRole.Agent,
                AgentName = _agent?.Name,
                Content = $"[connection error] {snapshot.ErrorMessage}",
            });
            // The error is shown as a bubble plus the Retry bar, so the status line stays out of
            // it. StatusTextFor already returns "" for Failed; this clears the one case it can't
            // — a stop that was still pending when the run died, which would otherwise leave
            // "Stopping..." sitting under a finished turn.
            StatusText = "";
            CanRetry = true;
            if (_agent is not null)
            {
                _ = PresenceService.RefreshAsync(_agent);
                _notifications.NotifyConnectionLost(_agent.Id, _agent.Name);
            }
        }
        else if (snapshot.Status == ConversationRunStatus.Cancelled)
        {
            _liveProjection?.CompleteToolActivity(ToolActivityStatus.Cancelled);
            CanRetry = false;
        }

        // The turn is over, so nothing can answer an approval any more. Answering removes its own
        // card (SubmitApprovalAsync); this covers every other way a turn can end — Stop, a dropped
        // socket, a cancelled run — where the card would otherwise sit there offering buttons that
        // resolve nothing. It also keeps the live list agreeing with what was persisted, which for
        // an approval is nothing at all.
        RemoveApprovalBubbles();
        StopThinkingTicker();

        _liveProjection = null;
        _liveRunId = null;
        _appliedEventCount = 0;
        _historyLoadedRunId = null;
        ReconnectAttempt = 0;
        // The turn is over, so the phase goes back to whatever the resting socket is doing.
        RefreshConnectionPhase();
        StoreSessionCache();
    }

    /// <summary>Drops every approval bubble from the live list. Backwards, because removing from
    /// an <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/> shifts the tail.</summary>
    private void RemoveApprovalBubbles()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            if (Messages[i].Role == ChatRole.Event && Messages[i].EventKind == "approval")
            {
                Messages.RemoveAt(i);
            }
        }
    }

    private static bool HasPendingStop(ConversationRunSnapshot snapshot)
    {
        if (snapshot.IsTerminal) return false;

        var pending = false;
        foreach (var e in snapshot.Events)
        {
            if (e.Type == "interrupt_requested") pending = true;
            else if (e.Type == "interrupt_request_failed") pending = false;
        }
        return pending;
    }

    /// <summary>
    /// Keeps the mode pill honest when the agent changes mode on its own — calling
    /// <c>enter_plan_mode()</c> emits <c>mode_changed</c> with <c>triggered_by: "agent"</c>, and
    /// from then on the agent really is in plan mode whatever our pill said. So the agent wins,
    /// and the new mode is persisted as the conversation's, because that is what the next turn
    /// would otherwise be re-asserting over the top of.
    /// </summary>
    /// <summary>
    /// Re-reads the conversation after an abandoned resume, so a reply the manager recovered from
    /// the host's session appears without the user having to navigate away and back.
    /// <para>Goes through the cache invalidation first: the conversation was cached as it looked
    /// when it loaded (ending at the user's message), and a reload that hit that cache would show
    /// the same truncated history it was meant to replace.</para>
    /// </summary>
    private async Task ReloadAfterRecoveryAsync()
    {
        if (_agent is null || _session is null) return;

        ConversationCache.Invalidate(_session.Id);
        var generation = _loadGeneration;
        try
        {
            await RestoreConversationAsync(generation, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogResumeProbeFailed(_logger, _session.Id, ex);
        }
    }

    /// <summary>
    /// Tracks the transport phase from the reconnect markers the session manager publishes into
    /// the run's event stream, and from the interactive turns that park the agent on a human.
    /// <para>Reading these off the event stream rather than subscribing to the connection is
    /// deliberate: the events are buffered and replayed, so a page opened mid-outage arrives at
    /// the same phase as one that watched it happen, and there is no second subscription to tear
    /// down when the conversation switches.</para>
    /// </summary>
    private void FollowConnectionPhase(AgentStreamEvent e)
    {
        switch (e.Type)
        {
            case "reconnecting":
                ConnectionPhase = ConnectionPhase.Reconnecting;
                ReconnectAttempt = ReadAttempt(e.RawJson);
                break;

            // The socket is back. Whether the turn survived is the "resumed" flag's business
            // (and the transcript card says so); either way the transport is healthy again, and
            // the snapshot that follows settles Running vs terminal.
            case "reconnected":
                ConnectionPhase = ConnectionPhase.Running;
                ReconnectAttempt = 0;
                break;

            // The agent has handed the turn to the user and will not move until they answer.
            case "ask_user":
            case "approval_needed":
            case "plan_review":
                ConnectionPhase = ConnectionPhase.Waiting;
                break;

            // Any ordinary agent event means it is working again, so a Waiting phase left over
            // from an answered card does not stick for the rest of the turn.
            case "thinking":
            case "llm_call":
            case "tool_call":
            case "assistant":
                if (ConnectionPhase == ConnectionPhase.Waiting) ConnectionPhase = ConnectionPhase.Running;
                break;
        }
    }

    private static int ReadAttempt(string rawJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
            return doc.RootElement.TryGetProperty("attempt", out var v)
                && v.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? v.GetInt32()
                    : 1;
        }
        catch
        {
            return 1;
        }
    }

    private void FollowAgentModeChange(AgentStreamEvent e)
    {
        if (e.Type != "mode_changed" || _session is null) return;

        string? mode;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(e.RawJson);
            mode = doc.RootElement.TryGetProperty("mode", out var v) ? v.GetString() : null;
        }
        catch { return; }

        if (!AgentModes.IsValid(mode) || mode == CurrentMode) return;

        CurrentMode = mode!;
        _session.Mode = mode!;
        _ = _sessions.UpdateSessionAsync(_session);
    }

    /// <summary>
    /// The status line reports only what the transcript cannot show for itself. Once events start
    /// arriving, the transcript *is* the narration — a thought lands as a thought-process card, a
    /// tool as an activity card, an approval as its own card — so echoing the last event's
    /// description here said the same thing twice, in a weaker form (a bare tool name, and only
    /// ever the most recent one). Same reasoning retires the Failed text: that path already adds
    /// an error bubble and raises the Retry bar, which is why the caller below blanks this too.
    ///
    /// What is left is the connection lifecycle, which has no bubble anywhere: queued, connecting,
    /// and the gap between a started run and its first event, where nothing else says the app
    /// heard the user.
    /// </summary>
    private static string StatusTextFor(ConversationRunSnapshot snapshot) => snapshot.Status switch
    {
        ConversationRunStatus.Queued => "Working...",
        ConversationRunStatus.Connecting => "Connecting...",
        ConversationRunStatus.Running when snapshot.Events.Count == 0 => "Working...",
        _ => "",
    };
    /// <summary>Forces the app-owned socket for this conversation to reconnect to its session.</summary>
    public async Task ReconnectAsync()
    {
        if (_agent is null || _session is null) return;

        var connection = _runManager.Connections.GetOrCreate(_session.Id, _agent);

        IsConnecting = true;
        StatusText = "Reconnecting...";
        try
        {
            await connection.ReconnectAsync(_session.Id);
            StatusText = "Connected.";
        }
        catch (Exception ex)
        {
            StatusText = $"Reconnect failed: {UserFacingError(ex)}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    /// <summary>Re-runs the last failed turn: the app-level manager reuses the original user
    /// message but mints a fresh run id. The page's existing run subscription streams the new
    /// run's events back through <see cref="ApplyRunSnapshot"/>, so no extra wiring is needed
    /// here. Bound to the generated <see cref="RetryCommand"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanRetry))]
    public void Retry()
    {
        if (_agent is null || _session is null || IsProcessing || !CanRetry) return;

        // Ask the manager first: if the run is no longer retryable (the registry cleared it, or
        // a restart lost it), keep the error bubble as history rather than dropping it silently.
        if (_runManager.RetryRun(_agent, _session.Id) is null)
        {
            CanRetry = false;
            return;
        }

        CanRetry = false;
        RemoveLastFailureBubble();
        IsProcessing = true;
        CanStop = false;
        IsStopping = false;
        StatusText = "Connecting...";
    }

    /// <summary>Drops the "[connection error]" bubble for the last failed turn (whether it was
    /// added live or restored from history) so a successful retry doesn't leave a stale error
    /// above the fresh reply.</summary>
    private void RemoveLastFailureBubble()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var m = Messages[i];
            if (m.Role == ChatRole.Agent
                && m.Content is not null
                && m.Content.StartsWith("[connection error]", StringComparison.Ordinal))
            {
                Messages.RemoveAt(i);
                return;
            }
        }
    }
    /// <summary>Persists the remote session id + last processed event id so a later reconnect
    /// can skip already-seen events.</summary>
    private async Task PersistSessionMetadataAsync()
    {
        if (_agent is null || _session is null) return;
        var connection = _runManager.Connections.Get(_session.Id);
        if (connection is null) return;

        _session.RemoteSessionId = connection.SessionId;
        _session.LastProcessedEventId = connection.LastProcessedEventId;
        _session.UpdatedAt = DateTime.UtcNow.ToString("o");
        await _sessions.UpdateSessionAsync(_session);
    }
}
