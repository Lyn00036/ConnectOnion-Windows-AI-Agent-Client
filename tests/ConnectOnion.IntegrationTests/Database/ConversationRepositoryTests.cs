using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.IntegrationTests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class ConversationRepositoryTests
{
    private readonly TempDatabaseFixture _fixture;
    private readonly ConversationRepository _repository = new();

    public ConversationRepositoryTests(TempDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task UpsertMessagesAsync_SubsetUpdate_LeavesOtherRowsUnchanged()
    {
        await _fixture.CreateSessionAsync();
        await _repository.UpsertMessagesAsync("conversation-1", new[]
        {
            Message(1, "keep me"),
            Message(2, "old value"),
        });

        await _repository.UpsertMessagesAsync("conversation-1", new[] { Message(2, "new value") });

        var messages = await _repository.LoadMessagesAsync("conversation-1");
        Assert.Collection(messages,
            first => Assert.Equal("keep me", first.Content),
            second => Assert.Equal("new value", second.Content));
    }

    [Fact]
    public async Task UpsertMessagesAsync_ExistingMessage_PreservesCreatedAt()
    {
        await _fixture.CreateSessionAsync("created-at");
        await _repository.UpsertMessagesAsync("created-at", new[] { Message(1, "before") });
        var createdAt = await ReadCreatedAtAsync("created-at", 1);

        await Task.Delay(10);
        await _repository.UpsertMessagesAsync("created-at", new[] { Message(1, "after") });

        Assert.Equal(createdAt, await ReadCreatedAtAsync("created-at", 1));
    }

    [Fact]
    public async Task UpsertMessagesAsync_ChangedAttachments_ReplacesOnlyTargetMessageAttachments()
    {
        await _fixture.CreateSessionAsync("attachments");
        var first = Message(1, "first", Attachment("old-1"), Attachment("old-2"));
        var second = Message(2, "second", Attachment("keep"));
        await _repository.UpsertMessagesAsync("attachments", new[] { first, second });

        await _repository.UpsertMessagesAsync("attachments", new[]
        {
            Message(1, "first", Attachment("replacement")),
        });

        var messages = await _repository.LoadMessagesAsync("attachments");
        Assert.Equal(new[] { "replacement" }, messages[0].Attachments.Select(a => a.Id));
        Assert.Equal(new[] { "keep" }, messages[1].Attachments.Select(a => a.Id));
    }

    [Fact]
    public async Task UpsertMessagesAsync_Attachment_DoesNotPersistBase64Payload()
    {
        await _fixture.CreateSessionAsync("no-payload");
        await _repository.UpsertMessagesAsync("no-payload", new[]
        {
            Message(1, "image", new ChatAttachment
            {
                Id = "image-1",
                Kind = AttachmentKind.Image,
                FileName = "image.png",
                MimeType = "image/png",
                LocalCachePath = "C:\\cache\\image.png",
                RemoteUri = "https://example.test/image.png",
            }),
        });

        await using var connection = await AppDatabase.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT file_name, mime_type, local_cache_path, remote_uri
            FROM message_attachments
            WHERE conversation_id = 'no-payload' AND message_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var persisted = string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(reader.GetString));
        Assert.DoesNotContain("data:image/png;base64", persisted, StringComparison.Ordinal);
        Assert.Equal("image.png", reader.GetString(0));
    }

    [Fact]
    public async Task UpsertMessagesAsync_OutOfOrderIds_LoadsInAscendingIdOrder()
    {
        await _fixture.CreateSessionAsync("ordering");
        await _repository.UpsertMessagesAsync("ordering", new[]
        {
            Message(100, "last"),
            Message(2, "middle"),
            Message(1, "first"),
        });

        var messages = await _repository.LoadMessagesAsync("ordering");

        Assert.Equal(new long[] { 1, 2, 100 }, messages.Select(message => message.Id));
    }

    [Fact]
    public async Task GetNextMessageIdAsync_EmptyAndPopulatedConversation_ReturnsMaxPlusOne()
    {
        await _fixture.CreateSessionAsync("next-id");
        Assert.Equal(1, await _repository.GetNextMessageIdAsync("next-id"));
        await _repository.UpsertMessagesAsync("next-id", new[] { Message(5, "five"), Message(100, "hundred") });

        Assert.Equal(101, await _repository.GetNextMessageIdAsync("next-id"));
    }

    [Fact]
    public async Task LoadLastAgentMessageAsync_MixedRoles_ReturnsLatestAgentWithAttachments()
    {
        await _fixture.CreateSessionAsync("last-agent");
        await _repository.UpsertMessagesAsync("last-agent", new[]
        {
            Message(1, "user"),
            AgentMessage(2, "older", Attachment("old-agent-file")),
            AgentMessage(3, "latest", Attachment("latest-agent-file")),
            Message(4, "newer user"),
        });

        var message = await _repository.LoadLastAgentMessageAsync("last-agent");

        Assert.NotNull(message);
        Assert.Equal(3, message.Id);
        Assert.Equal("latest", message.Content);
        Assert.Equal("latest-agent-file", Assert.Single(message.Attachments).Id);
    }

    [Fact]
    public async Task DeleteMessagesAsync_Conversation_RemovesMessagesAndAttachmentsOnlyForTarget()
    {
        await _fixture.CreateSessionAsync("delete-target");
        await _fixture.CreateSessionAsync("delete-keep");
        await _repository.UpsertMessagesAsync("delete-target", new[] { Message(1, "delete", Attachment("delete-file")) });
        await _repository.UpsertMessagesAsync("delete-keep", new[] { Message(1, "keep", Attachment("keep-file-2")) });

        await _repository.DeleteMessagesAsync("delete-target");

        Assert.Empty(await _repository.LoadMessagesAsync("delete-target"));
        var kept = Assert.Single(await _repository.LoadMessagesAsync("delete-keep"));
        Assert.Equal("keep-file-2", Assert.Single(kept.Attachments).Id);
    }

    [Fact]
    public async Task DeleteMessageAsync_RemovesOnlyThatBubbleAndItsAttachments()
    {
        // What un-sending a queued message does: the row was written at submit time, so backing
        // out has to take it — and nothing else — away again.
        await _fixture.CreateSessionAsync("unsend");
        await _repository.UpsertMessagesAsync("unsend", new[]
        {
            Message(1, "earlier", Attachment("keep-file")),
            Message(2, "cancelled", Attachment("drop-file")),
        });

        await _repository.DeleteMessageAsync("unsend", 2);

        var remaining = Assert.Single(await _repository.LoadMessagesAsync("unsend"));
        Assert.Equal(1, remaining.Id);
        Assert.Equal("keep-file", Assert.Single(remaining.Attachments).Id);
    }

    [Fact]
    public async Task DeleteMessageAsync_MissingRow_IsANoOp()
    {
        // The cancel path races the run actually starting; losing that race must leave the
        // conversation untouched rather than throwing.
        await _fixture.CreateSessionAsync("unsend-missing");
        await _repository.UpsertMessagesAsync("unsend-missing", new[] { Message(1, "kept") });

        await _repository.DeleteMessageAsync("unsend-missing", 99);

        Assert.Single(await _repository.LoadMessagesAsync("unsend-missing"));
    }

    [Fact]
    public async Task UpsertMessagesAsync_EventFields_RoundTripsPersistedSnapshot()
    {
        await _fixture.CreateSessionAsync("event-roundtrip");
        var expected = new ChatMessage
        {
            Id = 7,
            Role = ChatRole.Event,
            Content = "content",
            AgentName = "Agent",
            // Any kind but "approval" — those are filtered out of every read (see below).
            EventKind = "plan_review",
            EventKey = "event-key",
            EventEyebrow = "Plan review",
            EventTitle = "Run command",
            EventDetail = "description",
            EventMeta = "Approved once",
            EventArgs = "{\"command\":\"echo test\"}",
            EventResult = "ok",
            Status = EventStatus.Done,
            IsOnboarding = true,
        };
        await _repository.UpsertMessagesAsync("event-roundtrip", new[] { expected });

        var actual = Assert.Single(await _repository.LoadMessagesAsync("event-roundtrip"));

        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Role, actual.Role);
        Assert.Equal(expected.Content, actual.Content);
        Assert.Equal(expected.AgentName, actual.AgentName);
        Assert.Equal(expected.EventKind, actual.EventKind);
        Assert.Equal(expected.EventKey, actual.EventKey);
        Assert.Equal(expected.EventEyebrow, actual.EventEyebrow);
        Assert.Equal(expected.EventTitle, actual.EventTitle);
        Assert.Equal(expected.EventDetail, actual.EventDetail);
        Assert.Equal(expected.EventMeta, actual.EventMeta);
        Assert.Equal(expected.EventArgs, actual.EventArgs);
        Assert.Equal(expected.EventResult, actual.EventResult);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.IsOnboarding, actual.IsOnboarding);
    }

    [Theory]
    [InlineData(DiffChangeState.Applied, false)]
    [InlineData(DiffChangeState.Rejected, false)]
    [InlineData(DiffChangeState.Pending, true)]
    [InlineData(DiffChangeState.Failed, true)]
    public async Task DiffStateAndContent_RoundTripWithCorrectDefaultExpansion(
        DiffChangeState state, bool expectedExpanded)
    {
        var conversationId = $"diff-{state}";
        await _fixture.CreateSessionAsync(conversationId);
        var diff = new ChatMessage
        {
            Id = 1,
            Role = ChatRole.Event,
            EventKind = "diff_preview",
            EventTitle = "/tmp/test.txt",
            EventEyebrow = "CREATE",
            EventDetail = "+ hello world",
            Status = EventStatus.Done,
        };
        diff.SetDiffState(state);

        await _repository.UpsertMessagesAsync(conversationId, new[] { diff });
        var restored = Assert.Single(await _repository.LoadMessagesAsync(conversationId));

        Assert.Equal(state, restored.DiffState);
        Assert.Equal("+ hello world", restored.EventDetail);
        Assert.Equal(1, restored.DiffPreview.Additions);
        Assert.Equal(expectedExpanded, restored.IsDiffExpanded);
    }

    [Fact]
    public async Task Reads_ExcludeApprovalBubbles_EvenWhenARowExists()
    {
        // The persist path no longer writes these, but rows survive in databases created before
        // that change — and a settled approval is not shown anywhere in the transcript, so the
        // reads must not surface one whatever is on disk.
        await _fixture.CreateSessionAsync("approval-hidden");
        await _repository.UpsertMessagesAsync("approval-hidden", new[]
        {
            new ChatMessage { Id = 1, Role = ChatRole.User, Content = "send it" },
            new ChatMessage
            {
                Id = 2,
                Role = ChatRole.Event,
                EventKind = "approval",
                EventTitle = "send",
                EventMeta = "Skipped",
                Status = EventStatus.Error,
            },
            new ChatMessage { Id = 3, Role = ChatRole.Agent, Content = "done" },
        });

        var all = await _repository.LoadMessagesAsync("approval-hidden");

        Assert.Equal(new long[] { 1, 3 }, all.Select(m => m.Id));
    }

    private static ChatMessage Message(long id, string content, params ChatAttachment[] attachments)
    {
        var message = new ChatMessage { Id = id, Role = ChatRole.User, Content = content };
        foreach (var attachment in attachments) message.Attachments.Add(attachment);
        return message;
    }

    private static ChatMessage AgentMessage(long id, string content, params ChatAttachment[] attachments)
    {
        var message = Message(id, content, attachments);
        message.Role = ChatRole.Agent;
        return message;
    }

    private static ChatAttachment Attachment(string id) => new()
    {
        Id = id,
        Kind = AttachmentKind.File,
        FileName = id + ".txt",
        MimeType = "text/plain",
        SizeBytes = 10,
        LocalCachePath = "C:\\cache\\" + id + ".txt",
        Status = AttachmentStatus.Sent,
    };

    private static async Task<long> ReadCreatedAtAsync(string conversationId, long messageId)
    {
        await using var connection = await AppDatabase.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT created_at FROM messages WHERE conversation_id = $conversation AND id = $id;";
        command.Parameters.AddWithValue("$conversation", conversationId);
        command.Parameters.AddWithValue("$id", messageId);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
