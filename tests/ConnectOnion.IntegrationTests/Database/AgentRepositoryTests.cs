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
}
