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

    [Fact]
    public void Apply_StopRequested_SaysItWasOnlyRequestedNotDone()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("stop_requested", "{\"stopped\":false}"));

        var bubble = Assert.Single(target.Messages);
        Assert.Equal("Stop requested", bubble.EventTitle);
        // A cooperative stop is a message to the model, not an interrupt — claiming "Stopped" while
        // the spinner keeps going would be a lie.
        Assert.Contains("Asked the agent to stop", bubble.EventDetail);
    }

    [Fact]
    public void Apply_StopThatActuallyHalted_SaysStopped()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("stop_requested", "{\"stopped\":true}"));

        var bubble = Assert.Single(target.Messages);
        Assert.Equal("Stopped", bubble.EventTitle);
        Assert.Null(bubble.EventDetail);
    }

    [Fact]
    public void Apply_StopForced_WarnsTheAgentMayStillBeRunning()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("stop_forced", "{\"detail\":\"Stopped locally. The agent may still be running.\"}"));

        var bubble = Assert.Single(target.Messages);
        Assert.Equal("Force-stopped", bubble.EventTitle);
        Assert.Equal(EventStatus.Error, bubble.Status);
        Assert.Contains("may still be running", bubble.EventDetail);
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
