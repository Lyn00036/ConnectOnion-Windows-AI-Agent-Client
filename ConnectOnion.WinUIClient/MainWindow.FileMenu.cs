using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;

namespace ConnectOnion.WinUIClient;

public sealed partial class MainWindow
{
    private static readonly List<Window> OpenWindows = new();

    private void TrackWindowLifetime()
    {
        OpenWindows.Add(this);
        Closed += (_, _) => OpenWindows.Remove(this);
        RegisterFileMenuAccelerators();
    }

    private void NewWindow_Click(object sender, RoutedEventArgs e)
        => OpenNewWindow();

    private async void NewChat_Click(object sender, RoutedEventArgs e)
        => await StartNewChatAsync();

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
        => await PickAndOpenFolderAsync();

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
        => MinimizeToTray();

    private void SettingsMenu_Click(object sender, RoutedEventArgs e)
        => NavigateTo(typeof(SettingsPage));

    private void Exit_Click(object sender, RoutedEventArgs e)
        => ExitApplication();

    private void OpenNewWindow()
    {
        var window = new MainWindow();
        window.Activate();
    }

    private void RegisterFileMenuAccelerators()
        => RootGrid.KeyDown += FileMenuShortcut_KeyDown;

    private async void FileMenuShortcut_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!IsKeyDown(VirtualKey.Control)) return;

        var hasShift = IsKeyDown(VirtualKey.Shift);
        var hasAlt = IsKeyDown(VirtualKey.Menu);

        if (e.Key == VirtualKey.N && hasShift && !hasAlt)
        {
            OpenNewWindow();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.N && !hasShift && !hasAlt)
        {
            await StartNewChatAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.O && !hasShift && !hasAlt)
        {
            await PickAndOpenFolderAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.W && !hasShift && !hasAlt)
        {
            MinimizeToTray();
            e.Handled = true;
            return;
        }

        if ((int)e.Key == 188 && !hasShift && !hasAlt)
        {
            NavigateTo(typeof(SettingsPage));
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Q && !hasShift && !hasAlt)
        {
            ExitApplication();
            e.Handled = true;
        }
    }

    private static bool IsKeyDown(VirtualKey key)
        => (GetKeyState((int)key) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private async Task StartNewChatAsync()
    {
        if (!await ShellSidebar.StartNewChatAsync())
        {
            NavigateTo(typeof(HomePage), forceReload: true);
            await ShowMenuNoticeAsync("No agents available", "Add an agent before starting a chat.");
        }
    }

    private async Task PickAndOpenFolderAsync()
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            await Launcher.LaunchFolderAsync(folder);
        }
    }

    private async Task ShowMenuNoticeAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = RootGrid.XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
