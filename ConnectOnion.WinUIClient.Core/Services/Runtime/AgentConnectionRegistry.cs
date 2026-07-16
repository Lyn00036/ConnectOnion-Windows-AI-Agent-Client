using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services.Runtime;

/// <summary>
/// App-level owner of <see cref="AgentConnectionService"/> sockets, keyed by conversation.
/// A conversation binds one session id to one socket (the SDK carries a single in-flight
/// input per socket and stamps the session on CONNECT), so per-conversation is the
/// SDK-appropriate granularity and still lets different conversations of the same agent
/// run in parallel. The socket is created lazily and lives at app scope — switching pages
/// or minimizing to the tray never closes it; only removing the conversation/agent or app
/// exit does.
/// </summary>
public sealed class AgentConnectionRegistry
{
    /// <summary>How many idle (no active run) sockets to keep warm. Beyond this the least
    /// recently used idle ones are closed; they lazily rebuild on the next send. Busy
    /// conversations are never counted against this — they are always kept.</summary>
    private const int MaxIdleConnections = 5;

    private readonly Func<AgentConfig, AgentConnectionService> _connectionFactory;
    private readonly Func<long> _clock;
    private readonly ConcurrentDictionary<string, Lazy<AgentConnectionService>> _connections = new();
    // Last-touched tick per conversation, so idle eviction can drop the least recently used.
    private readonly ConcurrentDictionary<string, long> _lastUsed = new();

    public AgentConnectionRegistry(
        HttpClient http,
        Func<AgentConfig, AgentConnectionService>? connectionFactory = null,
        Func<long>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _connectionFactory = connectionFactory
            ?? (agent => new AgentConnectionService(
                agent.Address,
                agent.DirectUrl,
                IdentityStore.EnsureIdentity(),
                http));
        _clock = clock ?? (() => Environment.TickCount64);
    }

    public AgentConnectionService GetOrCreate(string conversationId, AgentConfig agent)
    {
        _lastUsed[conversationId] = _clock();
        return _connections.GetOrAdd(conversationId,
            _ => new Lazy<AgentConnectionService>(() => _connectionFactory(agent))).Value;
    }

    /// <summary>The existing socket for a conversation (for interactive-turn responses and
    /// explicit reconnect), or null if none has been created.</summary>
    public AgentConnectionService? Get(string conversationId)
    {
        if (_connections.TryGetValue(conversationId, out var lazy) && lazy.IsValueCreated)
        {
            _lastUsed[conversationId] = _clock();
            return lazy.Value;
        }
        return null;
    }

    public async Task RemoveAsync(string conversationId)
    {
        _lastUsed.TryRemove(conversationId, out _);
        if (_connections.TryRemove(conversationId, out var lazy) && lazy.IsValueCreated)
        {
            try { await lazy.Value.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        }
    }

    /// <summary>Closes idle sockets beyond <see cref="MaxIdleConnections"/>, least recently used
    /// first, so a long session that touches many conversations does not accumulate live
    /// WebSockets for the whole app lifetime. A conversation the caller reports as busy (an
    /// active run, including one paused on an interactive turn) is always spared and rebuilds
    /// lazily via <see cref="GetOrCreate"/> when next used.</summary>
    public async Task TrimIdleAsync(Func<string, bool> isBusy)
    {
        var evictable = _connections
            .Where(kv => kv.Value.IsValueCreated && !isBusy(kv.Key))
            .Select(kv => kv.Key)
            .OrderByDescending(id => _lastUsed.TryGetValue(id, out var t) ? t : 0)
            .Skip(MaxIdleConnections)
            .ToList();

        foreach (var id in evictable)
            await RemoveAsync(id).ConfigureAwait(false);
    }

    /// <summary>App exit: release every socket. Best-effort and bounded — never throws.</summary>
    public async Task DisposeAllAsync()
    {
        var connections = _connections.Values
            .Where(lazy => lazy.IsValueCreated)
            .Select(lazy => lazy.Value)
            .ToArray();
        _connections.Clear();
        _lastUsed.Clear();

        await Task.WhenAll(connections.Select(async connection =>
        {
            try { await connection.DisposeAsync().ConfigureAwait(false); }
            catch { /* best-effort */ }
        })).ConfigureAwait(false);
    }
}
