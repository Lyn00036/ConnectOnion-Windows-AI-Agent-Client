using System;
using ConnectOnion.WinUIClient.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ConnectOnion.WinUIClient.Presentation;

public sealed class ToolStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value switch
        {
            ToolActivityStatus.Success or ToolStepStatus.Success => "SuccessBrush",
            ToolActivityStatus.PartialSuccess or ToolStepStatus.Warning => "WarningBrush",
            ToolActivityStatus.Failed or ToolStepStatus.Failed => "DangerBrush",
            ToolActivityStatus.WaitingForConfirmation or ToolActivityStatus.WaitingForPermission => "WarningBrush",
            _ => "TextSecondaryBrush",
        };
        return Application.Current.Resources[key] as Brush
            ?? Application.Current.Resources["TextSecondaryBrush"] as Brush
            ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
