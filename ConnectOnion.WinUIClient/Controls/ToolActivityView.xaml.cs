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

    private void Header_Click(object sender, RoutedEventArgs e) => Activity?.ToggleExpanded();
    private void Step_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ToolStepViewModel step) step.ToggleExpanded();
    }
}
