using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services;

/// <summary>Online status the UI shows for an agent.</summary>
public enum AgentPresence
{
    /// <summary>Not probed yet this session.</summary>
    Unknown,
    /// <summary>A reachability probe is in flight.</summary>
    Checking,
    /// <summary>The agent answered the probe.</summary>
    Online,
    /// <summary>The probe failed or the agent is registered but offline.</summary>
    Offline,
}

/// <summary>
/// Process-wide cache of agent online status. Confirming presence is a lightweight
/// reachability probe (<see cref="ConnectionTester"/>): a <c>/health</c> hit for
/// Direct URL agents, a relay lookup for address agents. Results are cached per
/// agent for the app session, so navigating back to a page shows the green dot
/// without re-probing; a page can force a fresh probe via <see cref="RefreshAsync"/>.
///
/// <see cref="PresenceChanged"/> may fire off the UI thread (the probe completes on
/// a thread-pool continuation) — subscribers must marshal onto the UI thread before
/// touching bound state.
/// </summary>
public sealed class AgentPresenceService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentPresence> _presence = new();
    private readonly Dictionary<string, string> _details = new();
    private readonly Dictionary<string, Task> _inflight = new();

    /// <summary>Raised (possibly off the UI thread) when an agent's presence changes. Carries the agent id.</summary>
    public event Action<string>? PresenceChanged;

    public AgentPresence GetPresence(string agentId)
    {
        lock (_gate) return _presence.TryGetValue(agentId, out var p) ? p : AgentPresence.Unknown;
    }

    public string GetDetail(string agentId)
    {
        lock (_gate) return _details.TryGetValue(agentId, out var d) ? d : "";
    }

    /// <summary>
    /// Confirms the agent is online unless we already know it is. A no-op when the
    /// agent is already <see cref="AgentPresence.Online"/>; otherwise probes once,
    /// coalescing concurrent callers onto a single probe.
    /// </summary>
    public Task EnsureCheckedAsync(AgentConfig agent)
        => GetPresence(agent.Id) == AgentPresence.Online
            ? Task.CompletedTask
            : RefreshAsync(agent);

    /// <summary>Forces a fresh reachability probe, reusing any probe already running for this agent.</summary>
    public Task RefreshAsync(AgentConfig agent)
    {
        lock (_gate)
        {
            if (_inflight.TryGetValue(agent.Id, out var running)) return running;
            var task = ProbeAsync(agent);
            _inflight[agent.Id] = task;
            return task;
        }
    }

    private async Task ProbeAsync(AgentConfig agent)
    {
        // Yield before touching shared state so RefreshAsync can release _gate and
        // register this task before Set() re-enters the lock / fires the event.
        await Task.Yield();

        Set(agent.Id, AgentPresence.Checking, "Checking if the agent is online…");
        try
        {
            // A lean reachability probe: no existingAgent, so we skip the extra
            // /info diff — the detail page fetches /info on its own.
            var result = await ConnectionTester.TestAsync(agent.Address, agent.DirectUrl);
            Set(agent.Id, result.Ok ? AgentPresence.Online : AgentPresence.Offline, result.Detail);
        }
        catch (Exception ex)
        {
            Set(agent.Id, AgentPresence.Offline, ex.Message);
        }
        finally
        {
            lock (_gate) _inflight.Remove(agent.Id);
        }
    }

    private void Set(string agentId, AgentPresence presence, string detail)
    {
        lock (_gate)
        {
            _presence[agentId] = presence;
            _details[agentId] = detail;
        }
        PresenceChanged?.Invoke(agentId);
    }
}
