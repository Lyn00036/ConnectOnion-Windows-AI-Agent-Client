using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;

namespace ConnectOnion.WinUIClient;

public sealed partial class MainWindow
{
    private void TrackWindowLifetime()
    {
        RegisterFileMenuAccelerators();
        RegisterEditMenuAccelerators();
        RegisterViewMenuAccelerators();
        RegisterHelpMenuAccelerators();
        RegisterShortcutHints();
        RegisterDragDropHint();
    }

    private async void NewChat_Click(object sender, RoutedEventArgs e)
        => await StartNewChatAsync();

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
        => await PickAndOpenFolderAsync();

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
        => MinimizeToTray();

    private void SettingsMenu_Click(object sender, RoutedEventArgs e)
        => ShowSettingsOverlay(sender as FrameworkElement);

    private void Exit_Click(object sender, RoutedEventArgs e)
        => ExitApplication();

    private void RegisterFileMenuAccelerators()
        => RootGrid.KeyDown += FileMenuShortcut_KeyDown;

    private async void FileMenuShortcut_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // While a modal overlay is open it owns all input — don't fire background
        // accelerators behind it.
        if (IsModalOverlayOpen) return;

        switch (MatchShortcut(e))
        {
            case KeyboardShortcutCatalog.Ids.NewChat:
                await StartNewChatAsync();
                e.Handled = true;
                return;
            case KeyboardShortcutCatalog.Ids.OpenFolder:
                await PickAndOpenFolderAsync();
                e.Handled = true;
                return;
            case KeyboardShortcutCatalog.Ids.CloseWindow:
                MinimizeToTray();
                e.Handled = true;
                return;
            case KeyboardShortcutCatalog.Ids.OpenSettings:
                ShowSettingsOverlay();
                e.Handled = true;
                return;
            case KeyboardShortcutCatalog.Ids.Exit:
                ExitApplication();
                e.Handled = true;
                return;
        }
    }

    /// <summary>
    /// Resolves a keystroke to the shortcut id it is bound to, or null. The single place the
    /// window turns raw keys into actions — the bindings themselves live in
    /// <see cref="KeyboardShortcutCatalog"/> and whatever the user rebound them to, so no handler
    /// hard-codes a key any more. The key goes through <see cref="LayoutKeys"/> first so a
    /// punctuation binding means the character, not a US keyboard position.
    /// </summary>
    private static string? MatchShortcut(KeyRoutedEventArgs e)
        => AppServices.Shortcuts.Match(
            LayoutKeys.Normalize((int)e.Key),
            IsKeyDown(VirtualKey.Control),
            IsKeyDown(VirtualKey.Shift),
            IsKeyDown(VirtualKey.Menu));

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
