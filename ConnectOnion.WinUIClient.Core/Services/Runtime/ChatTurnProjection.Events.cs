using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services.Runtime;

/// <summary>
/// <see cref="ChatTurnProjection"/>: the stream-event dispatch table — one switch arm per wire
/// event type, mapping it to the bubble (or the mutation of an existing bubble) it produces.
///
/// This is the file to edit when the agent gains a new event type. It is kept apart from the
/// turn-completion logic in the main file because it is a flat, growing table rather than a
/// sequence: arms are independent of each other, and the ordering between them carries no
/// meaning. Everything it needs from the target goes through the small helpers at the bottom of
/// the main file, so nothing here touches the UI collection directly.
/// </summary>
public sealed partial class ChatTurnProjection
{
    private void ApplyParsed(AgentStreamEvent e, JsonElement root)
    {
        // An unparseable frame still projects its bubble (with empty fields), exactly as before —
        // the readers below tolerate a missing property but not an *undefined* element.
        if (root.ValueKind != JsonValueKind.Object) root = EmptyObject.RootElement;

        // Local kind-checked readers over the captured root. Locals rather than statics taking
        // the element, purely so the switch arms below read as `Str("model")` instead of
        // threading `root` through forty call sites; the closure is the whole point.
        string? Str(string name) => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;
        double? Num(string name) => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetDouble() : null;
        bool? Flag(string name) => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var v)
                ? v.ValueKind == JsonValueKind.True ? true
                    : v.ValueKind == JsonValueKind.False ? false : (bool?)null
                : null;
        switch (e.Type)
        {
            // Local-only lifecycle markers. They intentionally render no separate bubble; the
            // successful marker is attached to the final OUTPUT message instead.
            case "interrupt_requested":
                _wasInterrupted = true;
                break;

            case "interrupt_request_failed":
                _wasInterrupted = false;
                break;

            // GetToolActivity creates the card on demand; tool_result uses `?.` instead, because
            // a result with no preceding call has nothing to attach to and must not conjure an
            // empty activity card out of a stray frame.
            case "tool_call":
                GetToolActivity().ApplyCall(e);
                break;

            case "tool_result":
                _toolActivity?.ApplyResult(e);
                break;

            case "llm_call":
                if (Str("model") is { } callModel) _turnModel = callModel;
                break;

            case "llm_result":
            {
                if (Str("model") is { } resultModel) _turnModel = resultModel;

                // Accumulating (+=), not assigning: one turn makes an LLM call per agent
                // iteration, and the summary bubble reports the turn's total spend. The model,
                // by contrast, is overwritten — the last one used is what gets labelled.
                var (tokIn, tokOut) = TryParseRawTokenUsage(root);
                if (tokIn is not null) { _turnTokensIn += tokIn.Value; _turnHasUsage = true; }
                if (tokOut is not null) { _turnTokensOut += tokOut.Value; _turnHasUsage = true; }
                if (Num("duration_ms") is { } dur) _turnDurationMs += dur;
                if (Num("context_percent") is { } ctx) _turnContextPercent = ctx;
                if (Num("tool_calls_count") is { } tcc) _turnToolCallsCount += (long)tcc;
                break;
            }

            case "thinking":
            {
                var thinking = new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "activity",
                    EventTitle = "Thinking",
                    EventDetail = Str("content"),
                    Status = EventStatus.Running,
                };
                _thinkingActivities.Add(thinking);
                Add(thinking);
                break;
            }

            // intent / eval / compact share one shape: a "started" frame opens an activity row
            // keyed by the frame's id, and the matching completion frame mutates that same row
            // in place rather than adding a second one. A completion whose id never opened a row
            // (page joined mid-turn) is dropped — a lone "complete" reads as noise.
            case "intent":
            {
                var id = Str("id");
                if (Str("status") == "analyzing")
                {
                    Add(NewActivity(id, "Understanding request...", EventStatus.Running));
                }
                else if (FindEvent(id, "activity") is { } existing)
                {
                    existing.EventTitle = "Request understood";
                    existing.EventDetail = Str("ack");
                    existing.Status = EventStatus.Done;
                }
                break;
            }

            case "eval":
            {
                var id = Str("id");
                if (Str("status") == "evaluating")
                {
                    var item = NewActivity(id, "Evaluating result...", EventStatus.Running);
                    item.EventDetail = Str("expected");
                    Add(item);
                }
                else if (FindEvent(id, "activity") is { } existing)
                {
                    existing.EventTitle = "Evaluation complete";
                    existing.EventDetail = Str("summary") ?? Str("expected");
                    existing.Status = Flag("passed") == false ? EventStatus.Error : EventStatus.Done;
                }
                break;
            }

            case "compact":
            {
                var id = Str("id");
                if (Str("status") == "compacting")
                {
                    Add(NewActivity(id, "Compacting context...", EventStatus.Running));
                }
                else if (FindEvent(id, "activity") is { } existing)
                {
                    existing.EventTitle = "Context compacted";
                    existing.Status = Str("status") == "error" ? EventStatus.Error : EventStatus.Done;
                    existing.EventDetail = Str("message") ?? Str("error");
                    if (Num("context_before") is { } before && Num("context_after") is { } after)
                        existing.EventMeta = $"{before:0} → {after:0}";
                }
                break;
            }

