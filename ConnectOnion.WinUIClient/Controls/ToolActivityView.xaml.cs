using ConnectOnion.WinUIClient.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

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

        // Save the ListView's scroll offset before the layout changes, so the
        // expansion of the timeline never shifts the header relative to content
        // above it in the chat transcript.
        var scroller = FindAncestorScrollViewer(this);
        var savedOffset = scroller?.VerticalOffset;

        await DisclosureAnimation.ToggleAsync(
            header,
            DisclosureAnimation.FindContentAfter(header),
            Activity.IsExpanded,
            Activity.ToggleExpanded);

        // Restore the scroll offset after the layout pass settles, preventing
        // the ListView's internal ScrollViewer from adjusting the viewport when
        // the timeline item height changes.
        if (savedOffset is > 0 && scroller is not null)
        {
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => scroller.ChangeView(null, savedOffset.Value, null, disableAnimation: true));
        }
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

    private void Header_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (HeaderTitle is not null)
            HeaderTitle.Foreground = ResolveBrush("TextPrimaryBrush");
        if (HeaderChevron is not null)
            HeaderChevron.Opacity = 1;
    }

    private void Header_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (HeaderTitle is not null)
            HeaderTitle.Foreground = ResolveBrush("TextSecondaryBrush");
        if (HeaderChevron is not null)
            HeaderChevron.Opacity = 0;
    }

    private void StepItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement root) return;
        if (root.FindName("StepNameText") is TextBlock nameText)
            nameText.Foreground = ResolveBrush("TextPrimaryBrush");
        if (root.FindName("StepChevron") is FrameworkElement chevron)
            chevron.Opacity = 1;
    }

    private void StepItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement root) return;
        if (root.FindName("StepNameText") is TextBlock nameText)
            nameText.Foreground = ResolveBrush("TextSecondaryBrush");
        if (root.FindName("StepChevron") is FrameworkElement chevron)
            chevron.Opacity = 0;
    }

    private static Brush ResolveBrush(string key)
        => Application.Current.Resources[key] as Brush
           ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128));

    /// <summary>Walks up the visual tree to find the ListView's internal
    /// ScrollViewer. Needed so we can save/restore the scroll offset around
    /// expand operations, preventing viewport shifts when item height changes.</summary>
    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject element)
    {
        for (var current = VisualTreeHelper.GetParent(element);
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollViewer sv) return sv;
        }
        return null;
    }
}
