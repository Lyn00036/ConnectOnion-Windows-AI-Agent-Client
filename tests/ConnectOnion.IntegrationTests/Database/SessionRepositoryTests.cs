using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.IntegrationTests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class SessionRepositoryTests
{
    private readonly TempDatabaseFixture _fixture;
    private readonly SessionRepository _sessions = new();
    private readonly ConversationRepository _messages = new();

    public SessionRepositoryTests(TempDatabaseFixture fixture) => _fixture = fixture;

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
