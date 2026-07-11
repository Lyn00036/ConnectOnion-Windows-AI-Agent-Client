using System;
using System.Collections.Concurrent;
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
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, Lazy<AgentConnectionService>> _connections = new();

    public AgentConnectionRegistry(HttpClient http) => _http = http;

    public AgentConnectionService GetOrCreate(string conversationId, AgentConfig agent)
        => _connections.GetOrAdd(conversationId,
            _ => new Lazy<AgentConnectionService>(() => Create(agent))).Value;

    /// <summary>The existing socket for a conversation (for interactive-turn responses and
    /// explicit reconnect), or null if none has been created.</summary>
    public AgentConnectionService? Get(string conversationId)
        => _connections.TryGetValue(conversationId, out var lazy) && lazy.IsValueCreated ? lazy.Value : null;

    public async Task RemoveAsync(string conversationId)
    {
        if (_connections.TryRemove(conversationId, out var lazy) && lazy.IsValueCreated)
        {
            try { await lazy.Value.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        }
    }

    /// <summary>App exit: release every socket. Best-effort and bounded — never throws.</summary>
    public async Task DisposeAllAsync()
    {
        foreach (var lazy in _connections.Values)
        {
            if (!lazy.IsValueCreated) continue;
            try { await lazy.Value.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        }
        _connections.Clear();
    }

    private AgentConnectionService Create(AgentConfig agent)
        => new(agent.Address, agent.DirectUrl, IdentityStore.EnsureIdentity(), _http);
}
