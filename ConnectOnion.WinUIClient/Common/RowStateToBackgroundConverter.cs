using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ConnectOnion.WinUIClient.Common;

/// <summary>
/// Single source of truth for a sidebar row's background so selection and hover
/// never paint two independent, visually-conflicting overlays at once: bind
/// <c>Background</c> to <c>IsSelected</c> through this converter and
/// <c>Visibility</c> to <c>IsSelected || IsHovered</c> (the item's
/// <c>ShowRowBackground</c> property) — selected rows get the accent tint
/// (hovering a selected row keeps that same tint instead of layering a second,
/// differently-colored hover wash on top), unselected-but-hovered rows get the
/// app's standard neutral <c>ShellHoverBrush</c>.
/// </summary>
public sealed class RowStateToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isSelected = value is bool b && b;
        if (isSelected)
        {
            var accent = (SolidColorBrush)Application.Current.Resources["AccentFillColorSecondaryBrush"];
            return new SolidColorBrush(accent.Color) { Opacity = 0.18 };
        }
        return (Brush)Application.Current.Resources["ShellHoverBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
