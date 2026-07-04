using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text.Json;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class ChatPage : Page
{
    public ChatViewModel Vm { get; } = new();
    private ScrollViewer? _messageScrollViewer;
    private bool _isLoadingConversation;
    private bool _isAutoScrollQueued;
    private bool _stickToBottom = true;

    public ChatPage()
    {
        InitializeComponent();
        Vm.Messages.CollectionChanged += Messages_CollectionChanged;
        Vm.AskUserRequested += OnAskUserRequested;
        Vm.ApprovalRequested += OnApprovalRequested;
        Vm.PlanReviewRequested += OnPlanReviewRequested;
        Loaded += OnLoaded;
        Unloaded += (_, _) => Vm.Cleanup();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _messageScrollViewer ??= FindDescendant<ScrollViewer>(MessageList);
        if (_messageScrollViewer is not null)
        {
            _messageScrollViewer.ViewChanged -= MessageScrollViewer_ViewChanged;
            _messageScrollViewer.ViewChanged += MessageScrollViewer_ViewChanged;
        }

        _isLoadingConversation = true;
        try
        {
            await Vm.LoadAsync();
        }
        finally
        {
            _isLoadingConversation = false;
        }

        QueueScrollToEnd(force: true);
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async void NewChat_Click(object sender, RoutedEventArgs e)
    {
        await Vm.NewConversationAsync();
        InputBox.Text = "";
    }

    private async void Recheck_Click(object sender, RoutedEventArgs e)
    {
        await Vm.RecheckPresenceAsync();
    }

    private async void SubmitOnboard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ChatMessage message)
        {
            await Vm.SubmitOnboardInviteCodeAsync(message);
            ScrollToEnd();
        }
    }

    private async void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChatMessage message ||
            string.IsNullOrEmpty(message.Content))
        {
            return;
        }

        var data = new DataPackage();
        data.SetText(message.Content);
        Clipboard.SetContent(data);

        if (sender is Button button && button.Content is StackPanel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is TextBlock text)
                {
                    var original = text.Text;
                    text.Text = "Copied";
                    await System.Threading.Tasks.Task.Delay(1000);
                    text.Text = original;
                    break;
                }
            }
        }
    }

    private async void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var isShiftDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (e.Key == VirtualKey.Enter && !isShiftDown)
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private void ChatRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var isNarrow = e.NewSize.Width < 760;
        Grid.SetRow(ChatHeaderActions, isNarrow ? 1 : 0);
        Grid.SetColumn(ChatHeaderActions, isNarrow ? 0 : 1);
        Grid.SetColumnSpan(ChatHeaderActions, isNarrow ? 2 : 1);
        ChatHeaderActions.Margin = isNarrow ? new Thickness(40, 0, 0, 0) : new Thickness(0);
        ChatHeaderActions.Orientation = isNarrow ? Orientation.Vertical : Orientation.Horizontal;
        ChatHeaderActions.HorizontalAlignment = isNarrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;

        var horizontalInset = e.NewSize.Width < 640 ? 20 : 52;
        MessageList.Padding = new Thickness(horizontalInset, 18, horizontalInset, 22);
        ComposerPanel.Width = Math.Max(280, Math.Min(1104, e.NewSize.Width - 48));
    }

    private async System.Threading.Tasks.Task SendAsync()
    {
        var text = InputBox.Text;
        if (string.IsNullOrWhiteSpace(text) || !Vm.CanSend) return;

        InputBox.Text = "";
        await Vm.SendAsync(text);
        QueueScrollToEnd(force: true);
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isLoadingConversation) return;
        if (e.Action is not (NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace))
        {
            return;
        }

        QueueScrollToEnd(force: false);
    }

    private void QueueScrollToEnd(bool force)
    {
        if (Vm.Messages.Count == 0) return;
        if (!force && !_stickToBottom) return;
        if (_isAutoScrollQueued) return;

        _isAutoScrollQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _isAutoScrollQueued = false;
            ScrollToEnd();
        });
    }

    private void ScrollToEnd()
    {
        if (Vm.Messages.Count == 0) return;

        _messageScrollViewer ??= FindDescendant<ScrollViewer>(MessageList);
        if (_messageScrollViewer is not null)
        {
            _messageScrollViewer.ChangeView(null, _messageScrollViewer.ScrollableHeight, null, disableAnimation: true);
            return;
        }

        MessageList.ScrollIntoView(Vm.Messages[^1]);
    }

    private void MessageScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_messageScrollViewer is null) return;

        var distanceFromBottom = _messageScrollViewer.ScrollableHeight - _messageScrollViewer.VerticalOffset;
        _stickToBottom = distanceFromBottom < 96;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }

        return null;
    }

    // --- Interactive turns ---

    private async void OnAskUserRequested(AskUserRequest request)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = request.Text, TextWrapping = TextWrapping.Wrap });

        // Dynamic fields take priority; otherwise options (single/multi) plus a
        // free-text box, matching the web app's ask_user handling.
        var fieldInputs = new List<(string Name, TextBox Box)>();
        var optionChecks = new List<CheckBox>();
        RadioButtons? radio = null;
        TextBox? freeText = null;

        if (request.Fields.Count > 0)
        {
            foreach (var field in request.Fields)
            {
                panel.Children.Add(new TextBlock { Text = field.Label, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
                var box = new TextBox { PlaceholderText = field.Placeholder ?? "" };
                panel.Children.Add(box);
                fieldInputs.Add((field.Name, box));
            }
        }
        else
        {
            if (request.Options.Count > 0)
            {
                if (request.MultiSelect)
                {
                    foreach (var option in request.Options)
                    {
                        var check = new CheckBox { Content = option, Tag = option };
                        optionChecks.Add(check);
                        panel.Children.Add(check);
                    }
                }
                else
                {
                    radio = new RadioButtons();
                    foreach (var option in request.Options) radio.Items.Add(option);
                    panel.Children.Add(radio);
                }
            }
            freeText = new TextBox { PlaceholderText = request.Options.Count > 0 ? "Or enter another answer" : "Type your answer" };
            panel.Children.Add(freeText);
        }

        var dialog = new ContentDialog
        {
            Title = "Question",
            Content = new ScrollViewer { Content = panel },
            PrimaryButtonText = "Submit",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        string answer;
        if (fieldInputs.Count > 0)
        {
            var values = new Dictionary<string, string>();
            foreach (var (name, box) in fieldInputs) values[name] = box.Text;
            answer = JsonSerializer.Serialize(values, AppJsonContext.Default.DictionaryStringString);
        }
        else if (optionChecks.Count > 0)
        {
            var selected = new List<string>();
            foreach (var check in optionChecks)
                if (check.IsChecked == true) selected.Add((string)check.Tag);
            answer = string.Join(", ", selected);
        }
        else
        {
            answer = !string.IsNullOrWhiteSpace(freeText?.Text)
                ? freeText!.Text.Trim()
                : radio?.SelectedItem as string ?? "";
        }

        if (!string.IsNullOrEmpty(answer)) await Vm.RespondAskUserAsync(answer);
    }

    private async void OnApprovalRequested(ApprovalRequest request)
    {
        var panel = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrEmpty(request.Description))
            panel.Children.Add(new TextBlock { Text = request.Description, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Arguments:", Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
        panel.Children.Add(new TextBlock { Text = request.ArgumentsJson, TextWrapping = TextWrapping.Wrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") });

        var dialog = new ContentDialog
        {
            Title = $"Approve tool: {request.Tool}",
            Content = new ScrollViewer { Content = panel },
            PrimaryButtonText = "Approve once",
            SecondaryButtonText = "Approve for session",
            CloseButtonText = "Reject",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        switch (result)
        {
            case ContentDialogResult.Primary:
                await Vm.RespondApprovalAsync(true, "once");
                break;
            case ContentDialogResult.Secondary:
                await Vm.RespondApprovalAsync(true, "session");
                break;
            default:
                await Vm.RespondApprovalAsync(false);
                break;
        }
    }

    private async void OnPlanReviewRequested(PlanReviewRequest request)
    {
        var feedback = new TextBox
        {
            PlaceholderText = "Optional feedback or requested changes",
            AcceptsReturn = true,
            Height = 100,
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = request.PlanContent, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(feedback);

        var dialog = new ContentDialog
        {
            Title = "Plan review",
            Content = new ScrollViewer { Content = panel, MaxHeight = 400 },
            PrimaryButtonText = "Approve plan",
            SecondaryButtonText = "Request changes",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var message = string.IsNullOrWhiteSpace(feedback.Text)
                ? "Plan approved. Proceed with implementation."
                : feedback.Text.Trim();
            await Vm.RespondPlanReviewAsync(message);
        }
        else if (result == ContentDialogResult.Secondary && !string.IsNullOrWhiteSpace(feedback.Text))
        {
            await Vm.RespondPlanReviewAsync(feedback.Text.Trim());
        }
    }
}
