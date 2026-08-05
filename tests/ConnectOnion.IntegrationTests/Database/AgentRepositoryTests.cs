using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.IntegrationTests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class AgentRepositoryTests
{
    private readonly AgentRepository _repository = new();

    /// <summary>
    /// The thin read returns the same agents in the same order as the full one, and carries no
    /// <c>info_json</c>.
    ///
    /// <para>The type is the safety mechanism here: <c>SaveAsync</c> writes <c>info_json</c> from
    /// whatever object it is given, and five production call sites round-trip a load straight back
    /// into it — so a blob-less <c>AgentConfig</c> would erase every agent's cached metadata.
    /// <c>AgentSummary</c> has no route into <c>SaveAsync</c>, and this test would stop compiling
    /// if that ever changed, which is the point.</para>
    /// </summary>
    [Fact]
    public async Task LoadSummariesAsync_MatchesLoadAsync_WithoutCarryingInfoJson()
    {
        var agents = Enumerable.Range(0, 3).Select(i => new AgentConfig
        {
            Id = $"summary-{i}",
            Name = $"Summary Agent {i}",
            Address = $"0x{i}",
            DirectUrl = i % 2 == 0 ? $"https://agent-{i}.example.test" : null,
            IconPath = i == 0 ? "avatars/summary-0.png" : null,
            InfoJson = "{\"skills\":[{\"name\":\"a\",\"description\":\"a big cached payload\"}]}",
            InfoUpdatedAt = "2026-08-05T00:00:00Z",
        }).ToList();
        await _repository.SaveAsync(new AgentsState { Agents = agents, SelectedAgentId = "summary-1" });

        var full = await _repository.LoadAsync();
        var thin = await _repository.LoadSummariesAsync();

        Assert.Equal(full.Agents.Select(a => a.Id), thin.Agents.Select(a => a.Id));
        Assert.Equal(full.SelectedAgentId, thin.SelectedAgentId);
        Assert.Equal(
            full.Agents.Select(a => (a.Name, a.Address, a.DirectUrl, a.IconPath)),
            thin.Agents.Select(a => (a.Name, a.Address, a.DirectUrl, a.IconPath)));
        // Address + DirectUrl are exactly what a reachability probe dials, so the sidebar can
        // drive presence from a summary without ever reading a full agent record.
        Assert.All(thin.Agents, agent =>
            Assert.False(string.IsNullOrWhiteSpace(agent.Address) && string.IsNullOrWhiteSpace(agent.DirectUrl)));

        // The blob is still on disk — the thin read skips it, it does not drop it.
        Assert.All(full.Agents, agent => Assert.Contains("cached payload", agent.InfoJson!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveLoadAndDelete_Agent_RoundTripsLifecycleAndAllFields()
    {
        var agent = new AgentConfig
        {
            Id = "agent-crud",
            Name = "CRUD Agent",
            Address = "0x1234",
            DirectUrl = "https://agent.example.test",
            IconPath = "avatars/agent-crud.png",
            InviteCode = "invite-code",
            InfoJson = "{\"name\":\"remote\"}",
            InfoUpdatedAt = "2026-07-13T00:00:00Z",
        };
        await _repository.SaveAsync(new AgentsState
        {
            Agents = new List<AgentConfig> { agent },
            SelectedAgentId = agent.Id,
        });

        var loaded = await _repository.LoadAsync();
        var actual = Assert.Single(loaded.Agents);
        Assert.Equal(agent.Id, loaded.SelectedAgentId);
        Assert.Equal(agent.Name, actual.Name);
        Assert.Equal(agent.Address, actual.Address);
        Assert.Equal(agent.DirectUrl, actual.DirectUrl);
        Assert.Equal(agent.IconPath, actual.IconPath);
        Assert.Equal(agent.InviteCode, actual.InviteCode);
        Assert.Equal(agent.InfoJson, actual.InfoJson);
        Assert.Equal(agent.InfoUpdatedAt, actual.InfoUpdatedAt);

        await _repository.SaveAsync(new AgentsState());
        var deleted = await _repository.LoadAsync();
        Assert.Empty(deleted.Agents);
        Assert.Null(deleted.SelectedAgentId);
    }

    [Fact]
    public async Task SaveAsync_DirectUrlOnlyAgent_PersistsAsValidConnectionTarget()
    {
        var agent = new AgentConfig
        {
            Id = "direct-only",
            Name = "Direct",
            Address = "",
            DirectUrl = "wss://direct.example.test",
        };

        await _repository.SaveAsync(new AgentsState { Agents = new List<AgentConfig> { agent } });

        Assert.Equal("direct-only", Assert.Single((await _repository.LoadAsync()).Agents).Id);
    }

    [Fact]
    public async Task SaveAsync_InvalidAndReservedAgents_SkipsRowsAndStaleSelection()
    {
        await _repository.SaveAsync(new AgentsState
        {
            Agents = new List<AgentConfig>
            {
                new() { Id = "", Name = "Missing id", Address = "0x1" },
                new() { Id = "missing-name", Name = "", Address = "0x2" },
                new() { Id = "missing-target", Name = "No target" },
                new() { Id = "local-agent", Name = "Reserved", Address = "0x3" },
            },
            SelectedAgentId = "missing-target",
        });

        var loaded = await _repository.LoadAsync();
        Assert.Empty(loaded.Agents);
        Assert.Null(loaded.SelectedAgentId);
    }

    [Fact]
    public async Task SaveAsync_AgentList_PreservesExplicitOrder()
    {
        var agents = new[]
        {
            new AgentConfig { Id = "order-z", Name = "Zulu", Address = "0x1" },
            new AgentConfig { Id = "order-a", Name = "Alpha", Address = "0x2" },
        };

        await _repository.SaveAsync(new AgentsState { Agents = agents.ToList() });

        Assert.Equal(new[] { "order-z", "order-a" }, (await _repository.LoadAsync()).Agents.Select(agent => agent.Id));
    }

    [Fact]
    public async Task UpdateInfoAsync_PreservesSelectionAndOtherAgentFields()
    {
        var fetchingAgent = new AgentConfig
        {
            Id = "info-fetching",
            Name = "Original name",
            Address = "0x1",
        };
        var selectedAgent = new AgentConfig
        {
            Id = "new-selection",
            Name = "Selected agent",
            Address = "0x2",
        };
        await _repository.SaveAsync(new AgentsState
        {
            Agents = new List<AgentConfig> { fetchingAgent, selectedAgent },
            SelectedAgentId = fetchingAgent.Id,
        });

        // Model the race that caused the sidebar bug: the user changes selection while the
        // first agent's /info request is still in flight, then that delayed request persists.
        await _repository.SetSelectedAgentAsync(selectedAgent.Id);
        await _repository.UpdateInfoAsync(
            fetchingAgent.Id,
            "{\"model\":\"gpt-test\"}",
            "2026-08-03T00:00:00.0000000Z");

        var loaded = await _repository.LoadAsync();
        Assert.Equal(selectedAgent.Id, loaded.SelectedAgentId);
        var updated = Assert.Single(loaded.Agents, agent => agent.Id == fetchingAgent.Id);
        Assert.Equal(fetchingAgent.Name, updated.Name);
        Assert.Equal(fetchingAgent.Address, updated.Address);
        Assert.Equal("{\"model\":\"gpt-test\"}", updated.InfoJson);
        Assert.Equal("2026-08-03T00:00:00.0000000Z", updated.InfoUpdatedAt);
    }

    [Fact]
    public async Task SaveAsync_RemovingAgent_DeletesItsConversationGraphFirst()
    {
        var agent = new AgentConfig { Id = "agent-with-session", Name = "Agent", Address = "0x1" };
        await _repository.SaveAsync(new AgentsState { Agents = new List<AgentConfig> { agent } });

        await using (var connection = await AppDatabase.OpenAsync())
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO sessions (id, agent_id, title, created_at, updated_at, sort_order, mode)
                VALUES ('owned-session', 'agent-with-session', 'Owned', 'now', 'now', 0, 'safe');

                INSERT INTO messages (id, conversation_id, role, content, created_at)
                VALUES (1, 'owned-session', 'user', 'message', 1);

                INSERT INTO message_attachments (
                    id, conversation_id, message_id, kind, file_name, size_bytes, status, created_at)
                VALUES ('owned-attachment', 'owned-session', 1, 'file', 'test.txt', 1, 'sent', 1);

                INSERT INTO executions (id, conversation_id, prompt, status, created_at)
                VALUES ('owned-execution', 'owned-session', 'test', 'complete', 1);

                INSERT INTO trace_events (id, conversation_id, execution_id, type, payload_json)
                VALUES ('owned-trace', 'owned-session', 'owned-execution', 'tool_call', '{}');
                """;
            await seed.ExecuteNonQueryAsync();
        }

        await _repository.SaveAsync(new AgentsState());

        await using var verify = await AppDatabase.OpenAsync();
        await using var count = verify.CreateCommand();
        count.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM agents WHERE id = 'agent-with-session') +
                (SELECT COUNT(*) FROM sessions WHERE id = 'owned-session') +
                (SELECT COUNT(*) FROM messages WHERE conversation_id = 'owned-session') +
                (SELECT COUNT(*) FROM message_attachments WHERE conversation_id = 'owned-session') +
                (SELECT COUNT(*) FROM executions WHERE conversation_id = 'owned-session') +
                (SELECT COUNT(*) FROM trace_events WHERE conversation_id = 'owned-session');
            """;
        Assert.Equal(
            0,
            Convert.ToInt64(
                await count.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture));
    }
}
