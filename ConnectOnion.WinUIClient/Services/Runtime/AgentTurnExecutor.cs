using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ConnectOnion.Protocol;
using ConnectOnion.Protocol.Runtime;

namespace ConnectOnion.WinUIClient.Services.Runtime;

/// <summary>
/// Drives a single turn against an app-owned <see cref="AgentConnectionService"/> and
/// reports progress into the run's <see cref="IRunSink"/>. Interactive turns and
/// onboarding — which the connection surfaces as dedicated typed events, not generic
/// stream events — are re-serialized into synthetic stream frames and buffered too, so a
/// page created mid-turn replays them into the exact same bubbles. Answering those turns
/// is done separately by the view model on the same socket (fetched from the connection
/// registry); this executor only records that they happened.
/// </summary>
public sealed class AgentTurnExecutor : ITurnExecutor
{
    private readonly AgentConnectionService _connection;
    private readonly Action<ApprovalRequest>? _onApproval;

    /// <param name="onApproval">App-level hook fired when the agent requests approval, so the
    /// notification is raised once (in the tray, across windows) rather than per open page.</param>
    public AgentTurnExecutor(AgentConnectionService connection, Action<ApprovalRequest>? onApproval = null)
    {
        _connection = connection;
        _onApproval = onApproval;
    }

    public async Task<string> ExecuteAsync(TurnRequest request, IRunSink sink)
    {
        void OnStream(AgentStreamEvent e)
        {
            sink.SetRunning();
            sink.Publish(e);
            if (e.Type == "assistant")
            {
                var content = ExtractString(e.RawJson, "content");
                if (!string.IsNullOrEmpty(content)) sink.UpdatePartial(content!);
            }
        }
        void OnOnboard() => sink.Publish(new AgentStreamEvent(
            "ONBOARD_REQUIRED", "Agent requires onboarding", null, "{\"type\":\"ONBOARD_REQUIRED\"}"));
        void OnAsk(AskUserRequest r) => sink.Publish(new AgentStreamEvent(
            "ask_user", r.Text, r.Id, BuildAskUserFrame(r)));
        void OnApproval(ApprovalRequest r)
        {
            sink.Publish(new AgentStreamEvent("approval_needed", r.Tool, null, BuildApprovalFrame(r)));
            _onApproval?.Invoke(r);
        }
        void OnPlan(PlanReviewRequest r) => sink.Publish(new AgentStreamEvent(
            "plan_review", "Plan review", null, BuildPlanFrame(r)));

        _connection.StreamEvent += OnStream;
        _connection.OnboardRequired += OnOnboard;
        _connection.AskUserRequested += OnAsk;
        _connection.ApprovalRequested += OnApproval;
        _connection.PlanReviewRequested += OnPlan;
        try
        {
            sink.SetConnecting();
            var reply = await _connection.SendInputAsync(
                request.Prompt, request.SessionId, request.Images, request.Files, sink.CancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(reply)) sink.UpdatePartial(reply);
            return reply;
        }
        finally
        {
            _connection.StreamEvent -= OnStream;
            _connection.OnboardRequired -= OnOnboard;
            _connection.AskUserRequested -= OnAsk;
            _connection.ApprovalRequested -= OnApproval;
            _connection.PlanReviewRequested -= OnPlan;
        }
    }

    private static string? ExtractString(string rawJson, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() : null;
        }
        catch { return null; }
    }

    // Re-serialize typed interactive requests back into the canonical wire shape the
    // projection parses (keys chosen to round-trip through the same reader).

    private static string BuildAskUserFrame(AskUserRequest r)
    {
        var frame = new Dictionary<string, object?>
        {
            ["type"] = "ask_user",
            ["id"] = r.Id,
            ["text"] = r.Text,
            ["options"] = r.Options,
            ["multi_select"] = r.MultiSelect,
            ["fields"] = r.Fields.Select(f => new Dictionary<string, object?>
            {
                ["name"] = f.Name,
                ["label"] = f.Label,
                ["placeholder"] = f.Placeholder,
                ["required"] = f.Required,
                ["type"] = f.Type,
            }).ToList(),
        };
        return JsonSerializer.Serialize(frame);
    }

    private static string BuildApprovalFrame(ApprovalRequest r)
    {
        JsonElement args;
        try { args = JsonSerializer.Deserialize<JsonElement>(r.ArgumentsJson); }
        catch { args = JsonSerializer.Deserialize<JsonElement>("{}"); }
        var frame = new Dictionary<string, object?>
        {
            ["type"] = "approval_needed",
            ["tool"] = r.Tool,
            ["description"] = r.Description,
            ["arguments"] = args,
        };
        return JsonSerializer.Serialize(frame);
    }

    private static string BuildPlanFrame(PlanReviewRequest r)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "plan_review",
            ["plan_content"] = r.PlanContent,
        });
}
