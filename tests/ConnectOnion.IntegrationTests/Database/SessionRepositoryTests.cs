using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.IntegrationTests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class SessionRepositoryTests : IAsyncLifetime
{
    private readonly TempDatabaseFixture _fixture;
    private readonly SessionRepository _sessions = new();
    private readonly ConversationRepository _messages = new();

    public SessionRepositoryTests(TempDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync()
    {
        // SaveAsync owns the complete session index and legitimately removes rows not present in
        // its input. Start each case with an empty index so child rows left by another class in
        // this collection cannot make that delete fail; xunit v3 may choose any class order.
        await using var connection = await AppDatabase.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM message_attachments;
            DELETE FROM messages;
            DELETE FROM trace_events;
            DELETE FROM executions;
            DELETE FROM sessions;
            DELETE FROM app_meta;
            DELETE FROM agents;
            INSERT INTO agents (id, name, address) VALUES ('agent-1', 'Agent', '0x1');
            """;
        await command.ExecuteNonQueryAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SaveUpdateAndDelete_Conversation_PersistsEachLifecycleStage()
    {
        var session = Summary("session-lifecycle", "Original", "2026-01-01T00:00:00Z");
        await _sessions.SaveAsync(new SessionsState
        {
            Sessions = new List<SessionSummary> { session },
            ActiveSessionId = session.Id,
        });

        var created = await _sessions.LoadAsync();
        Assert.Equal(session.Id, created.ActiveSessionId);
        Assert.Equal("Original", Assert.Single(created.Sessions).Title);

        session.Title = "Renamed";
        session.UpdatedAt = "2026-01-02T00:00:00Z";
        await _sessions.UpdateSessionAsync(session);
        Assert.Equal("Renamed", Assert.Single((await _sessions.LoadAsync()).Sessions).Title);

        await _messages.DeleteMessagesAsync(session.Id);
        await _sessions.SaveAsync(new SessionsState());
        var deleted = await _sessions.LoadAsync();
        Assert.Empty(deleted.Sessions);
        Assert.Null(deleted.ActiveSessionId);
    }

    [Fact]
    public async Task Mode_SurvivesSaveAndUpdate_AndDefaultsToSafe()
    {
        var session = Summary("session-mode", "Modes", "2026-01-01T00:00:00Z");
        await _sessions.SaveAsync(new SessionsState { Sessions = new List<SessionSummary> { session } });

        // The mode is client-owned: the host forgets ours between turns, so a conversation that
        // reloads at the wrong mode would silently start auto-approving (or refusing) tools.
        Assert.Equal("safe", Assert.Single((await _sessions.LoadAsync()).Sessions).Mode);

        session.Mode = "plan";
        await _sessions.UpdateSessionAsync(session);
        Assert.Equal("plan", Assert.Single((await _sessions.LoadAsync()).Sessions).Mode);

        // A mode the host wouldn't honour is not a mode: it falls back rather than being sent.
        session.Mode = "yolo";
        Assert.Equal("safe", session.Mode);
    }

    [Fact]
    public async Task LoadAsync_SameSortOrder_OrdersByUpdatedAtDescending()
    {
        await InsertSessionAsync("order-old", "Old", "2026-01-01T00:00:00Z", sortOrder: 50);
        await InsertSessionAsync("order-new", "New", "2026-03-01T00:00:00Z", sortOrder: 50);
        await InsertSessionAsync("order-middle", "Middle", "2026-02-01T00:00:00Z", sortOrder: 50);

        var state = await _sessions.LoadAsync();
        var ordered = state.Sessions
            .Where(session => session.Id.StartsWith("order-", StringComparison.Ordinal))
            .Select(session => session.Id);

        Assert.Equal(new[] { "order-new", "order-middle", "order-old" }, ordered);
    }

    [Fact]
    public async Task SaveAsync_PinnedAndActiveSession_RoundTripsMetadata()
    {
        var pinned = Summary("pinned-session", "Pinned", "2026-04-01T00:00:00Z");
        pinned.IsPinned = true;

        await _sessions.SaveAsync(new SessionsState
        {
            Sessions = new List<SessionSummary> { pinned },
            ActiveSessionId = pinned.Id,
        });

        var loaded = await _sessions.LoadAsync();
        var actual = Assert.Single(loaded.Sessions);
        Assert.True(actual.IsPinned);
        Assert.Equal(pinned.Id, loaded.ActiveSessionId);
    }

    [Fact]
    public async Task SaveAsync_InvalidSession_SkipsRowAndClearsStaleActivePointer()
    {
        await _sessions.SaveAsync(new SessionsState
        {
            Sessions = new List<SessionSummary>
            {
                Summary("", "Missing id", "2026-01-01T00:00:00Z"),
                Summary("invalid-title", "", "2026-01-01T00:00:00Z"),
            },
            ActiveSessionId = "missing",
        });

        var loaded = await _sessions.LoadAsync();
        Assert.Empty(loaded.Sessions);
        Assert.Null(loaded.ActiveSessionId);
    }

    [Fact]
    public async Task AppendSession_AddsOneRowAndActivatesIt_WithoutDisturbingTheIndex()
    {
        var first = Summary("append-first", "First", "2026-01-01T00:00:00Z");
        var second = Summary("append-second", "Second", "2026-01-02T00:00:00Z");
        second.IsPinned = true;
        await _sessions.SaveAsync(new SessionsState
        {
            Sessions = new List<SessionSummary> { first, second },
            ActiveSessionId = first.Id,
        });

        var appended = Summary("append-third", "Third", "2026-01-03T00:00:00Z");
        await _sessions.AppendSessionAsync(appended);

        var state = await _sessions.LoadAsync();
        // The point of the targeted write: the two existing rows are untouched, including the
        // pinned flag that lives outside the sessions table entirely.
        Assert.Equal(3, state.Sessions.Count);
        Assert.Equal(appended.Id, state.ActiveSessionId);
        Assert.True(state.Sessions.Single(s => s.Id == second.Id).IsPinned);
        Assert.False(state.Sessions.Single(s => s.Id == appended.Id).IsPinned);
        // sort_order continues the sequence, so LoadAsync's ORDER BY puts the new row last —
        // matching where the caller's in-memory Sessions.Add placed it.
        Assert.Equal(appended.Id, state.Sessions[^1].Id);

        await _messages.DeleteMessagesAsync(first.Id);
        await _sessions.SaveAsync(new SessionsState());
    }

    [Fact]
    public async Task AppendSession_WithoutMakeActive_LeavesTheActivePointerAlone()
    {
        var existing = Summary("append-keep-active", "Existing", "2026-01-01T00:00:00Z");
        await _sessions.SaveAsync(new SessionsState
        {
            Sessions = new List<SessionSummary> { existing },
            ActiveSessionId = existing.Id,
        });

        await _sessions.AppendSessionAsync(
            Summary("append-inactive", "Inactive", "2026-01-02T00:00:00Z"),
            makeActive: false);

        Assert.Equal(existing.Id, (await _sessions.LoadAsync()).ActiveSessionId);
        await _sessions.SaveAsync(new SessionsState());
    }

    [Fact]
    public async Task SetPinned_TogglesOneConversation_AndClearsTheMetadataRowWhenEmpty()
    {
        var pinned = Summary("pin-target", "Pinned", "2026-01-01T00:00:00Z");
        var other = Summary("pin-other", "Other", "2026-01-02T00:00:00Z");
        await _sessions.SaveAsync(new SessionsState
        {
            Sessions = new List<SessionSummary> { pinned, other },
        });

        await _sessions.SetPinnedAsync(pinned.Id, isPinned: true);
        var afterPin = await _sessions.LoadAsync();
        Assert.True(afterPin.Sessions.Single(s => s.Id == pinned.Id).IsPinned);
        Assert.False(afterPin.Sessions.Single(s => s.Id == other.Id).IsPinned);

        await _sessions.SetPinnedAsync(pinned.Id, isPinned: false);
        Assert.All((await _sessions.LoadAsync()).Sessions, s => Assert.False(s.IsPinned));

        await _sessions.SaveAsync(new SessionsState());
    }

    private static SessionSummary Summary(string id, string title, string updatedAt) => new()
    {
        Id = id,
        AgentId = "agent-1",
        Title = title,
        CreatedAt = "2026-01-01T00:00:00Z",
        UpdatedAt = updatedAt,
    };

    private static async Task InsertSessionAsync(string id, string title, string updatedAt, int sortOrder)
    {
        await using var connection = await AppDatabase.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sessions (id, agent_id, title, created_at, updated_at, sort_order)
            VALUES ($id, 'agent-1', $title, '2026-01-01T00:00:00Z', $updated, $sort);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$updated", updatedAt);
        command.Parameters.AddWithValue("$sort", sortOrder);
        await command.ExecuteNonQueryAsync();
    }
}
