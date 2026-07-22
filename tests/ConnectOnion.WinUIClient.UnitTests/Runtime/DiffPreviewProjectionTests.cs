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

    [Fact]
    public void WriteDiff_IsPendingAndExpanded_ThenOnlySuccessfulToolResultMarksApplied()
    {
        var target = new FakeTarget { IsLiveView = true };
        var projection = new ChatTurnProjection(target);
        projection.Apply(Event("tool_call",
            """{"tool_id":"write-1","name":"remote_write_file","args":{"path":"/tmp/test.txt"}}"""));
        projection.Apply(Event("diff_preview",
            """{"type":"diff_preview","path":"/tmp/test.txt","preview":"+ hello world","file_exists":false}"""));
        projection.Apply(Event("ask_user",
            """{"id":"ask-1","question":"Apply changes to /tmp/test.txt?","options":["Yes, apply this change","No, reject and give feedback"],"multi_select":false}"""));

        var diff = target.Messages.Single(message => message.IsDiffPreviewEvent);
        var ask = target.Messages.Single(message => message.IsAskUserEvent);
        Assert.Equal(DiffChangeState.Pending, diff.DiffState);
        Assert.True(diff.IsDiffExpanded);
        Assert.Equal(1, diff.DiffPreview.Additions);
        Assert.Equal(0, diff.DiffPreview.Deletions);

        ask.EventMeta = "Answered: Yes, apply this change";
        Assert.Equal(DiffChangeState.Applying, diff.DiffState);
        Assert.True(diff.IsDiffExpanded);

        projection.Apply(Event("tool_result",
            """{"tool_id":"write-1","status":"success","result":"File written successfully."}"""));
        Assert.Equal(DiffChangeState.Applied, diff.DiffState);
        Assert.False(diff.IsDiffExpanded);
        Assert.Equal("Changes applied to test.txt", diff.DiffCardTitle);
    }

    [Fact]
    public void RejectedWrite_FoldsButKeepsFullDiffAvailable()
    {
        var (projection, target, diff, ask) = ProjectWriteApproval();
        ask.EventMeta = "Answered: No, reject and give feedback";

        Assert.Equal(DiffChangeState.Rejected, diff.DiffState);
        Assert.False(diff.IsDiffExpanded);
        Assert.Contains("+new", diff.EventDetail);

        diff.ToggleDiffExpanded();
        Assert.True(diff.IsDiffExpanded);
        Assert.Contains("+new", diff.DiffPreview.RawText);
    }

    [Fact]
    public void FailedWrite_RemainsExpandedAndNeverClaimsApplied()
    {
        var (projection, _, diff, ask) = ProjectWriteApproval();
        ask.EventMeta = "Answered: Yes, apply this change";
        projection.Apply(Event("tool_result",
            """{"tool_id":"write-1","status":"error","error":"permission denied"}"""));

        Assert.Equal(DiffChangeState.Failed, diff.DiffState);
        Assert.True(diff.IsDiffExpanded);
        Assert.True(diff.ShowDiffProblem);
        Assert.NotEqual("Changes applied to test.txt", diff.DiffCardTitle);
    }

    [Fact]
    public void CompletedWithoutToolResult_IsUnconfirmedAndExpanded()
    {
        var (projection, _, diff, ask) = ProjectWriteApproval();
        ask.EventMeta = "Answered: Yes, apply this change";

        projection.AppendCompletedTurn("");

        Assert.Equal(DiffChangeState.Unconfirmed, diff.DiffState);
        Assert.True(diff.IsDiffExpanded);
        Assert.DoesNotContain("Changes applied", diff.DiffCardTitle, StringComparison.Ordinal);
    }

    [Fact]
    public void InformationalDiffPreview_DoesNotPretendToAwaitApproval()
    {
        var target = new FakeTarget();
        var projection = new ChatTurnProjection(target);
        projection.Apply(Event("tool_call",
            """{"tool_id":"preview-1","name":"remote_diff_preview","args":{"path":"/tmp/test.txt"}}"""));
        projection.Apply(Event("diff_preview",
            """{"type":"diff_preview","path":"/tmp/test.txt","preview":"@@ -1 +1 @@\n-old\n+new","file_exists":true}"""));

        var diff = target.Messages.Single(message => message.IsDiffPreviewEvent);
        Assert.Equal(DiffChangeState.Preview, diff.DiffState);
        Assert.True(diff.IsDiffExpanded);
        Assert.False(diff.IsInteractiveEvent);
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

    private static (ChatTurnProjection Projection, FakeTarget Target, ChatMessage Diff, ChatMessage Ask)
        ProjectWriteApproval()
    {
        var target = new FakeTarget { IsLiveView = true };
        var projection = new ChatTurnProjection(target);
        projection.Apply(Event("tool_call",
            """{"tool_id":"write-1","name":"remote_write_file","args":{"path":"/tmp/test.txt"}}"""));
        projection.Apply(Event("diff_preview",
            """{"type":"diff_preview","path":"/tmp/test.txt","preview":"@@ -1 +1 @@\n-old\n+new","file_exists":true}"""));
        projection.Apply(Event("ask_user",
            """{"id":"ask-1","question":"Apply changes to /tmp/test.txt?","options":["Yes, apply this change","No, reject and give feedback"],"multi_select":false}"""));
        return (projection, target,
            target.Messages.Single(message => message.IsDiffPreviewEvent),
            target.Messages.Single(message => message.IsAskUserEvent));
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
