using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.ViewModels;

/// <summary>
/// Holds the full session catalog for the shell search overlay and exposes a live-filtered,
/// most-recent-first result list. The overlay only supplies data and writes
/// <see cref="SearchText"/>; matching stays headless and unit-testable.
/// </summary>
public sealed partial class SessionSearchViewModel : Common.ObservableObject
{
    private IReadOnlyList<SessionSearchItem> _allItems = [];

    public ObservableCollection<SessionSearchItem> Results { get; } = new();

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasResults { get; private set; }

    public bool IsEmpty => !HasResults;

    public SessionSearchViewModel()
    {
        SearchText = "";
    }

    /// <summary>Replaces the catalog and clears any query left by a prior opening.</summary>
    public void Reset(IEnumerable<SessionSearchItem> items)
    {
        _allItems = items
            .OrderByDescending(item => item.UpdatedAt, StringComparer.Ordinal)
            .ToList();

        if (SearchText.Length == 0)
            ApplyFilter();
        else
            SearchText = "";
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        Results.Clear();
        foreach (var item in _allItems)
        {
            if (item.Matches(query)) Results.Add(item);
        }

        HasResults = Results.Count > 0;
    }
}
