using System;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _vm = new();
    private bool _initializing;
    private string _currentCategory = "General";

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

        LoadNotificationSettings();

        _initializing = false;
    }

    private void LoadNotificationSettings()
    {
        var s = AppServices.NotificationSettings.Current;
        EnableNotificationsSwitch.IsOn = s.EnableNotifications;
        NotifyAgentRepliesSwitch.IsOn = s.NotifyAgentReplies;
        NotifyTaskCompletionSwitch.IsOn = s.NotifyTaskCompletion;
        NotifyApprovalRequestsSwitch.IsOn = s.NotifyApprovalRequests;
        NotifyConnectionProblemsSwitch.IsOn = s.NotifyConnectionProblems;
        PlayNotificationSoundSwitch.IsOn = s.PlayNotificationSound;
        ShowMessagePreviewSwitch.IsOn = s.ShowMessagePreview;
        NotificationDetailPanel.IsEnabled = s.EnableNotifications;
    }

    private async void EnableNotificationsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        NotificationDetailPanel.IsEnabled = EnableNotificationsSwitch.IsOn;
        if (_initializing) return;
        await SaveNotificationSettingsAsync();
    }

    private async void NotificationDetail_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        await SaveNotificationSettingsAsync();
    }

    private System.Threading.Tasks.Task SaveNotificationSettingsAsync()
    {
        var settings = new Models.Notifications.NotificationSettings
        {
            EnableNotifications = EnableNotificationsSwitch.IsOn,
            NotifyAgentReplies = NotifyAgentRepliesSwitch.IsOn,
            NotifyTaskCompletion = NotifyTaskCompletionSwitch.IsOn,
            NotifyApprovalRequests = NotifyApprovalRequestsSwitch.IsOn,
            NotifyConnectionProblems = NotifyConnectionProblemsSwitch.IsOn,
            PlayNotificationSound = PlayNotificationSoundSwitch.IsOn,
            ShowMessagePreview = ShowMessagePreviewSwitch.IsOn,
        };
        return AppServices.NotificationSettings.SaveAsync(settings);
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
        if (_initializing) return;
        _vm.SelectedMicIndex = MicChoice.SelectedIndex;
    }

    public void SelectCategory(string category)
    {
        _currentCategory = category;

        // "General" is two cards: Appearance (how the app looks) and Chat (how chat behaves/reads).
        var isGeneral = category == "General";
        GeneralSection.Visibility = isGeneral ? Visibility.Visible : Visibility.Collapsed;
        ChatSection.Visibility = isGeneral ? Visibility.Visible : Visibility.Collapsed;

        NotificationsSection.Visibility = category == "Notifications" ? Visibility.Visible : Visibility.Collapsed;
        AudioSection.Visibility = category == "Audio" ? Visibility.Visible : Visibility.Collapsed;
        ContentScrollViewer.ChangeView(null, 0, null, true);
    }

    public void Filter(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            SelectCategory(_currentCategory);
            return;
        }

        var value = query.Trim();
        GeneralSection.Visibility = Contains(value, "general appearance theme light dark system") ? Visibility.Visible : Visibility.Collapsed;
        ChatSection.Visibility = Contains(value, "general chat message text size font enter send newline") ? Visibility.Visible : Visibility.Collapsed;
        NotificationsSection.Visibility = Contains(value, "notifications agent replies task completion approval connection sound preview") ? Visibility.Visible : Visibility.Collapsed;
        AudioSection.Visibility = Contains(value, "audio microphone voice input") ? Visibility.Visible : Visibility.Collapsed;
        ContentScrollViewer.ChangeView(null, 0, null, true);
    }

    private static bool Contains(string query, string terms)
        => terms.Contains(query, StringComparison.OrdinalIgnoreCase);
}