            case "tool_blocked":
                GetToolActivity().AddBlocked(e);
                break;

            case "agent_image":
            {
                // Don't render the image yet — buffer it and hang it on the final output when the
                // turn completes (see FlushPendingImages). Rendering here would drop the image in
                // above the tool activity, usage summary, and final reply that stream in after it.
                if (AttachmentWireEvents.TryGetAgentImageDataUrl(WireMessage.Wrap(root), out var dataUrl))
                    _pendingAgentImages.Add(dataUrl);
                break;
            }

            case "files_received":
            {
                var names = new List<string>();
                if (AttachmentWireEvents.TryGetFilesReceived(WireMessage.Wrap(root), out var files))
                {
                    foreach (var file in files) names.Add(file.Name);
                }
                var item = NewActivity(null, $"Files received - {names.Count} file(s)", EventStatus.Done);
                if (names.Count > 0) item.EventDetail = string.Join(", ", names);
                Add(item);
                break;
            }

            case "assistant":
            {
                var content = Str("content");
                if (!string.IsNullOrEmpty(content))
                {
                    CompleteThinkingActivities(EventStatus.Done);
                    _streamedFinalReply = new ChatMessage
                    {
                        Id = NextId(),
                        Role = ChatRole.Agent,
                        Content = content,
                        AgentName = AgentName,
                    };
                    Add(_streamedFinalReply);
                }
                break;
            }

            case "ONBOARD_SUCCESS":
                // Seal the invite-code card: the code was accepted, so its input must stop
                // offering a second submission. Sealing it here (not just in the view model
                // that clicked Submit) also covers the auto-submitted and replayed cases.
                foreach (var card in Messages)
                {
                    if (!card.IsOnboarding) continue;
                    // Clearing IsOnboarding retires the card for good: the input row is gone,
                    // and a later failed run in this conversation won't mistake an already
                    // accepted code for one still awaiting an answer. IsOnboardingSubmitted is
                    // what raises the change notification (IsOnboarding is a plain property),
                    // so both have to move.
                    card.IsOnboardingSubmitted = true;
                    card.IsOnboarding = false;
                    card.Content = "Invite code accepted.";
                }
                Add(NewActivity(null, "Onboarding complete", EventStatus.Done));
                break;

            case "mode_changed":
            {
                // The agent can enter plan mode by itself (triggered_by: "agent"), so this card is
                // not an echo of the user's own click — it is the only place a mode the *agent*
                // chose becomes visible.
                var item = NewActivity(null, $"Mode: {AgentModes.DisplayName(Str("mode"))}", EventStatus.Done);
                item.EventMeta = Str("triggered_by") switch
                {
                    "agent" => "by agent",
                    "ulw_checkpoint" => "by checkpoint",
                    _ => null,
                };
                Add(item);
                break;
            }

            // ---- interactive turns & onboarding (synthetic events, see AgentTurnExecutor) ----

            case "ONBOARD_REQUIRED":
                // One live invite card at a time. The agent re-sends this on every reconnect
                // attempt, and the persist path replays it, so without the guard a user who
                // hasn't answered yet would accumulate a column of identical input boxes.
                if (!Messages.Any(m => m.IsOnboarding && !m.IsOnboardingSubmitted))
                {
                    Add(new ChatMessage
                    {
                        Id = NextId(),
                        Role = ChatRole.Agent,
                        AgentName = AgentName,
                        IsOnboarding = true,
                        Content = "This agent requires an invite code before the conversation can continue.",
                    });
                }
                break;

            case "ask_user":
            {
                var request = AgentInteractiveParsers.ParseAskUser(WireMessage.Wrap(root));
                var message = new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "ask_user",
                    EventKey = request.Id,
                    EventEyebrow = "Question",
                    EventTitle = request.Text,
                    Status = EventStatus.Running,
                };
                message.AskUserMultiSelect = request.MultiSelect;
                foreach (var option in request.Options)
                {
                    message.AskUserOptions.Add(option);
                    if (request.MultiSelect) message.AskUserOptionEntries.Add(new AskUserOptionEntry { Text = option });
                }
                foreach (var field in request.Fields)
                {
                    message.AskUserFields.Add(new AskUserFieldEntry
                    {
                        Name = field.Name,
                        Label = field.Label,
                        Placeholder = field.Placeholder,
                    });
                }
                Add(message);
                break;
            }

            case "approval_needed":
                // Park the tool card on "waiting for your confirmation" as well as raising the
                // approval bubble: the step that triggered this is still open in the activity
                // list, and leaving it spinning as though it were running would misreport what
                // the agent is actually blocked on.
                _toolActivity?.WaitForConfirmation();
                Add(new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "approval",
                    EventEyebrow = "Approval needed",
                    EventTitle = Str("tool") ?? "",
                    EventDetail = Str("description"),
                    EventArgs = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("arguments", out var a)
                        ? PrettyJson(a.GetRawText()) : PrettyJson("{}"),
                    Status = EventStatus.Running,
                });
                break;

            case "plan_review":
                Add(new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "plan_review",
                    EventEyebrow = "Plan review",
                    EventTitle = "Review the plan",
                    EventDetail = Str("plan_content") ?? "",
                    Status = EventStatus.Running,
                });
                break;
        }
    }
}
