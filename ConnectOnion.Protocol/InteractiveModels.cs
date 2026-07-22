using System.Collections.Generic;

namespace ConnectOnion.Protocol;

/// <summary>A dynamic field in an ask_user form (name/label/placeholder/type).</summary>
public sealed record AskUserField(string Name, string Label, string? Placeholder, bool Required, string? Type);

/// <summary>
/// An <c>ask_user</c> turn: the agent is waiting for a human answer. Answer with
/// <see cref="AgentConnectionService.RespondAskUserAsync"/>.
/// </summary>
public sealed record AskUserRequest(
    string? Id,
    string Text,
    IReadOnlyList<string> Options,
    bool MultiSelect,
    IReadOnlyList<AskUserField> Fields);

/// <summary>
/// An <c>approval_needed</c> turn: the agent wants to run a tool. Respond with
/// <see cref="AgentConnectionService.RespondApprovalAsync"/>.
/// </summary>
public sealed record ApprovalRequest(
    string Tool,
    string? Description,
    string ArgumentsJson,
    string? Reason = null,
    string? BatchRemainingJson = null);

/// <summary>
/// A <c>plan_review</c> turn: the agent wants its plan reviewed. Respond with
/// <see cref="AgentConnectionService.RespondPlanReviewAsync"/>.
/// </summary>
public sealed record PlanReviewRequest(string PlanContent);
