using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services.Runtime;

namespace ConnectOnion.WinUIClient.UnitTests.Runtime;

public sealed class ChatTurnProjectionTests
{
    [Fact]
    public void AppendFinalReply_MatchesLastAssistantBubble_DoesNotAddDuplicate()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);
        projection.Apply(Event("assistant", "{\"content\":\"same reply\"}"));

        projection.AppendFinalReply("same reply");

        Assert.Single(target.Messages);
        Assert.Equal("same reply", target.Messages[0].Content);
    }

    [Fact]
    public void AppendFinalReply_DiffersFromLastAssistantBubble_AddsReply()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);
        projection.Apply(Event("assistant", "{\"content\":\"streamed partial\"}"));

        projection.AppendFinalReply("final reply");

        Assert.Equal(2, target.Messages.Count);
        Assert.Equal("final reply", target.Messages[1].Content);
    }

    [Fact]
    public void AppendCompletedTurn_PutsUsageBeforeStreamedFinalReply()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);
        projection.Apply(Event("llm_result", "{\"model\":\"model-a\",\"usage\":{\"input_tokens\":120,\"output_tokens\":30},\"duration_ms\":250}"));
        projection.Apply(Event("assistant", "{\"content\":\"final reply\"}"));

        projection.AppendCompletedTurn("final reply");

        Assert.Equal(2, target.Messages.Count);
        Assert.True(target.Messages[0].IsEvent);
        Assert.Equal("turn_usage", target.Messages[0].EventKey);
        Assert.Equal("model-a", target.Messages[0].EventTitle);
        Assert.Contains("120→30 tok", target.Messages[0].EventMeta);
        Assert.True(target.Messages[1].IsAgent);
        Assert.Equal("final reply", target.Messages[1].Content);
    }

    [Fact]
    public void AppendCompletedTurn_WithoutReportedUsage_StillAddsHonestSummaryFirst()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.AppendCompletedTurn("final reply");

        Assert.Equal(2, target.Messages.Count);
        Assert.Equal("Turn usage", target.Messages[0].EventTitle);
        Assert.Equal("Usage not reported", target.Messages[0].EventMeta);
        Assert.Equal("final reply", target.Messages[1].Content);
    }

    [Fact]
    public void Apply_Thinking_StaysLoadingUntilReplyArrives()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("thinking", "{\"content\":\"Checking the request.\"}"));

        var thinking = Assert.Single(target.Messages);
        Assert.True(thinking.IsThinkingRunning);
        Assert.Equal("Thinking...", thinking.ActivityHeaderTitle);
        Assert.Equal("Checking the request.", thinking.ActivityTimelineText);

        projection.Apply(Event("assistant", "{\"content\":\"Final reply\"}"));

        Assert.False(thinking.IsThinkingRunning);
        Assert.Equal(EventStatus.Done, thinking.Status);
        Assert.Equal("Thought process", thinking.ActivityHeaderTitle);
        Assert.False(thinking.HasMultipleThoughts);
    }

    [Fact]
    public void Apply_ConsecutiveThinking_GroupsIntoOneFoldableCard()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("thinking", "{\"content\":\"First, check the address.\"}"));
        projection.Apply(Event("thinking", "{\"content\":\"Then draft the subject.\"}"));
        projection.Apply(Event("thinking", "{\"content\":\"Then send it.\"}"));

        var thinking = Assert.Single(target.Messages);
        Assert.True(thinking.HasMultipleThoughts);
        Assert.False(thinking.IsSingleActivityEntry);
        Assert.Equal(3, thinking.ThoughtSteps.Count);
        Assert.Equal("Thinking...", thinking.ActivityHeaderTitle);
        Assert.Equal("3 steps", thinking.ThoughtStepsSummary);
    }

    [Fact]
    public void Apply_ThinkingSeparatedByAToolCall_StaysTwoCards()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("thinking", "{\"content\":\"Look up the recipient.\"}"));
        projection.Apply(Event("tool_call", "{\"id\":\"t1\",\"name\":\"lookup\"}"));
        projection.Apply(Event("thinking", "{\"content\":\"Now compose the mail.\"}"));

        // Thoughts either side of a tool call are reasoning about different things — merging them
        // would claim the agent planned it all before calling anything.
        var thoughts = target.Messages.Where(m => m.IsThinkingEvent).ToList();
        Assert.Equal(2, thoughts.Count);
        Assert.All(thoughts, t => Assert.Single(t.ThoughtSteps));
    }

    [Fact]
    public void Apply_ReplayedAskUser_DoesNotRaiseASecondCard()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);
        const string frame = "{\"id\":\"q1\",\"text\":\"Pick one\",\"options\":[\"A\",\"B\"]}";

        projection.Apply(Event("ask_user", frame));
        // What a reconnect does: the host replays frames the client never acknowledged.
        projection.Apply(Event("ask_user", frame));

        var card = Assert.Single(target.Messages, m => m.EventKind == "ask_user");
        Assert.Equal(2, card.AskUserOptionEntries.Count);
    }

    [Fact]
    public void Apply_LlmCall_OpensThinkingIndicator_AndLlmResultTakesItAway()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("llm_call", "{\"model\":\"deepseek-v4-pro\",\"iteration\":1}"));

        var thinking = Assert.Single(target.Messages);
        Assert.True(thinking.IsThinkingRunning);

        // No reasoning text on an llm_call, so the row must not print the internal "Thinking"
        // marker as though the agent had said it — the spinner and the live counter are the row.
        Assert.False(thinking.HasThoughtText);
        Assert.Equal("", thinking.ActivityTimelineText);

        projection.Apply(Event("llm_result", "{\"model\":\"deepseek-v4-pro\",\"duration_ms\":2000}"));

        // The wait is over, so the row goes. No "thought for" epitaph is left behind: the turn's
        // total time is on the usage line.
        Assert.Empty(target.Messages);
    }

    [Fact]
    public void Apply_ManyLlmIterations_NeverStackIndicatorRows()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        // An agentic turn: think, call a tool, think again, and again.
        projection.Apply(Event("llm_call", "{\"model\":\"m\",\"iteration\":1}"));
        projection.Apply(Event("llm_result", "{\"duration_ms\":2400}"));
        projection.Apply(Event("tool_call", "{\"tool_id\":\"t1\",\"name\":\"read_file\"}"));
        projection.Apply(Event("llm_call", "{\"model\":\"m\",\"iteration\":2}"));
        projection.Apply(Event("llm_result", "{\"duration_ms\":6600}"));
        projection.Apply(Event("llm_call", "{\"model\":\"m\",\"iteration\":3}"));

        // Eight round trips used to leave eight spent rows. At most one is ever on screen, and
        // only while a call is actually out.
        var thinking = Assert.Single(target.Messages, m => m.IsThinkingEvent);
        Assert.True(thinking.IsThinkingRunning);

        projection.Apply(Event("llm_result", "{\"duration_ms\":5100}"));

        Assert.DoesNotContain(target.Messages, m => m.IsThinkingEvent);
    }

    [Fact]
    public void Apply_LlmCallWithoutResult_RemovesTheIndicatorAtTurnEnd()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("llm_call", "{\"model\":\"m\",\"iteration\":1}"));
        projection.CompleteToolActivity(ToolActivityStatus.Failed, "boom");

        // A run that dies mid-call never gets llm_result; the spinner must not turn forever.
        Assert.DoesNotContain(target.Messages, m => m.IsThinkingEvent);
    }

    [Fact]
    public void Apply_ThinkingText_SuppressesTheBareLlmIndicator()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("thinking", "{\"content\":\"Checking the request.\"}"));
        projection.Apply(Event("llm_call", "{\"model\":\"m\",\"iteration\":1}"));

        // A host that sends both must not stack an empty spinner on top of the real thought.
        var thinking = Assert.Single(target.Messages);
        Assert.Equal("Checking the request.", thinking.ActivityTimelineText);
    }

    [Fact]
    public void CompleteToolActivity_Failed_StopsThinkingLoader()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);
        projection.Apply(Event("thinking", "{\"content\":\"Checking the request.\"}"));

        projection.CompleteToolActivity(ToolActivityStatus.Failed, "failed");

        var thinking = Assert.Single(target.Messages);
        Assert.False(thinking.IsThinkingRunning);
        Assert.Equal(EventStatus.Error, thinking.Status);
    }

    [Fact]
    public void AppendCompletedTurn_AfterInterrupt_MarksFinalOutputAsStopped()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);
        projection.Apply(Event("interrupt_requested", "{\"type\":\"interrupt_requested\"}"));
        projection.Apply(Event("assistant", "{\"content\":\"What would you like me to do?\"}"));

        projection.AppendCompletedTurn("What would you like me to do?");

        var reply = Assert.Single(target.Messages, message => message.IsAgent);
        Assert.Equal("Stopped", reply.EventMeta);
    }

    [Fact]
    public void AppendCompletedTurn_AfterInterruptSendFailure_DoesNotMarkOutputStopped()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);
        projection.Apply(Event("interrupt_requested", "{\"type\":\"interrupt_requested\"}"));
        projection.Apply(Event("interrupt_request_failed", "{\"type\":\"interrupt_request_failed\"}"));

        projection.AppendCompletedTurn("normal reply");

        var reply = Assert.Single(target.Messages, message => message.IsAgent);
        Assert.Null(reply.EventMeta);
    }

    [Fact]
    public void Apply_ConsecutiveToolCalls_UsesSingleActivityBubbleWithMultipleSteps()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("tool_call", "{\"tool_id\":\"one\",\"name\":\"read_file\",\"args\":{\"path\":\"a.txt\"}}"));
        projection.Apply(Event("tool_call", "{\"tool_id\":\"two\",\"name\":\"search_web\",\"args\":{\"query\":\"test\"}}"));

        var bubble = Assert.Single(target.Messages);
        Assert.Equal("tool_activity", bubble.EventKind);
        Assert.Equal(2, bubble.ToolActivity!.Steps.Count);
    }

    [Fact]
    public void Apply_OnboardRequiredRepeated_AddsOnlyOnePendingBubble()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("ONBOARD_REQUIRED", "{}"));
        projection.Apply(Event("ONBOARD_REQUIRED", "{}"));

        Assert.Single(target.Messages);
        Assert.True(target.Messages[0].IsOnboarding);
    }

    [Fact]
    public void Apply_OnboardSuccess_RetiresThePendingInviteCard()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("ONBOARD_REQUIRED", "{}"));
        projection.Apply(Event("ONBOARD_SUCCESS", "{}"));

        var card = target.Messages[0];
        Assert.True(card.IsOnboardingSubmitted);
        Assert.False(card.IsOnboarding);
        Assert.False(card.IsOnboardingInputEnabled);
        Assert.Equal("Onboarding complete", target.Messages[1].EventTitle);
    }

    [Fact]
    public void Apply_ModeChangedByAgent_AddsCardSayingWhoChangedIt()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("mode_changed", "{\"mode\":\"plan\",\"triggered_by\":\"agent\"}"));

        var bubble = Assert.Single(target.Messages);
        Assert.Equal("activity", bubble.EventKind);
        Assert.Equal("Mode: Plan", bubble.EventTitle);
        // The agent can enter plan mode on its own, and that has to be visible: it changes what the
        // agent is allowed to do for the rest of the turn.
        Assert.Equal("by agent", bubble.EventMeta);
    }

    [Fact]
    public void AgentImage_IsHeldUntilTurnCompletes_ThenAttachesToFinalReply()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        // The image arrives mid-turn, before the assistant reply and the usage summary.
        projection.Apply(Event("agent_image", "{\"type\":\"agent_image\",\"image\":\"data:image/png;base64,AAAA\"}"));

        // Nothing rendered yet: it must wait for the agent's final output.
        Assert.Empty(target.Messages);
        Assert.Empty(target.ResolvedImages);

        projection.Apply(Event("assistant", "{\"content\":\"here you go\"}"));
        projection.AppendCompletedTurn("here you go");

        // Order: usage summary, then final reply carrying the image — never above the reply.
        Assert.Equal(2, target.Messages.Count);
        Assert.True(target.Messages[0].IsEvent);
        var reply = target.Messages[1];
        Assert.True(reply.IsAgent);
        Assert.Equal("here you go", reply.Content);
        Assert.Single(reply.Attachments);
        Assert.Equal(AttachmentKind.Image, reply.Attachments[0].Kind);
        Assert.Single(target.ResolvedImages);
    }

    [Fact]
    public void AgentImage_WithNoTextReply_GetsItsOwnBubbleAfterTheSummary()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("agent_image", "{\"type\":\"agent_image\",\"image\":\"data:image/png;base64,AAAA\"}"));
        projection.AppendCompletedTurn("");

        // Summary first, then a fresh agent bubble holding the image — still after the output.
        Assert.Equal(2, target.Messages.Count);
        Assert.True(target.Messages[0].IsEvent);
        Assert.True(target.Messages[1].IsAgent);
        Assert.Single(target.Messages[1].Attachments);
    }

    private static AgentStreamEvent Event(string type, string json) => new(type, type, null, json);

    private sealed class FakeTarget : IChatProjectionTarget
    {
        private long _nextId = 1;
        public IList<ChatMessage> Messages { get; } = new List<ChatMessage>();
        public List<string> ResolvedImages { get; } = new();
        public string? AgentName => "Test Agent";
        public bool IsLiveView { get; set; }
        public long NextId() => _nextId++;
        public void Add(ChatMessage message) => Messages.Add(message);
        public void ResolveAgentImage(string dataUrl, ChatAttachment attachment)
        {
            ResolvedImages.Add(dataUrl);
            attachment.Status = AttachmentStatus.Sent;
        }
    }
}
