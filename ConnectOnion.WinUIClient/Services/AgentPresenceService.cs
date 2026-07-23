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
    private static readonly TimeSpan OnlineTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OfflineTtl = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly Dictionary<string, AgentPresence> _presence = new();
    private readonly Dictionary<string, string> _details = new();
    private readonly Dictionary<string, DateTimeOffset> _checkedAt = new();
    private readonly Dictionary<string, Task> _inflight = new();
    private readonly ConnectionTester _connectionTester;

    public AgentPresenceService(ConnectionTester connectionTester)
        => _connectionTester = connectionTester;

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

    /// <summary>Drops process-wide status for a deleted agent. If a probe is already in flight,
    /// remove its result once it completes as well so the late continuation cannot resurrect the
    /// deleted key.</summary>
    public void Forget(string agentId)
    {
        Task? pending;
        lock (_gate)
        {
            _presence.Remove(agentId);
            _details.Remove(agentId);
            _checkedAt.Remove(agentId);
            _inflight.TryGetValue(agentId, out pending);
        }

        if (pending is not null)
            _ = pending.ContinueWith(
                _ => Forget(agentId),
                System.Threading.CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    /// <summary>
    /// Confirms the agent is online if the cached answer is missing or stale.
    /// Recent Online and Offline answers are reused, and concurrent callers are
    /// coalesced onto a single probe.
    /// </summary>
    public Task EnsureCheckedAsync(AgentConfig agent)
    {
        lock (_gate)
        {
            if (_inflight.TryGetValue(agent.Id, out var running)) return running;
            if (!ShouldProbeLocked(agent.Id, DateTimeOffset.UtcNow)) return Task.CompletedTask;
            return StartProbeLocked(agent);
        }
    }

    /// <summary>Forces a fresh reachability probe, reusing any probe already running for this agent.</summary>
    public Task RefreshAsync(AgentConfig agent)
    {
        lock (_gate)
        {
            if (_inflight.TryGetValue(agent.Id, out var running)) return running;
            // A user-initiated refresh bypasses the EndpointResolver relay-lookup
            // cache (which holds an "offline" answer for ~30s) — otherwise clicking
            // Recheck right after the agent comes back up keeps reporting offline.
            return StartProbeLocked(agent, forceRefresh: true);
        }
    }

    private Task StartProbeLocked(AgentConfig agent, bool forceRefresh = false)
    {
        var task = ProbeAsync(agent, forceRefresh);
        _inflight[agent.Id] = task;
        return task;
    }

    private bool ShouldProbeLocked(string agentId, DateTimeOffset now)
    {
        if (!_presence.TryGetValue(agentId, out var presence)) return true;
        if (!_checkedAt.TryGetValue(agentId, out var checkedAt)) return true;

        var ttl = presence switch
        {
            AgentPresence.Online => OnlineTtl,
            AgentPresence.Offline => OfflineTtl,
            AgentPresence.Checking => TimeSpan.FromSeconds(10),
            _ => TimeSpan.Zero,
        };
        return ttl == TimeSpan.Zero || now - checkedAt >= ttl;
    }

    private async Task ProbeAsync(AgentConfig agent, bool forceRefresh)
    {
        // Yield before touching shared state so RefreshAsync can release _gate and
        // register this task before Set() re-enters the lock / fires the event.
        await Task.Yield();

        Set(agent.Id, AgentPresence.Checking, "Checking if the agent is online…");
        try
        {
            // A lean /health reachability probe. The detail page fetches and persists /info
            // independently, so list/status refreshes never download metadata.
            var result = await _connectionTester.TestAsync(
                agent.Address, agent.DirectUrl, forceRefresh: forceRefresh);
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
            _checkedAt[agentId] = DateTimeOffset.UtcNow;
        }
        PresenceChanged?.Invoke(agentId);
    }
}
