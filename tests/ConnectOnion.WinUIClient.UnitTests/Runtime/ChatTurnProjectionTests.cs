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
