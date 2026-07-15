using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;

namespace ConnectOnion.WinUIClient.ViewModels;

/// <summary>
/// Backs the Keyboard shortcuts dialog: holds the full catalog and exposes a live, search-
/// filtered view of it. The UI only reads <see cref="Groups"/> / <see cref="IsEmpty"/> and
/// writes <see cref="SearchText"/> — all filtering lives here, not in code-behind.
/// </summary>
public sealed partial class KeyboardShortcutsViewModel : Common.ObservableObject
{
    private readonly IReadOnlyList<KeyboardShortcutGroup> _allGroups = KeyboardShortcutCatalog.GetGroups();

    /// <summary>The groups (with their matching items) to render for the current search.</summary>
    public ObservableCollection<KeyboardShortcutGroup> Groups { get; } = new();

    [ObservableProperty]
    public partial string SearchText { get; set; }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasResults { get; private set; }

    /// <summary>True when the current search matched nothing — drives the empty state.</summary>
    public bool IsEmpty => !HasResults;

    // Setting SearchText fires OnSearchTextChanged → ApplyFilter, which seeds Groups and HasResults.
    public KeyboardShortcutsViewModel() => SearchText = "";

    /// <summary>Clears the search back to the full list. Called each time the dialog opens so a
    /// prior session's query never leaks into a fresh view.</summary>
    public void Reset() => SearchText = "";

    private void ApplyFilter()
    {
        var query = SearchText.Trim();

        Groups.Clear();
        foreach (var group in _allGroups)
        {
            // A group-title match reveals the whole group; otherwise only its matching items.
            IReadOnlyList<KeyboardShortcutItem> matches;
            if (query.Length == 0 || group.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                matches = group.Shortcuts;
            else
                matches = group.Shortcuts.Where(s => s.Matches(query)).ToList();

            if (matches.Count > 0)
                Groups.Add(new KeyboardShortcutGroup(group.Title, matches));
        }

        HasResults = Groups.Count > 0;
    }
}
