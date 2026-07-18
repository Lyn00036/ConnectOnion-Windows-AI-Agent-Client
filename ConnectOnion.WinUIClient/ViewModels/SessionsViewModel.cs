using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Data;

namespace ConnectOnion.WinUIClient.ViewModels;

/// <summary>
/// Backs the Sessions page and provides the session index used by chat. Port of
/// the sessions store (<c>store.ts</c> + <c>sessionStorage.ts</c>): create,
/// select, touch (first-prompt title), delete, and delete-by-agent.
/// </summary>
public sealed class SessionsViewModel : ObservableObject
{
    /// <summary>Matches the placeholder title <see cref="SessionSummary.NewConversation"/>
    /// assigns. Only a title still in this shape gets replaced by the opening prompt — a user
    /// who renamed a conversation must never have that overwritten.</summary>
    private static readonly Regex GenericTitle = new(@"^Conversation \d+$", RegexOptions.Compiled);
    /// <summary>Collapses runs of whitespace so a multi-line prompt becomes a single-line title.</summary>
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly SessionRepository _repo;
    private readonly ConversationRepository _conversations;

    public SessionsViewModel(SessionRepository sessions, ConversationRepository conversations)
    {
        _repo = sessions;
        _conversations = conversations;
    }

    public ObservableCollection<SessionSummary> Sessions { get; } = new();

    /// <summary>The persisted selection pointer, kept separately from <see cref="_selected"/>
    /// because the two legitimately diverge: the load path sets the field directly to avoid
    /// re-saving what it just read, and clearing a deleted selection must null both.</summary>
    private string? _selectedSessionId;

    private SessionSummary? _selected;
    /// <summary>The active conversation. Hand-written rather than <c>[ObservableProperty]</c>
    /// because the setter has to persist — and has to skip persisting for assignments that did
    /// not come from the user, which is why <see cref="LoadAsync"/> bypasses it entirely.</summary>
    public SessionSummary? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            // Only persist a real, known selection pointer. A session not in the list is either
            // stale or mid-removal, and writing its id would leave the app pointing at a
            // conversation that no longer exists on the next launch.
            if (value is not null && Sessions.Contains(value))
            {
                _selectedSessionId = value.Id;
                _ = SaveAsync();
            }
        }
    }

    public async Task LoadAsync()
    {
        var state = await _repo.LoadAsync();
        Sessions.Clear();
        // Most-recent first, matching the web app's sort on updatedAt.
        foreach (var session in state.Sessions.OrderByDescending(s => s.UpdatedAt, StringComparer.Ordinal))
        {
            Sessions.Add(session);
        }
        // Assigns the *field*, deliberately bypassing the Selected setter: going through the
        // property would persist the state we just finished reading. The change notification is
        // then raised by hand so bindings still update.
        _selectedSessionId = state.ActiveSessionId;
        _selected = Sessions.FirstOrDefault(s => s.Id == _selectedSessionId);
        OnPropertyChanged(nameof(Selected));
    }

    /// <summary>Creates a new conversation for the given agent and selects it.</summary>
    public SessionSummary StartNewSession(string agentId)
    {
        var session = SessionSummary.NewConversation(agentId, Sessions);
        Sessions.Insert(0, session);
        Selected = session;
        _ = SaveAsync();
        return session;
    }

    /// <summary>
    /// Bumps updatedAt and, the first time only, replaces the generic
    /// "Conversation N" title with the opening prompt.
    /// </summary>
    public void TouchSession(string sessionId, string? prompt = null)
    {
        var session = Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null) return;

        if (GenericTitle.IsMatch(session.Title) && !string.IsNullOrWhiteSpace(prompt))
        {
            var title = Whitespace.Replace(prompt.Trim(), " ");
            session.Title = title.Length > 48 ? title[..48] : title;
        }
        // ISO-8601 round-trip ("o") and UTC, because the sidebar sorts these as *strings* —
        // lexicographic order is only chronological if every row uses this exact format.
        session.UpdatedAt = DateTime.UtcNow.ToString("o");
        _ = SaveAsync();
    }

    /// <summary>Deletes a conversation and everything hanging off it.
    /// <para><b>The statement order is load-bearing.</b> The foreign keys declare no
    /// <c>ON DELETE</c>, so the children have to go first; the final <c>SaveAsync</c> is what
    /// removes the <c>sessions</c> row, and reordering these lines raises a constraint
    /// violation that the repositories swallow into a log line. Note <c>usage_events</c> is
    /// deliberately <i>not</i> touched — the tokens were still spent (see CLAUDE.md).</para></summary>
    public async Task RemoveSessionAsync(SessionSummary session)
    {
        // UI first so the row disappears immediately; cache next so nothing can re-serve the
        // conversation from memory while the deletes are in flight.
        Sessions.Remove(session);
        ConversationCache.Invalidate(session.Id);
        await _conversations.DeleteMessagesAsync(session.Id);
        await _conversations.DeleteExecutionsAndTracesAsync(session.Id);
        if (ReferenceEquals(_selected, session))
        {
            _selected = null;
            _selectedSessionId = null;
            OnPropertyChanged(nameof(Selected));
        }
        await SaveAsync();
    }

    /// <summary>Removes every session for an agent (used when an agent is deleted).</summary>
    public async Task RemoveSessionsForAgentAsync(string agentId)
    {
        // Materialized with ToList before the loop mutates Sessions — enumerating the
        // collection while removing from it would throw.
        var removed = Sessions.Where(s => s.AgentId == agentId).ToList();
        foreach (var session in removed)
        {
            Sessions.Remove(session);
            ConversationCache.Invalidate(session.Id);
            await _conversations.DeleteMessagesAsync(session.Id);
            await _conversations.DeleteExecutionsAndTracesAsync(session.Id);
        }

        if (_selected is not null && removed.Contains(_selected))
        {
            _selected = null;
            _selectedSessionId = null;
            OnPropertyChanged(nameof(Selected));
        }
        await SaveAsync();
    }

    /// <summary>Persists the whole session index plus the selection pointer. Called after every
    /// mutation, so the in-memory list is always the authority and the stored state is a
    /// straight snapshot of it — there is no partial-update path to get out of sync.</summary>
    public Task SaveAsync()
        => _repo.SaveAsync(new SessionsState
        {
            Sessions = Sessions.ToList(),
            ActiveSessionId = _selectedSessionId,
        });
}
