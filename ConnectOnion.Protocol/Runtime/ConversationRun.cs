using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ConnectOnion.Protocol.Runtime;

/// <summary>
/// The live, mutable state of one agent turn. Owned exclusively by
/// <see cref="ConversationRunRegistry"/>; callers only ever see immutable
/// <see cref="ConversationRunSnapshot"/> projections of it. All mutation goes through
/// the registry while holding <see cref="Gate"/>, so a background executor thread and a
/// UI-driven cancel/subscribe can never corrupt the buffer or double-finalize.
/// </summary>
internal sealed class ConversationRun
{
    public required TurnRequest Request { get; init; }
    public required CancellationTokenSource Cts { get; init; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Single lock guarding every field below and the finalize-once invariant.</summary>
    public object Gate { get; } = new();

    public ConversationRunStatus Status { get; set; } = ConversationRunStatus.Queued;
    public string PartialContent { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public long Sequence { get; set; }
    public bool Finalized { get; set; }

    public readonly List<AgentStreamEvent> Events = new();

    /// <summary>The background turn task; observed by the registry (never fire-and-forget-unobserved).</summary>
    public Task? ExecutionTask { get; set; }

    public string RunId => Request.RunId;
    public string ConversationId => Request.ConversationId;
    public string AgentId => Request.AgentId;

    /// <summary>Builds an immutable snapshot. Caller must hold <see cref="Gate"/>.</summary>
    public ConversationRunSnapshot SnapshotLocked(ConversationRunStatus? statusOverride = null) => new(
        RunId,
        ConversationId,
        AgentId,
        Request.UserMessageId,
        Request.AssistantMessageId,
        statusOverride ?? Status,
        PartialContent,
        Sequence,
        ErrorCode,
        ErrorMessage,
        Events.ToArray());
}
