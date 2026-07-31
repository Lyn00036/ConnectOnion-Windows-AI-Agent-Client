using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.ViewModels;

namespace ConnectOnion.WinUIClient.UnitTests.ViewModels;

public sealed class SessionSearchViewModelTests
{
    [Fact]
    public void Reset_NoQuery_ShowsMostRecentlyUpdatedFirst()
    {
        var viewModel = new SessionSearchViewModel();

        viewModel.Reset(
        [
            Item("older", "Earlier chat", "remote-admin", "2026-01-01T00:00:00Z"),
            Item("newer", "Latest chat", "my_blog", "2026-02-01T00:00:00Z"),
        ]);

        Assert.Equal(["newer", "older"], viewModel.Results.Select(item => item.SessionId));
        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public void SearchText_TitleMatch_IsCaseInsensitive()
    {
        var viewModel = ViewModelWithCatalog();

        viewModel.SearchText = "CLAUDE";

        Assert.Equal("Clarify Claude request", Assert.Single(viewModel.Results).Title);
    }

    [Fact]
    public void SearchText_AgentDisplayName_MatchesFriendlyForm()
    {
        var viewModel = ViewModelWithCatalog();

        viewModel.SearchText = "remote admin";

        Assert.Equal("Clarify Claude request", Assert.Single(viewModel.Results).Title);
    }

    [Fact]
    public void SearchText_MultipleTerms_MustAllMatchTheSameResult()
    {
        var viewModel = ViewModelWithCatalog();

        viewModel.SearchText = "claude remote";

        Assert.Single(viewModel.Results);
        Assert.True(viewModel.HasResults);

        viewModel.SearchText = "claude blog";
        Assert.Empty(viewModel.Results);
        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public void Reset_AfterSearch_ClearsQueryAndRestoresNewCatalog()
    {
        var viewModel = ViewModelWithCatalog();
        viewModel.SearchText = "claude";

        viewModel.Reset([Item("fresh", "Fresh chat", "new_agent", "2026-03-01T00:00:00Z")]);

        Assert.Equal("", viewModel.SearchText);
        Assert.Equal("fresh", Assert.Single(viewModel.Results).SessionId);
    }

    private static SessionSearchViewModel ViewModelWithCatalog()
    {
        var viewModel = new SessionSearchViewModel();
        viewModel.Reset(
        [
            Item("one", "Clarify Claude request", "remote-admin", "2026-02-01T00:00:00Z"),
            Item("two", "Update homepage", "my_blog", "2026-01-01T00:00:00Z"),
        ]);
        return viewModel;
    }

    private static SessionSearchItem Item(
        string id,
        string title,
        string agent,
        string updatedAt)
        => new()
        {
            SessionId = id,
            AgentId = agent,
            Title = title,
            AgentName = agent,
            UpdatedAt = updatedAt,
        };
}
