using System.Collections.Generic;
using System.Linq;

namespace ConnectOnion.WinUIClient.Services.Notifications;

/// <summary>
/// Tracks per-window focus/visibility and which conversation each window is
/// showing, so the policy can tell "is the user looking at this conversation
/// right now" across multiple windows. Implements the pure <see cref="IWindowPresence"/>
/// the policy depends on, plus concrete lookups the navigation/in-app layers use.
/// </summary>
public sealed class WindowPresenceService : IWindowPresence
{
    private sealed class State
    {
        public bool Active;
        public bool Visible = true;
        public string? AgentId;
        public string? ConversationId;
    }

    private readonly Dictionary<IChatWindow, State> _windows = new();
    private readonly object _gate = new();

    public void Register(IChatWindow window)
    {
        lock (_gate) _windows[window] = new State();
    }

    public void Unregister(IChatWindow window)
    {
        lock (_gate) _windows.Remove(window);
    }

    public void SetActive(IChatWindow window, bool active)
    {
        lock (_gate) { if (_windows.TryGetValue(window, out var s)) s.Active = active; }
    }

    public void SetVisible(IChatWindow window, bool visible)
    {
        lock (_gate) { if (_windows.TryGetValue(window, out var s)) s.Visible = visible; }
    }

    /// <summary>Reports which conversation a window is showing (null when it isn't on a chat).</summary>
    public void SetViewing(IChatWindow window, string? agentId, string? conversationId)
    {
        lock (_gate)
        {
            if (!_windows.TryGetValue(window, out var s)) return;
            s.AgentId = agentId;
            s.ConversationId = conversationId;
        }
    }

    public bool IsForeground
    {
        get { lock (_gate) return _windows.Values.Any(s => s.Active && s.Visible); }
    }

    public bool IsViewingConversation(string? conversationId)
    {
        if (string.IsNullOrEmpty(conversationId)) return false;
        lock (_gate)
            return _windows.Values.Any(s => s.Active && s.Visible && s.ConversationId == conversationId);
    }

    public bool IsViewingApproval(string? conversationId) => IsViewingConversation(conversationId);

    /// <summary>A window already showing the conversation, or null.</summary>
    public IChatWindow? FindViewing(string conversationId)
    {
        lock (_gate)
            return _windows.FirstOrDefault(kv => kv.Value.ConversationId == conversationId).Key;
    }

    /// <summary>The best window to route into: prefer an active+visible one, then any
    /// visible one, then any registered one.</summary>
    public IChatWindow? ForegroundWindow()
    {
        lock (_gate)
        {
            return _windows.FirstOrDefault(kv => kv.Value.Active && kv.Value.Visible).Key
                   ?? _windows.FirstOrDefault(kv => kv.Value.Visible).Key
                   ?? _windows.Keys.FirstOrDefault();
        }
    }
}
