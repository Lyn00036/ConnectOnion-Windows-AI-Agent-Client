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

    /// <param name="Messages">The conversation's loaded window — not necessarily its whole
    /// history, since the chat page opens conversations a page at a time.</param>
    /// <param name="HasMoreHistory">Whether older bubbles exist above <paramref name="Messages"/>.
    /// Cached with the window because it is a property of the window, not of the conversation:
    /// serving a partial list back with this recomputed as false would strand the reader at the
    /// top of it with no way to pull the rest. Defaults to false so a caller holding a complete
    /// history needs no extra argument.</param>
    public sealed record Entry(
        AgentConfig Agent,
        SessionSummary Session,
        List<ChatMessage> Messages,
        long CreatedAt,
        bool HasMoreHistory = false);

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

    /// <summary>Caches a conversation, evicting down to <see cref="MaxEntries"/>. Overwrites an
    /// existing entry rather than merging — the caller always holds the full, freshly loaded
    /// message list, so a partial update is never what is wanted here.</summary>
    public static void Store(string sessionId, Entry entry)
    {
        lock (Gate)
        {
            Entries[sessionId] = entry;
            Touch(sessionId);

            // A loop, not a single removal: MaxEntries could be lowered, and evicting to the
            // limit is the invariant worth stating rather than "remove one per insert".
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

    /// <summary>Moves a session to the front of the recency list. <c>Nodes</c> exists purely to
    /// make this O(1) — without the node handle, promoting an entry would mean an O(n) scan of
    /// the list on every cache read.</summary>
    private static void Touch(string sessionId)
    {
        if (Nodes.TryGetValue(sessionId, out var node))
        {
            // The same node instance is re-inserted, which is what keeps the handle in Nodes
            // valid. Re-adding by value instead would leave Nodes pointing at a detached node
            // and quietly break every later Remove.
            Recency.Remove(node);
            Recency.AddFirst(node);
            return;
        }

        Nodes[sessionId] = Recency.AddFirst(sessionId);
    }
}
