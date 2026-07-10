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
    private static readonly Regex GenericTitle = new(@"^Conversation \d+$", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly SessionRepository _repo = AppServices.Sessions;

    public ObservableCollection<SessionSummary> Sessions { get; } = new();

    private string? _selectedSessionId;

    private SessionSummary? _selected;
    public SessionSummary? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            // Only persist a real, known selection pointer.
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
        session.UpdatedAt = DateTime.UtcNow.ToString("o");
        _ = SaveAsync();
    }

    public async Task RemoveSessionAsync(SessionSummary session)
    {
        Sessions.Remove(session);
        await AppServices.Conversations.DeleteMessagesAsync(session.Id);
        await AppServices.Conversations.DeleteExecutionsAndTracesAsync(session.Id);
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
        var removed = Sessions.Where(s => s.AgentId == agentId).ToList();
        foreach (var session in removed)
        {
            Sessions.Remove(session);
            await AppServices.Conversations.DeleteMessagesAsync(session.Id);
            await AppServices.Conversations.DeleteExecutionsAndTracesAsync(session.Id);
        }

        if (_selected is not null && removed.Contains(_selected))
        {
            _selected = null;
            _selectedSessionId = null;
            OnPropertyChanged(nameof(Selected));
        }
        await SaveAsync();
    }

    public Task SaveAsync()
        => _repo.SaveAsync(new SessionsState
        {
            Sessions = Sessions.ToList(),
            ActiveSessionId = _selectedSessionId,
        });
}
