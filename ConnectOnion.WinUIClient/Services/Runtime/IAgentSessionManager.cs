using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConnectOnion.Protocol;
using ConnectOnion.Protocol.Runtime;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services.Runtime;

/// <summary>Result of submitting a message: the stable ids the caller uses to correlate
/// its optimistic UI with the app-level run.</summary>
public sealed record SendMessageResult(string RunId, string UserMessageId, string AssistantMessageId);

/// <summary>
/// What a Stop actually achieved. The protocol has no STOP frame, so this is not a formality —
/// the three outcomes differ in whether the agent on the host has really stopped, and the UI
/// must not claim more than happened.
/// </summary>
public enum StopOutcome
{
    /// <summary>Nothing was running.</summary>
    NotRunning,

    /// <summary>The agent was parked on an <c>approval_needed</c> turn and we rejected it with
    /// <c>reject_hard</c>. This sets <c>stop_signal</c> host-side: the rest of the tool batch is
    /// dropped, the iteration loop breaks, and the turn ends with a normal OUTPUT. The only
    /// genuine, deterministic stop the protocol offers.</summary>
    Stopped,

    /// <summary>The agent was mid-work (or parked on a question), so all we could do was ask:
    /// either answering the open interactive turn with a stop instruction, or injecting a
    /// runtime-input "stop now". The model decides whether to comply, and the turn keeps
    /// running until it does. Not a stop — a request.</summary>
    StopRequested,

    /// <summary>The local run was abandoned. The user's chat is unblocked immediately, but the
    /// agent thread on the host <b>keeps executing</b> — nothing can preempt it — which is why
    /// this is only ever reached by an explicit second action. See
    /// <see cref="IAgentSessionManager.GetServerSessionStatusAsync"/>.</summary>
    ForceStopped,
}

/// <summary>
/// App-level owner of agent connections and in-flight turns. Pages/view models depend on
/// this singleton instead of holding a connection or a background task themselves, so a
/// turn keeps running (and persists) after the page is gone, and re-opening a conversation
/// resumes its live stream. See <see cref="ConversationRunRegistry"/> for the guarantees.
/// </summary>
public interface IAgentSessionManager
{
    /// <summary>Persists the user message, then starts an app-level run for the reply. Throws
    /// if the conversation already has an active run (callers gate on <see cref="GetActiveRun"/>).
    /// <paramref name="mode"/> is the approval mode the turn runs under; pass the conversation's
    /// current one on every send (the host will not remember ours between turns).</summary>
    Task<SendMessageResult> SendMessageAsync(
        AgentConfig agent,
        string conversationId,
        string prompt,
        IReadOnlyList<PendingAttachment>? attachments = null,
        string? mode = null);

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

    /// <summary>
    /// The best stop the protocol allows right now, chosen from what the agent is currently
    /// blocked on — see <see cref="StopOutcome"/> for what each one really does. Never abandons
    /// the local run: that is <see cref="ForceStopAsync"/>, and it is a separate, deliberate
    /// second action because it leaves the agent running on the host.
    /// </summary>
    Task<StopOutcome> RequestStopAsync(string conversationId);

    /// <summary>Abandons the local run. The host keeps executing the turn; the conversation is
    /// flagged so the next send can warn that it will land as a follow-up to it.</summary>
    Task ForceStopAsync(string conversationId);

    /// <summary>True once <see cref="ForceStopAsync"/> abandoned a run here and nothing has since
    /// confirmed the host went idle.</summary>
    bool MayHaveOrphanedRun(string conversationId);

    /// <summary>Asks the host whether this conversation's session still has an agent running —
    /// one of <see cref="SessionStatuses"/>. Clears the orphan flag when the answer is "no".</summary>
    Task<string> GetServerSessionStatusAsync(string conversationId);

    /// <summary>
    /// Switches the conversation's approval mode. Applies to a turn in flight immediately (the
    /// agent picks it up at its next iteration boundary); with no turn running there is nothing
    /// on the host to tell, so the mode simply rides along with the next
    /// <see cref="SendMessageAsync"/> — which is why callers must keep passing it there.
    /// </summary>
    Task SetModeAsync(string conversationId, string mode);

    /// <summary>Forgets a resolved interactive turn, so Stop stops treating the agent as parked
    /// on it. Called by the page the moment it answers one.</summary>
    void ClearPendingInteractive(string conversationId);
}
