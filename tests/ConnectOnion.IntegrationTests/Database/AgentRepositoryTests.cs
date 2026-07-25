using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.IntegrationTests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class AgentRepositoryTests
{
    private readonly AgentRepository _repository = new();

    [Fact]
    public async Task SaveLoadAndDelete_Agent_RoundTripsLifecycleAndAllFields()
    {
        var agent = new AgentConfig
        {
            Id = "agent-crud",
            Name = "CRUD Agent",
            Address = "0x1234",
            DirectUrl = "https://agent.example.test",
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
