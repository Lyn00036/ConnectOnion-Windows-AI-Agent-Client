using System;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
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

/// <summary>Colors the status dot by presence: green online, red offline, muted otherwise.</summary>
public sealed class PresenceToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is AgentPresence presence
            ? presence switch
            {
                AgentPresence.Online => "StatusSuccessTextBrush",
                AgentPresence.Offline => "StatusErrorTextBrush",
                _ => "TextFillColorTertiaryBrush",
            }
            : "TextFillColorTertiaryBrush";
        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Colors an event's status dot/badge: blue running, green done, red error.</summary>
public sealed class EventStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is EventStatus status
            ? status switch
            {
                EventStatus.Running => "AccentFillColorDefaultBrush",
                EventStatus.Error => "StatusErrorTextBrush",
                _ => "StatusSuccessTextBrush",
            }
            : "TextFillColorTertiaryBrush";
        return (Brush)Application.Current.Resources[key];
    }

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
