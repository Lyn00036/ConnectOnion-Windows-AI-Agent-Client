using System;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ConnectOnion.WinUIClient.Controls;

public sealed partial class SettingsOverlay : UserControl
{
    private FrameworkElement? _focusReturnTarget;
    private string _selectedCategory = "General";
    private bool _syncingSearch;
    public event EventHandler? CloseRequested;

    public SettingsOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateModalSize(e.NewSize.Width, e.NewSize.Height);
    }

    /// <summary>Whether the settings modal is currently shown.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Exposes this overlay to UI Automation as a dialog. Without a peer the control is
    /// invisible to UIA entirely — no dialog boundary for a screen reader, and its
    /// AutomationId unreachable from a UI test. See <see cref="ModalOverlayAutomationPeer"/>.</summary>
    protected override Microsoft.UI.Xaml.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
        => new ModalOverlayAutomationPeer(this);


    public void Show(FrameworkElement? focusReturnTarget)
    {
        _focusReturnTarget = focusReturnTarget;
        GeneralNav.IsChecked = true;
        SearchBox.Text = string.Empty;
        CompactSearchBox.Text = string.Empty;
        CompactCategoryPicker.SelectedIndex = 0;

        // Go through SelectCategory rather than setting the title by hand: it is what resets the
        // pane visibility too. Setting only the title left the previously shown standalone pane
        // (Identity, now also Usage) on screen under a "General" heading.
        SelectCategory("General");

        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
        UpdateModalSize(ActualWidth, ActualHeight);
        DispatcherQueue.TryEnqueue(() =>
        {
            var focusTarget = ActualWidth < 640
                ? (Control)CompactCategoryPicker
                : SearchBox;
            focusTarget.Focus(FocusState.Programmatic);
        });
    }

    public void Hide()
    {
        if (Visibility != Visibility.Visible) return;
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        _focusReturnTarget?.Focus(FocusState.Programmatic);
        _focusReturnTarget = null;
    }

    /// <summary>Disarms child controls whose delayed callbacks must not outlive the window.</summary>
    public void Shutdown() => IdentityContent.Shutdown();

    private void UpdateModalSize(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        var safeMargin = width >= 860 && height >= 640 ? 64d : width >= 640 ? 48d : 16d;
        ModalContainer.Width = Math.Max(0, Math.Min(1100, width - safeMargin));
        ModalContainer.Height = Math.Max(0, Math.Min(760, height - safeMargin));
    }

    private void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void CloseButton_Click(object sender, RoutedEventArgs e) => RequestClose();
    private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e) => RequestClose();
    private void ModalContainer_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;
    private void OverlayRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        RequestClose();
    }
    private void GeneralNav_Click(object sender, RoutedEventArgs e) => SelectCategory("General");
    private void AgentsNav_Click(object sender, RoutedEventArgs e) => SelectCategory("Agents");
    private void NotificationsNav_Click(object sender, RoutedEventArgs e) => SelectCategory("Notifications");
    private void IdentityNav_Click(object sender, RoutedEventArgs e) => SelectCategory("Identity");
    private void UsageNav_Click(object sender, RoutedEventArgs e) => SelectCategory("Usage");
    private void KeyboardNav_Click(object sender, RoutedEventArgs e) => SelectCategory("Keyboard");

    /// <summary>
    /// Swaps the right-hand pane. Identity, Usage and Keyboard are standalone controls; every other
    /// category is a section of <see cref="SettingsContent"/>, which stays hidden while one of them
    /// is shown.
    /// </summary>
    private void SelectCategory(string category)
    {
        if (!string.IsNullOrWhiteSpace(SearchBox.Text)
            || !string.IsNullOrWhiteSpace(CompactSearchBox.Text))
        {
            ClearSearch();
        }
        _selectedCategory = category;
        CategoryTitle.Text = LocalizedCategoryName(category);

        var isIdentity = category == "Identity";
        var isAgents = category == "Agents";
        var isUsage = category == "Usage";
        var isKeyboard = category == "Keyboard";
        var isSettingsPage = !isIdentity && !isAgents && !isUsage && !isKeyboard;

        SettingsContent.Visibility = isSettingsPage ? Visibility.Visible : Visibility.Collapsed;
        IdentityScrollViewer.Visibility = isIdentity ? Visibility.Visible : Visibility.Collapsed;
        AgentsScrollViewer.Visibility = isAgents ? Visibility.Visible : Visibility.Collapsed;
        UsageScrollViewer.Visibility = isUsage ? Visibility.Visible : Visibility.Collapsed;
        KeyboardScrollViewer.Visibility = isKeyboard ? Visibility.Visible : Visibility.Collapsed;

        if (isSettingsPage)
            SettingsContent.SelectCategory(category);

        // Re-read the ledger on entry: a turn may well have finished while Settings sat open.
        if (isUsage)
            UsageContent.Refresh();

        if (isAgents)
            AgentsContent.Refresh();

        // Bindings can have moved since this pane was last shown — the dialog cannot change them,
        // but a second window's Settings can.
        if (isKeyboard)
            KeyboardContent.Refresh();
    }

    public Task RefreshAgentsAsync() => AgentsContent.RefreshAsync();
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => ApplySettingsSearch(SearchBox.Text, CompactSearchBox);
    private void CompactSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => ApplySettingsSearch(CompactSearchBox.Text, SearchBox);

    private void ApplySettingsSearch(string query, TextBox peer)
    {
        if (_syncingSearch) return;

        _syncingSearch = true;
        peer.Text = query;
        _syncingSearch = false;

        if (string.IsNullOrWhiteSpace(query))
        {
            SelectCategory(_selectedCategory);
            return;
        }

        // Search is a modal-level affordance, so it must work from every category. Standalone
        // panes used to hide the search boxes; merely keeping them visible would still leave the
        // filtered SettingsPage collapsed behind Identity/Agents/Usage/Keyboard. Enter an
        // explicit results mode and let clearing the query restore the selected category.
        SettingsContent.Visibility = Visibility.Visible;
        IdentityScrollViewer.Visibility = Visibility.Collapsed;
        AgentsScrollViewer.Visibility = Visibility.Collapsed;
        UsageScrollViewer.Visibility = Visibility.Collapsed;
        KeyboardScrollViewer.Visibility = Visibility.Collapsed;
        SettingsContent.Filter(query);
        CategoryTitle.Text = LocalizedStrings.Get(
            "SettingsSearchResultsHeading",
            "Search results");
    }

    private void ClearSearch()
    {
        _syncingSearch = true;
        SearchBox.Text = string.Empty;
        CompactSearchBox.Text = string.Empty;
        _syncingSearch = false;
        SettingsContent.Filter(string.Empty);
    }

    private void CompactCategoryPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CompactCategoryPicker.SelectedItem is ComboBoxItem { Tag: string category })
            SelectCategory(category);
    }

    private static string LocalizedCategoryName(string category) => category switch
    {
        "Agents" => LocalizedStrings.Get("SettingsCategoryAgentsName", "Agents"),
        "Notifications" => LocalizedStrings.Get("SettingsCategoryNotificationsName", "Notifications"),
        "Keyboard" => LocalizedStrings.Get("SettingsCategoryKeyboardName", "Keyboard"),
        "Identity" => LocalizedStrings.Get("SettingsCategoryIdentityName", "Identity"),
        "Usage" => LocalizedStrings.Get("SettingsCategoryUsageName", "Usage"),
        _ => LocalizedStrings.Get("SettingsCategoryGeneralName", "General"),
    };
}
