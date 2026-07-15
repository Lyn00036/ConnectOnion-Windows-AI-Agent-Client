using System;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// The Usage panel in Settings: per-model token totals for a chosen window, read from the usage
/// ledger. Loads on first show and on every range change; nothing is cached, because the ledger
/// changes underneath it whenever a turn finishes.
/// </summary>
public sealed partial class UsageSettingsContent : UserControl
{
    public UsageViewModel Vm { get; } = App.GetService<UsageViewModel>();

    public UsageSettingsContent()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Vm.LoadAsync();
    }

    /// <summary>Re-reads the ledger. Called by <see cref="SettingsOverlay"/> when the user navigates
    /// to this section, so the panel reflects turns that finished while Settings sat open.</summary>
    public async void Refresh()
    {
        try { await Vm.LoadAsync(); }
        catch { /* the panel is informational; a failed refresh must not take Settings down */ }
    }

    private void Range_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag) return;
        if (!Enum.TryParse<UsageRange>(tag, out var range)) return;

        Vm.Range = range;   // the setter kicks off the reload
    }

    /// <summary>
    /// The only path in the app that deletes usage. Confirmed first: the ledger is deliberately not
    /// erased by deleting conversations, so this button is the single place a user can lose it, and
    /// it cannot be undone.
    /// </summary>
    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Clear usage history?",
            Content = "This permanently deletes all recorded token usage on this device. "
                    + "Your conversations are not affected.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await Vm.ClearAllAsync();
    }
}
