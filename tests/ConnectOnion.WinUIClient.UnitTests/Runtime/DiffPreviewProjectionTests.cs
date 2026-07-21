using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services.Runtime;

namespace ConnectOnion.WinUIClient.UnitTests.Runtime;

/// <summary>
/// diff_preview and the client-originated reconnect markers.
/// <para>The diff_preview contract is the one worth pinning down: DiffWriter sends it for
/// display only and never waits on it (<c>_send_preview</c> in diff_writer.py), asking for
/// approval through a separate ask_user. So the card it projects must carry no interactive
/// state — a card with buttons here would answer a frame nothing is waiting on and leave the
/// real ask_user blocked forever.</para>
/// </summary>
public class DiffPreviewProjectionTests
{
    [Fact]
    public void DiffPreview_ProjectsReadOnlyCard()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("diff_preview",
            """{"type":"diff_preview","path":"src/app.py","preview":"@@ -1 +1 @@\n-old\n+new","truncated":false,"file_exists":true}"""));

        var card = Assert.Single(target.Messages);
        Assert.Equal("diff_preview", card.EventKind);
        Assert.Equal("src/app.py", card.EventTitle);
        Assert.Equal("MODIFY", card.EventEyebrow);
        Assert.Contains("+new", card.EventDetail);
        // Never interactive: no pending state, nothing for a user to resolve.
        Assert.False(card.IsInteractiveEvent);
        Assert.Equal(EventStatus.Done, card.Status);
    }

    [Fact]
    public void DiffPreview_LabelsNewFileAsCreate()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("diff_preview",
            """{"type":"diff_preview","path":"new.txt","preview":"+hello","file_exists":false}"""));

        Assert.Equal("CREATE", Assert.Single(target.Messages).EventEyebrow);
    }

    /// <summary>An empty preview is the writer saying it found nothing to change. Saying so
    /// beats a blank card, which reads as a failure.</summary>
    [Fact]
    public void DiffPreview_EmptyPreviewSaysNoChanges()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("diff_preview",
            """{"type":"diff_preview","path":"same.txt","preview":"","file_exists":true}"""));

        Assert.Equal("(no changes)", Assert.Single(target.Messages).EventDetail);
    }

    [Fact]
    /// <summary>The host already appends a literal "...(truncated)" line to the preview body
    /// (<c>_truncate_preview</c> in diff_writer.py), so the card must not add a badge restating
    /// it — the marker the user sees comes from the diff text itself.</summary>
    public void DiffPreview_DoesNotRestateTruncationOutsideTheBody()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("diff_preview",
            """{"type":"diff_preview","path":"big.txt","preview":"+a\n...(truncated)","truncated":true}"""));

        var card = Assert.Single(target.Messages);
        Assert.Null(card.EventMeta);
        Assert.Contains("...(truncated)", card.EventDetail);
    }

    /// <summary>Five attempts must not leave five rows behind.</summary>
    [Fact]
    public void Reconnecting_ReusesOneRowAcrossAttempts()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);

        projection.Apply(Event("reconnecting", """{"type":"reconnecting","attempt":1,"max_attempts":5}"""));
        projection.Apply(Event("reconnecting", """{"type":"reconnecting","attempt":2,"max_attempts":5}"""));
        projection.Apply(Event("reconnecting", """{"type":"reconnecting","attempt":3,"max_attempts":5}"""));

        var row = Assert.Single(target.Messages);
        Assert.Equal("Reconnecting (3/5)", row.EventTitle);
        Assert.Equal(EventStatus.Running, row.Status);
    }

    /// <summary>The socket comes back either way; only "running" means the turn survived, and
    /// the row has to say which happened.</summary>
    [Fact]
    public void Reconnected_DistinguishesResumedFromLostTurn()
    {
        var resumed = ProjectReconnect(resumedFlag: true);
        Assert.Equal("Reconnected - resuming", resumed.EventTitle);
        Assert.Equal(EventStatus.Done, resumed.Status);

        var lost = ProjectReconnect(resumedFlag: false);
        Assert.Equal("Reconnected", lost.EventTitle);
        Assert.Contains("no longer running", lost.EventDetail);
    }

    private static ChatMessage ProjectReconnect(bool resumedFlag)
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);
        projection.Apply(Event("reconnecting", """{"type":"reconnecting","attempt":1,"max_attempts":5}"""));
        projection.Apply(Event("reconnected",
            $$"""{"type":"reconnected","status":"{{(resumedFlag ? "running" : "connected")}}","resumed":{{(resumedFlag ? "true" : "false")}}}"""));
        return Assert.Single(target.Messages);
    }

    private static AgentStreamEvent Event(string type, string json) => new(type, type, null, json);

    private sealed class FakeTarget : IChatProjectionTarget
    {
        private long _nextId = 1;
        public IList<ChatMessage> Messages { get; } = new List<ChatMessage>();
        public string? AgentName => "Test Agent";
        public bool IsLiveView { get; set; }
        public long NextId() => _nextId++;
        public void Add(ChatMessage message) => Messages.Add(message);
        public void ResolveAgentImage(string dataUrl, ChatAttachment attachment)
            => attachment.Status = AttachmentStatus.Sent;
    }
}
