using System;
using System.Collections.Generic;
using System.Text.Json;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class ChatPage : Page
{
    public ChatViewModel Vm { get; } = new();

    public ChatPage()
    {
        InitializeComponent();
        Vm.Messages.CollectionChanged += (_, _) => ScrollToEnd();
        Vm.AskUserRequested += OnAskUserRequested;
        Vm.ApprovalRequested += OnApprovalRequested;
        Vm.PlanReviewRequested += OnPlanReviewRequested;
        Loaded += async (_, _) => await Vm.LoadAsync();
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async void NewChat_Click(object sender, RoutedEventArgs e)
    {
        await Vm.NewConversationAsync();
        InputBox.Text = "";
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        await Vm.ReconnectAsync();
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
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private async System.Threading.Tasks.Task SendAsync()
    {
        var text = InputBox.Text;
        if (string.IsNullOrWhiteSpace(text) || !Vm.CanSend) return;

        InputBox.Text = "";
        await Vm.SendAsync(text);
        ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (Vm.Messages.Count > 0)
        {
            MessageList.ScrollIntoView(Vm.Messages[^1]);
        }
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
