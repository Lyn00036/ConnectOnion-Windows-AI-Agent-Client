using System;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class AgentDetailPage : Page
{
    public AgentDetailViewModel Vm { get; } = new();

    public AgentDetailPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await Vm.LoadAsync();
        Composer.CanSubmit = Vm.HasAgent;
        Composer.FocusInput();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => Vm.Cleanup();

    private async void Composer_SubmitRequested(object? sender, string text)
        => await StartConversationAsync(text);

    private async System.Threading.Tasks.Task StartConversationAsync(string prompt)
    {
        var initialPrompt = prompt.Trim();
        if (initialPrompt.Length == 0) return;

        if (await Vm.StartConversationAsync(initialPrompt))
        {
            Frame.Navigate(typeof(ChatPage), initialPrompt);
        }
    }
}
