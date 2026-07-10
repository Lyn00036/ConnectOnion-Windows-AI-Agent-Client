using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// Brand-colored square avatar showing an agent's initial. Shared across the
/// home cards, agent detail, chat header and sidebar. The visual size is set
/// per-site via <see cref="Size"/>; corner radius and font size reuse the
/// inherited <see cref="Control.CornerRadius"/>/<see cref="Control.FontSize"/>.
/// </summary>
public sealed partial class AgentAvatar : UserControl
{
    public static readonly DependencyProperty InitialProperty =
        DependencyProperty.Register(
            nameof(Initial),
            typeof(string),
            typeof(AgentAvatar),
            new PropertyMetadata("?"));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(
            nameof(Size),
            typeof(double),
            typeof(AgentAvatar),
            new PropertyMetadata(32.0));

    public AgentAvatar()
    {
        InitializeComponent();
    }

    public string Initial
    {
        get => (string)GetValue(InitialProperty);
        set => SetValue(InitialProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }
}
