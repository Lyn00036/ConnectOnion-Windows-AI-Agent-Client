using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;

namespace ConnectOnion.WinUIClient.ViewModels;

/// <summary>
/// Backs the Agents page: CRUD over saved agents, the selected agent (persisted
/// across restarts), and a Direct URL connection test. Port of the agents
/// feature (<c>agentStorage.ts</c> + <c>AgentsPage.tsx</c>).
/// </summary>
public sealed class AgentsViewModel : ObservableObject
{
    private readonly AgentRepository _repo = AppServices.Agents;

    public ObservableCollection<AgentConfig> Agents { get; } = new();

    private AgentConfig? _selected;
    public AgentConfig? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            // Persist the selection pointer immediately so it survives restarts.
            _ = SaveAsync();
        }
    }

    public bool HasSelection => _selected is not null;

    public async Task LoadAsync()
    {
        var state = await _repo.LoadAsync();
        Agents.Clear();
        foreach (var agent in state.Agents) Agents.Add(agent);
        _selected = Agents.FirstOrDefault(a => a.Id == state.SelectedAgentId);
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelection));
    }

    public AgentConfig CreateAgent()
    {
        var agent = new AgentConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = "New agent",
            Address = "",
        };
        Agents.Add(agent);
        Selected = agent;
        _ = SaveAsync();
        return agent;
    }

    public async Task RemoveAsync(AgentConfig agent)
    {
        var wasSelected = ReferenceEquals(_selected, agent);
        Agents.Remove(agent);
        if (wasSelected) Selected = Agents.FirstOrDefault();
        await SaveAsync();
    }

    /// <summary>Persists edits made through the bound editor form.</summary>
    public Task SaveAsync()
        => _repo.SaveAsync(new AgentsState
        {
            Agents = Agents.ToList(),
            SelectedAgentId = _selected?.Id,
        });

    public Task<ConnectionTestResult> TestConnectionAsync(AgentConfig agent)
        => ConnectionTester.TestAsync(agent.Address, agent.DirectUrl);
}
