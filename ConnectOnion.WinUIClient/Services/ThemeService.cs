using ConnectOnion.WinUIClient.Models;
using Microsoft.UI.Xaml;
using Windows.UI;

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

    /// <summary>
    /// Resolves a <see cref="Color"/> token straight from the app's Light/Dark
    /// <c>Styles/Colors.xaml</c> ThemeDictionaries, for the specific requested
    /// theme regardless of the app's current ambient theme. For the handful of
    /// call sites (Win2D canvas draws, <c>AppWindowTitleBar</c>) that need a raw
    /// <see cref="Color"/> rather than a brush and can't use
    /// <c>{ThemeResource}</c> markup — keeps them reading the same tokens pages
    /// bind to instead of duplicating hex literals.
    /// <c>Application.Resources.ThemeDictionaries</c> only reflects entries
    /// declared directly on that dictionary, not ones nested inside its merged
    /// dictionaries, so this walks <c>MergedDictionaries</c> to find the one
    /// (<c>Colors.xaml</c>) that actually declares <paramref name="key"/>.
    /// </summary>
    public static Color GetColor(string key, bool isDark)
    {
        var themeKey = isDark ? "Dark" : "Light";
        foreach (var merged in Application.Current.Resources.MergedDictionaries)
        {
            if (merged.ThemeDictionaries.TryGetValue(themeKey, out var themeDictionaryObj)
                && themeDictionaryObj is ResourceDictionary themeDictionary
                && themeDictionary.TryGetValue(key, out var value))
            {
                return (Color)value;
            }
        }

        throw new System.Collections.Generic.KeyNotFoundException($"Theme color '{key}' not found in '{themeKey}'.");
    }

    private static void Root_ActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_theme == ThemeMode.System)
        {
            ThemeApplied?.Invoke(EffectiveTheme);
        }
    }
}
