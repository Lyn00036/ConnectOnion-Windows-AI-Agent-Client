using System;
using System.Numerics;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>Shared short transition for transcript disclosure rows. Closing finishes its fade
/// before Visibility collapses; opening makes the content measurable first, then fades it in.
/// The header is disabled for the 140 ms transition so rapid clicks cannot invert model state.</summary>
internal static class DisclosureAnimation
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(140);
    private static readonly bool AnimationsEnabled = new UISettings().AnimationsEnabled;

    public static FrameworkElement? FindContentAfter(Button header)
    {
        if (header.Parent is not Panel parent) return null;

        var index = parent.Children.IndexOf(header);
        for (var i = index + 1; i < parent.Children.Count; i++)
        {
            if (parent.Children[i] is FrameworkElement element) return element;
        }
        return null;
    }

    public static async Task ToggleAsync(
        Button header,
        FrameworkElement? content,
        bool isExpanded,
        Action toggle)
    {
        if (content is null || !AnimationsEnabled)
        {
            toggle();
            return;
        }

        header.IsEnabled = false;
        content.IsHitTestVisible = false;
        try
        {
            if (isExpanded)
            {
                await AnimationBuilder.Create()
                    .Opacity(to: 0, duration: Duration)
                    .Translation(Axis.Y, to: -3, duration: Duration)
                    .StartAsync(content);
                toggle();
            }
            else
            {
                toggle();
                content.Opacity = 0;
                content.Translation = new Vector3(0, -3, 0);
                await Task.Yield();
                await AnimationBuilder.Create()
                    .Opacity(to: 1, duration: Duration)
                    .Translation(Axis.Y, to: 0, duration: Duration)
                    .StartAsync(content);
            }
        }
        finally
        {
            content.Opacity = 1;
            content.Translation = Vector3.Zero;
            content.IsHitTestVisible = true;
            header.IsEnabled = true;
        }
    }
}
