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
    private static ThemeMode _theme = ThemeMode.System;

    public static event System.Action<ElementTheme>? ThemeApplied;

    public static void RegisterRoot(FrameworkElement root)
    {
        if (_root is not null)
        {
            _root.ActualThemeChanged -= Root_ActualThemeChanged;
        }

        _root = root;
        _root.ActualThemeChanged += Root_ActualThemeChanged;
    }

    public static void Apply(ThemeMode theme)
    {
        _theme = theme;
        if (_root is null) return;
        _root.RequestedTheme = theme switch
        {
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.Light => ElementTheme.Light,
            _ => ElementTheme.Default,
        };
        ThemeApplied?.Invoke(EffectiveTheme);
    }

    private static ElementTheme EffectiveTheme
        => _root?.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;

    private static void Root_ActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_theme == ThemeMode.System)
        {
            ThemeApplied?.Invoke(EffectiveTheme);
        }
    }
}
