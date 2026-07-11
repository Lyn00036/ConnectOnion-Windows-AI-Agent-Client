using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConnectOnion.Protocol.Runtime;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services.Runtime;

/// <summary>Result of submitting a message: the stable ids the caller uses to correlate
/// its optimistic UI with the app-level run.</summary>
public sealed record SendMessageResult(string RunId, string UserMessageId, string AssistantMessageId);

/// <summary>
/// App-level owner of agent connections and in-flight turns. Pages/view models depend on
/// this singleton instead of holding a connection or a background task themselves, so a
/// turn keeps running (and persists) after the page is gone, and re-opening a conversation
/// resumes its live stream. See <see cref="ConversationRunRegistry"/> for the guarantees.
/// </summary>
public interface IAgentSessionManager
{
    /// <summary>Persists the user message, then starts an app-level run for the reply. Throws
    /// if the conversation already has an active run (callers gate on <see cref="GetActiveRun"/>).</summary>
    Task<SendMessageResult> SendMessageAsync(
        AgentConfig agent,
        string conversationId,
        string prompt,
        IReadOnlyList<PendingAttachment>? attachments = null);

    ConversationRunSnapshot? GetActiveRun(string conversationId);

    /// <summary>Whether the run's projected conversation history has already been written
    /// to the local database. This is distinct from <see cref="ConversationRunSnapshot.IsTerminal"/>:
    /// persistence completes just before the registry publishes the terminal snapshot.</summary>
    bool IsRunHistoryPersisted(string runId);

    IReadOnlyList<ConversationRunSnapshot> GetActiveRuns();

    /// <summary>Subscribe to a conversation's run updates; the handler fires immediately with
    /// the current snapshot (if any). Disposing detaches without cancelling the run.</summary>
    IDisposable Subscribe(string conversationId, Action<ConversationRunSnapshot> handler);

    Task CancelRunAsync(string conversationId, string runId);

    /// <summary>Retries a failed run, reusing the original user message. Returns null if there
    /// is nothing to retry.</summary>
    ConversationRunSnapshot? RetryRun(AgentConfig agent, string conversationId);
}
