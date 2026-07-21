using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ConnectOnion.WinUIClient.Presentation;

/// <summary>
/// Resolves a themed <see cref="Brush"/> from <c>Application.Current.Resources</c> by key, freshly
/// on every call — never cached, because the brush behind a key changes when the user flips
/// light/dark and a cached instance would keep painting the old theme's colour until the control
/// was rebuilt.
///
/// A missing key returns <c>null</c> rather than throwing, and returning <c>null</c> to a binding
/// fails deep inside the binding engine with no useful stack. So this ends in a neutral grey shown
/// only if the palette itself failed to merge — a visibly-wrong colour is the better failure. That
/// last-resort grey lived copied across every resource-reading converter and code-behind; it now
/// lives here once.
/// </summary>
internal static class ThemeBrushResolver
{
    // Mid-grey, legible on both light and dark. Shared instance: it is only ever assigned when a
    // key is missing (never in practice), and a Brush may back many elements safely.
    private static readonly Brush PaletteMissingFallback =
        new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));

    /// <summary>
    /// Returns the brush at <paramref name="key"/>, else the brush at <paramref name="fallbackKey"/>
    /// (when given), else the palette-missing grey.
    /// </summary>
    public static Brush Resolve(string key, string? fallbackKey = null)
        => Application.Current.Resources[key] as Brush
           ?? (fallbackKey is not null ? Application.Current.Resources[fallbackKey] as Brush : null)
           ?? PaletteMissingFallback;
}
