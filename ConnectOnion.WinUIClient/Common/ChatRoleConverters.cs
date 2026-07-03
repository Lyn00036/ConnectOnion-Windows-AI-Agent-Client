using System;
using ConnectOnion.WinUIClient.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ConnectOnion.WinUIClient.Common;

/// <summary>Right-aligns the user's bubbles, left-aligns the agent's.</summary>
public sealed class RoleToAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is ChatRole.User ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Visible when the string is non-empty, collapsed otherwise.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Subtle fill for chat bubbles.</summary>
public sealed class RoleToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is ChatRole.User ? "LayerFillColorDefaultBrush" : "CardBackgroundFillColorDefaultBrush";
        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
