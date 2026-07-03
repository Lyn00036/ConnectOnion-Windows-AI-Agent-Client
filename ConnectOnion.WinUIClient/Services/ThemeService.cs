using ConnectOnion.WinUIClient.Models;
using Microsoft.UI.Xaml;

namespace ConnectOnion.WinUIClient.Services;

/// <summary>
/// Applies the persisted theme to the app's root element. WinUI applies
/// <see cref="FrameworkElement.RequestedTheme"/> per element tree, so the
/// window registers its root here and the Settings page can re-apply live.
/// </summary>
public static class ThemeService
{
    private static FrameworkElement? _root;

    public static event System.Action<ThemeMode>? ThemeApplied;

    public static void RegisterRoot(FrameworkElement root) => _root = root;

    public static void Apply(ThemeMode theme)
    {
        if (_root is null) return;
        _root.RequestedTheme = theme == ThemeMode.Dark ? ElementTheme.Dark : ElementTheme.Light;
        ThemeApplied?.Invoke(theme);
    }
}
