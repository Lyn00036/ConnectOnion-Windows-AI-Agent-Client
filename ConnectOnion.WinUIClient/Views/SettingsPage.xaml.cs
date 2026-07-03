using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _vm = new();

    // Suppresses the SelectionChanged handlers while we seed the initial values,
    // so restoring saved prefs doesn't write them straight back.
    private bool _initializing;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _vm.LoadAsync();

        _initializing = true;
        ThemeChoice.SelectedIndex = _vm.Theme == ThemeMode.Dark ? 1 : 0;
        FontChoice.SelectedIndex = _vm.MessageFontSize switch
        {
            MessageFontSize.Sm => 0,
            MessageFontSize.Lg => 2,
            _ => 1,
        };
        _initializing = false;
    }

    private async void ThemeChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        var theme = ThemeChoice.SelectedIndex == 1 ? ThemeMode.Dark : ThemeMode.Light;
        await _vm.SetThemeAsync(theme);
    }

    private async void FontChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        var size = FontChoice.SelectedIndex switch
        {
            0 => MessageFontSize.Sm,
            2 => MessageFontSize.Lg,
            _ => MessageFontSize.Md,
        };
        await _vm.SetMessageFontSizeAsync(size);
    }
}
