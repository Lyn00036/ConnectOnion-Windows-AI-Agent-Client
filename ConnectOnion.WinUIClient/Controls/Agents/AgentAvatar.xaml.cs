using ConnectOnion.WinUIClient.Common;
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
            new PropertyMetadata("?", OnInitialChanged));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(
            nameof(Size),
            typeof(double),
            typeof(AgentAvatar),
            new PropertyMetadata(32.0));

    public static readonly DependencyProperty PaletteIndexProperty =
        DependencyProperty.Register(
            nameof(PaletteIndex),
            typeof(int),
            typeof(AgentAvatar),
            new PropertyMetadata(-1, OnPaletteIndexChanged));

    public AgentAvatar()
    {
        InitializeComponent();
        ApplyPalette();
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

    /// <summary>A stable palette slot. Negative values derive a palette from the initial.</summary>
    public int PaletteIndex
    {
        get => (int)GetValue(PaletteIndexProperty);
        set => SetValue(PaletteIndexProperty, value);
    }

    private static void OnPaletteIndexChanged(DependencyObject sender, DependencyPropertyChangedEventArgs _)
        => ((AgentAvatar)sender).ApplyPalette();

    private static void OnInitialChanged(DependencyObject sender, DependencyPropertyChangedEventArgs _)
        => ((AgentAvatar)sender).ApplyPalette();

    private void ApplyPalette()
    {
        var state = PaletteIndex < 0 && string.IsNullOrWhiteSpace(Initial)
            ? "Neutral"
            : new[] { "Blue", "Violet", "Teal", "Coral" }[
                PaletteIndex < 0 ? AvatarPalette.From(Initial) : PaletteIndex % 4];
        VisualStateManager.GoToState(this, state, useTransitions: false);
    }
}
