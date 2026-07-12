using System.Collections.Generic;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services;

/// <summary>
/// Keeps the last few *idle* conversations in memory so re-opening one skips the database read
/// and the bubble re-projection. A conversation with an active run is never served from here —
/// the chat page must load the authoritative history the run manager persisted before replaying
/// the run's events on top of it.
///
/// Eviction is real LRU (touched on every read <i>and</i> write), and entries are dropped when
/// the conversation they belong to is deleted — a cache that never forgets a deleted session
/// would pin its whole message list, attachments and all, for the lifetime of the process.
/// </summary>
public static class ConversationCache
{
    private const int MaxEntries = 4;

    public sealed record Entry(
        AgentConfig Agent,
        SessionSummary Session,
        List<ChatMessage> Messages,
        long CreatedAt);

    private static readonly Dictionary<string, LinkedListNode<string>> Nodes = new();
    private static readonly Dictionary<string, Entry> Entries = new();

    // Most-recently-used at the front, so the tail is always the eviction victim.
    private static readonly LinkedList<string> Recency = new();

    private static readonly object Gate = new();

    /// <summary>Returns the cached conversation and marks it most-recently-used. Misses when the
    /// entry was cached for a different agent, which guards against an id collision serving a
    /// conversation into the wrong agent's page.</summary>
    public static Entry? Get(string sessionId, string agentId)
    {
        lock (Gate)
        {
            if (!Entries.TryGetValue(sessionId, out var entry)) return null;
            if (entry.Agent.Id != agentId) return null;

            Touch(sessionId);
            return entry;
        }
    }

    public static void Store(string sessionId, Entry entry)
    {
        lock (Gate)
        {
            Entries[sessionId] = entry;
            Touch(sessionId);

            while (Entries.Count > MaxEntries && Recency.Last is { } victim)
            {
                Entries.Remove(victim.Value);
                Nodes.Remove(victim.Value);
                Recency.RemoveLast();
            }
        }
    }

    /// <summary>Drops a conversation from the cache — call when its messages are deleted, so a
    /// removed session leaves no in-memory copy behind.</summary>
    public static void Invalidate(string sessionId)
    {
        lock (Gate)
        {
            if (!Entries.Remove(sessionId)) return;
            if (Nodes.Remove(sessionId, out var node)) Recency.Remove(node);
        }
    }

    private static void Touch(string sessionId)
    {
        if (Nodes.TryGetValue(sessionId, out var node))
        {
            Recency.Remove(node);
            Recency.AddFirst(node);
            return;
        }

        Nodes[sessionId] = Recency.AddFirst(sessionId);
    }
}
