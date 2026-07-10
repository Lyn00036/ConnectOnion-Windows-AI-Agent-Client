using System;
using ConnectOnion.WinUIClient.Controls;
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
        Composer.CanSubmit = Vm.CanStartConversation;
        Composer.FocusInput();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => Vm.Cleanup();

    // This composer starts a fresh conversation before navigating to ChatPage.
    // The whole submission (text + attachments) is carried across the
    // navigation via the Frame parameter so an image/file sent from here
    // behaves exactly like one sent from ChatPage's own composer: it creates
    // the session, navigates, and is actually sent as the first turn.
    private async void Composer_SubmitRequested(object? sender, ComposerSubmission submission)
        => await StartConversationAsync(submission);

    private async void RecheckPresence_Click(object sender, RoutedEventArgs e)
        => await Vm.RecheckPresenceAsync();

    private async System.Threading.Tasks.Task StartConversationAsync(ComposerSubmission submission)
    {
        var initialPrompt = submission.Text.Trim();
        var hasAttachments = submission.Attachments.Count > 0;
        if (initialPrompt.Length == 0 && !hasAttachments) return;

        if (await Vm.StartConversationAsync(initialPrompt, hasAttachments))
        {
            Frame.Navigate(typeof(ChatPage), new ComposerSubmission(initialPrompt, submission.Attachments));
        }
    }
}
