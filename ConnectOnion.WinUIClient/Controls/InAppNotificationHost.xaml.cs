using System;
using ConnectOnion.WinUIClient.Models.Notifications;
using ConnectOnion.WinUIClient.Services;
using FluentIcons.Common;
using FluentIcons.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// A window-local overlay that stacks lightweight, non-blocking in-app toasts in
/// the top-right corner. Cards auto-dismiss (approvals linger longer), can be
/// dismissed with a close button, and navigate to their conversation on click.
/// Never uses a modal dialog, so it can't interrupt the user's typing.
/// </summary>
public sealed partial class InAppNotificationHost : UserControl
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ApprovalLifetime = TimeSpan.FromSeconds(12);
    private const int MaxVisible = 4;

    public InAppNotificationHost() => InitializeComponent();

    public void ShowToast(InAppToastModel toast)
    {
        // Cap the stack so a burst can't fill the window.
        while (ToastStack.Children.Count >= MaxVisible)
            ToastStack.Children.RemoveAt(0);

        ToastStack.Children.Add(BuildCard(toast));
    }

    private Border BuildCard(InAppToastModel toast)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 8, 10),
            Background = Brush("SurfaceElevatedBrush"),
            BorderBrush = Brush("BorderDefaultBrush"),
            BorderThickness = new Thickness(1),
        };

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FluentIcon
        {
            Icon = IconFor(toast.Type),
            IconVariant = IconVariant.Regular,
            IconSize = IconSize.Size20,
            Foreground = Brush(BrushKeyFor(toast.Type)),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0),
        };
        Grid.SetColumn(icon, 0);

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        text.Children.Add(new TextBlock
        {
            Text = toast.Title,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = toast.Body,
            FontSize = 12,
            Foreground = Brush("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var close = new Button
        {
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Top,
            Content = new FluentIcon { Icon = Icon.Dismiss, IconVariant = IconVariant.Regular, IconSize = IconSize.Size12, Foreground = Brush("TextSecondaryBrush") },
        };
        Grid.SetColumn(close, 2);

        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(close);
        card.Child = grid;

        var timer = new DispatcherTimer
        {
            Interval = toast.Type == NotificationType.ApprovalRequired ? ApprovalLifetime : DefaultLifetime,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Remove(card);
        };

        close.Click += (_, _) =>
        {
            timer.Stop();
            Remove(card);
        };

        card.Tapped += (_, e) =>
        {
            e.Handled = true;
            timer.Stop();
            Remove(card);
            try { AppServices.Navigation.OpenConversation(toast.AgentId, toast.ConversationId); }
            catch { /* best-effort navigation on click */ }
        };

        timer.Start();
        return card;
    }

    private void Remove(Border card)
    {
        if (ToastStack.Children.Contains(card))
            ToastStack.Children.Remove(card);
    }

    private static Icon IconFor(NotificationType type) => type switch
    {
        NotificationType.AgentReply => Icon.Chat,
        NotificationType.TaskCompleted => Icon.CheckmarkCircle,
        NotificationType.ApprovalRequired => Icon.ShieldTask,
        NotificationType.ConnectionLost => Icon.PlugDisconnected,
        _ => Icon.ErrorCircle,
    };

    private static string BrushKeyFor(NotificationType type) => type switch
    {
        NotificationType.TaskCompleted => "SuccessBrush",
        NotificationType.ApprovalRequired => "WarningBrush",
        NotificationType.ConnectionLost => "DangerBrush",
        NotificationType.Error => "DangerBrush",
        _ => "BrandPrimaryBrush",
    };

    private static Brush Brush(string key)
        => (Brush)Application.Current.Resources[key];
}
