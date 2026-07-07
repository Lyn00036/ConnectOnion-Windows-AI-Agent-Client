using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _vm = new();
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
        ThemeChoice.SelectedIndex = _vm.Theme switch
        {
            ThemeMode.Light => 1,
            ThemeMode.Dark => 2,
            _ => 0,
        };
        FontChoice.SelectedIndex = _vm.MessageFontSize switch
        {
            MessageFontSize.Sm => 0,
            MessageFontSize.Lg => 2,
            _ => 1,
        };
        EnterToSendSwitch.IsOn = _vm.EnterToSend;

        MicChoice.ItemsSource = _vm.Microphones;
        MicChoice.SelectedIndex = _vm.SelectedMicIndex;

        _initializing = false;
    }

    private async void ThemeChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        var theme = ThemeChoice.SelectedIndex switch
        {
            1 => ThemeMode.Light,
            2 => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
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

    private async void EnterToSendSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        await _vm.SetEnterToSendAsync(EnterToSendSwitch.IsOn);
    }

    private void MicChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged on ComboBox is two-way through the VM binding;
        // the VM's SelectedMicIndex setter triggers the save.
    }

    private void Identity_Click(object sender, RoutedEventArgs e)
        => Frame.Navigate(typeof(IdentityPage));
}
