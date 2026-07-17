using System;
using System.Collections.Generic;
using ConnectOnion.WinUIClient.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ConnectOnion.WinUIClient.Views;

// In-content find (Ctrl+F) implementation of IFindHost — driven by MainWindow's
// shared find overlay. Kept in its own partial so ChatPage.xaml.cs stays focused
// on the conversation/scroll lifecycle. The constructor wiring and the
// QueueFindRefresh()/_findDebounceTimer.Stop() calls live in ChatPage.xaml.cs.
public sealed partial class ChatPage
{
    private readonly List<FindMatch> _findMatches = new();
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

        _currentFindIndex = (_currentFindIndex + 1 + _findMatches.Count) % _findMatches.Count;
        ApplyCurrentFindMatch(scroll: true);
    }

    private void SelectPreviousMatch()
    {
        if (_findMatches.Count == 0) return;

        _currentFindIndex = (_currentFindIndex - 1 + _findMatches.Count) % _findMatches.Count;
        ApplyCurrentFindMatch(scroll: true);
    }

    private void ApplyCurrentFindMatch(bool scroll)
    {
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

    private void UpdateFindButtons()
    {
        FindStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private readonly record struct FindMatch(ChatMessage Message, int MatchIndexInMessage);
}
