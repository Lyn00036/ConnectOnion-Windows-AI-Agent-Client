using System;
using System.Collections.Generic;
using ConnectOnion.WinUIClient.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ConnectOnion.WinUIClient.Views;

// In-content find (Ctrl+F) implementation of IFindHost — driven by MainWindow's
// shared find overlay. Kept in its own partial so ChatPage.xaml.cs stays focused
// on the conversation/scroll lifecycle. The constructor wiring and the
// QueueFindRefresh()/_findDebounceTimer.Stop() calls live in ChatPage.xaml.cs.
public sealed partial class ChatPage
{
    /// <summary>Every match in the conversation, flattened across messages and in transcript
    /// order — so next/previous can walk the whole conversation as one list rather than
    /// tracking a position within a message and a position among messages.</summary>
    private readonly List<FindMatch> _findMatches = new();
    /// <summary>Debounces the search. Matching walks every message's text, so running it on
    /// each keystroke makes typing in a long conversation stutter. Stopped in the page's
    /// Unloaded handler — a live timer on a navigated-away page is the class of bug that shows
    /// up as a crash at window close (see CLAUDE.md).</summary>
    private readonly DispatcherTimer _findDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private int _currentFindIndex = -1;
    private string _searchQuery = "";
    public event EventHandler? FindStateChanged;
    public string FindStatusText { get; private set; } = "0 / 0 results";
    public bool HasFindMatches => _findMatches.Count > 0;

    public void OpenFind()
    {
        UpdateFindButtons();
    }

    public void CloseFind()
    {
        _findDebounceTimer.Stop();
        _searchQuery = "";
        ClearFindHighlights();
        FindStatusText = "0 / 0 results";
        UpdateFindButtons();
        MessageList.Focus(FocusState.Programmatic);
    }

    /// <summary>Esc closes find — but only while a search is actually active, so the key stays
    /// available to whatever else has focus the rest of the time (which is why Esc is listed as
    /// non-customizable in the shortcut catalog).</summary>
    private void ChatRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_searchQuery) && e.Key == VirtualKey.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    public void SetFindQuery(string query)
    {
        _searchQuery = query;
        QueueFindRefresh();
    }

    public void SelectPreviousFindMatch()
        => SelectPreviousMatch();

    public void SelectNextFindMatch()
        => SelectNextMatch();

    private void QueueFindRefresh()
    {
        _findDebounceTimer.Stop();
        _findDebounceTimer.Start();
    }

    private void FindDebounceTimer_Tick(object? sender, object e)
    {
        _findDebounceTimer.Stop();
        FindAllMatches();
    }

    /// <summary>Rebuilds the match list from scratch and jumps to the first hit.
    /// <para>Searches <c>message.Content</c> only, so matches inside tool arguments, event
    /// details, or attachment names are not found — a deliberate limit, since those are
    /// collapsed by default and highlighting inside a closed card would report a match the user
    /// cannot see.</para></summary>
    private void FindAllMatches()
    {
        _findMatches.Clear();
        ClearFindHighlights();

        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            _currentFindIndex = -1;
            FindStatusText = "0 / 0 results";
            UpdateFindButtons();
            return;
        }

        foreach (var message in Vm.Messages)
        {
            if (string.IsNullOrEmpty(message.Content)) continue;

            // Two counters with different jobs: `cursor` walks characters, while `localIndex`
            // counts matches *within this message* and is what the highlighter uses to pick out
            // the active occurrence. Advancing the cursor past the whole match (not by one)
            // gives non-overlapping matches, matching what a user expects from a find bar.
            var localIndex = 0;
            var cursor = 0;
            while (cursor < message.Content.Length)
            {
                var index = message.Content.IndexOf(_searchQuery, cursor, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;

                _findMatches.Add(new FindMatch(message, localIndex));
                localIndex++;
                cursor = index + _searchQuery.Length;
            }

            if (localIndex > 0)
            {
                message.FindQuery = _searchQuery;
            }
        }

        _currentFindIndex = _findMatches.Count > 0 ? 0 : -1;
        ApplyCurrentFindMatch(scroll: _findMatches.Count > 0);
    }

    private void SelectNextMatch()
    {
        if (_findMatches.Count == 0) return;

        // Wraps around the end, so find keeps cycling rather than dead-ending at the last match.
        _currentFindIndex = (_currentFindIndex + 1 + _findMatches.Count) % _findMatches.Count;
        ApplyCurrentFindMatch(scroll: true);
    }

    private void SelectPreviousMatch()
    {
        if (_findMatches.Count == 0) return;

        _currentFindIndex = (_currentFindIndex - 1 + _findMatches.Count) % _findMatches.Count;
        ApplyCurrentFindMatch(scroll: true);
    }

    /// <summary>Moves the "current match" highlight and refreshes the status text.</summary>
    /// <param name="scroll">False on the initial search so a user still typing is not yanked
    /// around the transcript; true for explicit next/previous.</param>
    private void ApplyCurrentFindMatch(bool scroll)
    {
        // Clear the active marker everywhere first. Note this leaves FindQuery intact, so all
        // matches stay dimly highlighted and only the active one is re-marked below.
        foreach (var message in Vm.Messages)
        {
            message.ActiveFindMatchIndex = -1;
        }

        if (_currentFindIndex >= 0 && _currentFindIndex < _findMatches.Count)
        {
            var match = _findMatches[_currentFindIndex];
            match.Message.FindQuery = _searchQuery;
            match.Message.ActiveFindMatchIndex = match.MatchIndexInMessage;

            if (scroll)
            {
                MessageList.ScrollIntoView(match.Message);
            }
        }

        FindStatusText = _findMatches.Count == 0
            ? "0 / 0 results"
            : $"{_currentFindIndex + 1} / {_findMatches.Count} results";
        UpdateFindButtons();
    }

    private void ClearFindHighlights()
    {
        foreach (var message in Vm.Messages)
        {
            message.FindQuery = "";
            message.ActiveFindMatchIndex = -1;
        }
    }

    /// <summary>Tells MainWindow's shared find overlay to re-read <see cref="FindStatusText"/>
    /// and <see cref="HasFindMatches"/>. The page owns the matching; the window owns the UI, so
    /// this event is the only channel between them (see <see cref="IFindHost"/>).</summary>
    private void UpdateFindButtons()
    {
        FindStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A single hit: which bubble, and which occurrence within that bubble. Holds the
    /// message by reference rather than by index, so the list survives new messages arriving
    /// mid-search — a live turn can append while the find bar is open.</summary>
    private readonly record struct FindMatch(ChatMessage Message, int MatchIndexInMessage);
}
