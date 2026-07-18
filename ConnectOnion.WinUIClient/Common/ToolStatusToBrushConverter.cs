using System;
using ConnectOnion.WinUIClient.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ConnectOnion.WinUIClient.Presentation;

/// <summary>
/// Maps a tool-activity or tool-step status onto a semantic brush from the app palette.
///
/// Takes both <see cref="ToolActivityStatus"/> and <see cref="ToolStepStatus"/> because the
/// activity card and the step rows inside it are styled by the same rules — one converter
/// keeps a failed step and a failed activity the same red without the two enums having to
/// agree on member names.
/// </summary>
public sealed class ToolStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // Note the pairings are not one-to-one: PartialSuccess and both "waiting on a human"
        // states all read as warning, because to the user they mean the same thing — this
        // step didn't simply succeed.
        var key = value switch
        {
            ToolActivityStatus.Success or ToolStepStatus.Success => "SuccessBrush",
            ToolActivityStatus.PartialSuccess or ToolStepStatus.Warning => "WarningBrush",
            ToolActivityStatus.Failed or ToolStepStatus.Failed => "DangerBrush",
            ToolActivityStatus.WaitingForConfirmation or ToolActivityStatus.WaitingForPermission => "WarningBrush",
            _ => "TextSecondaryBrush",
        };
        // Resolved from Application.Resources on every call rather than cached in a field:
        // the brush behind a key changes when the user flips light/dark, and a cached Brush
        // would keep painting the old theme's color until the control was recreated.
        //
        // Two fallbacks because a missing key returns null rather than throwing: first the
        // neutral token, then a hard-coded grey for the case where the palette itself failed
        // to merge. Returning null would throw inside the binding engine with no useful
        // stack, so a visibly-wrong grey is the better failure.
        return Application.Current.Resources[key] as Brush
            ?? Application.Current.Resources["TextSecondaryBrush"] as Brush
            ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128));
    }

    // One-way by nature: a brush cannot tell you which status produced it.
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
