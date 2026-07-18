using System;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ConnectOnion.WinUIClient.Presentation;

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

/// <summary>
/// Colors the status dot by presence — green online, neutral offline, muted
/// otherwise. Color is a secondary signal here: every presence dot this feeds
/// is paired with a text label (see <c>PresenceLabel</c>/<c>StatusText</c> in
/// the view models), so presence is never conveyed by color alone.
/// </summary>
public sealed class PresenceToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is AgentPresence presence
            ? presence switch
            {
                AgentPresence.Online => "SuccessBrush",
                AgentPresence.Checking => "InfoBrush",
                AgentPresence.Offline => "TextTertiaryBrush",
                _ => "TextTertiaryBrush",
            }
            : "TextTertiaryBrush";
        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>
/// Colors an event's status dot/badge: medium running, green done, darker error.
/// Running deliberately does NOT use BrandPrimaryBrush — Success is the brand green,
/// so the two would render identically and the dot would stop meaning anything.
/// See StatusRunningColor in Styles/Colors.xaml.
/// </summary>
public sealed class EventStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is EventStatus status
            ? status switch
            {
                EventStatus.Running => "StatusRunningBrush",
                EventStatus.Error => "DangerBrush",
                _ => "SuccessBrush",
            }
            : "TextTertiaryBrush";
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
        var key = value is ChatRole.User ? "UserMessageBackgroundBrush" : "AgentMessageBackgroundBrush";
        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
