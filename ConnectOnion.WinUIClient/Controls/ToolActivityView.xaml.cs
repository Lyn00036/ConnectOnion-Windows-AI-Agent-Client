using ConnectOnion.WinUIClient.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Controls;

public sealed partial class ToolActivityView : UserControl
{
    public static readonly DependencyProperty ActivityProperty = DependencyProperty.Register(
        nameof(Activity), typeof(ToolActivityViewModel), typeof(ToolActivityView), new PropertyMetadata(null));

    public ToolActivityViewModel Activity
    {
        get => (ToolActivityViewModel)GetValue(ActivityProperty);
        set => SetValue(ActivityProperty, value);
    }

    public ToolActivityView() => InitializeComponent();

    private async void Header_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button header || Activity is null) return;
        await DisclosureAnimation.ToggleAsync(
            header,
            DisclosureAnimation.FindContentAfter(header),
            Activity.IsExpanded,
            Activity.ToggleExpanded);
    }

    private async void Step_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button header || header.Tag is not ToolStepViewModel step) return;
        await DisclosureAnimation.ToggleAsync(
            header,
            DisclosureAnimation.FindContentAfter(header),
            step.IsExpanded,
            step.ToggleExpanded);
    }
}
