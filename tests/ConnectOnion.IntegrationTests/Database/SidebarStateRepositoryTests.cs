using ConnectOnion.WinUIClient.Data;

namespace ConnectOnion.IntegrationTests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class SidebarStateRepositoryTests
{
    private readonly SidebarStateRepository _repository = new();

    [Fact]
    public async Task LoadAsync_NoStoredState_ReturnsFirstRunDefaults()
    {
        await DeleteStateAsync();

        var state = await _repository.LoadAsync();

        Assert.False(state.HasAgentExpansionState);
        Assert.Empty(state.ExpandedAgentIds);
        Assert.True(state.IsPinnedExpanded);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsExpandedAgentsAndPinnedDisclosure()
    {
        await _repository.SaveAsync(["agent-b", "agent-a", "agent-a"], isPinnedExpanded: false);

        var state = await _repository.LoadAsync();

        Assert.True(state.HasAgentExpansionState);
        Assert.Equal(["agent-a", "agent-b"], state.ExpandedAgentIds.OrderBy(id => id));
        Assert.False(state.IsPinnedExpanded);
    }

    [Fact]
    public async Task SaveAsync_EmptyList_PreservesExplicitAllCollapsedState()
    {
        await _repository.SaveAsync([], isPinnedExpanded: true);

        var state = await _repository.LoadAsync();

        Assert.True(state.HasAgentExpansionState);
        Assert.Empty(state.ExpandedAgentIds);
        Assert.True(state.IsPinnedExpanded);
    }

    private static async Task DeleteStateAsync()
    {
        await using var connection = await AppDatabase.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM app_meta
            WHERE key IN ('sidebar_expanded_agent_ids', 'sidebar_pinned_expanded');
            """;
        await command.ExecuteNonQueryAsync();
    }
}
