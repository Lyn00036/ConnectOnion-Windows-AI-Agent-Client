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
        // "danger-only": red for a failure, neutral for everything else. Used on text that is
        // normally just prose — a completion label, a step's name — where the full palette would
        // paint every successful row green and turn the card into a traffic light. An optional
        // ":Key" suffix names the neutral brush, so a caller keeps whatever weight of grey it
        // already had ("danger-only:TextTertiaryBrush") instead of being nudged a shade darker.
        var dangerOnly = parameter as string is { } p && p.StartsWith("danger-only", StringComparison.Ordinal)
            ? p.Split(':') is [_, var neutral] ? neutral : "TextSecondaryBrush"
            : null;
        // "muted-success": keeps the warning amber and the failure red, but lets a successful step
        // sit in the neutral grey. Succeeding is what a tool is *supposed* to do, so colouring it
        // spends the reader's attention on the rows that need none — the same argument as
        // "danger-only" above, one notch weaker, because a warning here is still worth a glance.
        var mutedSuccess = string.Equals(parameter as string, "muted-success", StringComparison.Ordinal);
        var key = value switch
        {
            ToolActivityStatus.Failed or ToolStepStatus.Failed => "DangerBrush",
            _ when dangerOnly is not null => dangerOnly,
            ToolActivityStatus.Success or ToolStepStatus.Success when mutedSuccess => "TextSecondaryBrush",
            ToolActivityStatus.Success or ToolStepStatus.Success => "SuccessBrush",
            ToolActivityStatus.PartialSuccess or ToolStepStatus.Warning => "WarningBrush",
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
