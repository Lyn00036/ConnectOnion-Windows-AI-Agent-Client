using System;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class AgentDetailPage : Page
{
    public AgentDetailViewModel Vm { get; } = new();

    public AgentDetailPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Vm.LoadAsync();
        Unloaded += (_, _) => Vm.Cleanup();
    }

    private async void StartConversation_Click(object sender, RoutedEventArgs e)
        => await StartConversationAsync(PromptBox.Text);

    private async void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string prompt)
        {
            await StartConversationAsync(prompt);
        }
    }

    private async void PromptBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var isShiftDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (e.Key == VirtualKey.Enter && !isShiftDown)
        {
            e.Handled = true;
            await StartConversationAsync(PromptBox.Text);
        }
    }

    private async System.Threading.Tasks.Task StartConversationAsync(string prompt)
    {
        var initialPrompt = prompt.Trim();
        if (initialPrompt.Length == 0) return;

        PromptBox.Text = "";
        if (await Vm.StartConversationAsync(initialPrompt))
        {
            Frame.Navigate(typeof(ChatPage), initialPrompt);
        }
    }

    private void AgentRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PromptPanel.Width = Math.Max(300, Math.Min(656, e.NewSize.Width - 48));
    }
}
