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
    public void Apply_Thinking_RemainsCollapsedAndLoadingUntilReplyArrives()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("thinking", "{\"content\":\"Checking the request.\"}"));

        var thinking = Assert.Single(target.Messages);
        Assert.True(thinking.IsThinkingRunning);
        Assert.False(thinking.IsActivityExpanded);
        Assert.Equal("Thinking...", thinking.ActivityHeaderTitle);

        projection.Apply(Event("assistant", "{\"content\":\"Final reply\"}"));

        Assert.False(thinking.IsThinkingRunning);
        Assert.Equal(EventStatus.Done, thinking.Status);
        Assert.Equal("Thought process", thinking.ActivityHeaderTitle);
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

    private static AgentStreamEvent Event(string type, string json) => new(type, type, null, json);

    private sealed class FakeTarget : IChatProjectionTarget
    {
        private long _nextId = 1;
        public IList<ChatMessage> Messages { get; } = new List<ChatMessage>();
        public string? AgentName => "Test Agent";
        public long NextId() => _nextId++;
        public void Add(ChatMessage message) => Messages.Add(message);
        public void ResolveAgentImage(string dataUrl, ChatAttachment attachment) { }
    }
}
