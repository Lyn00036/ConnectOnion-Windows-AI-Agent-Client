using System;
using ConnectOnion.WinUIClient.Controls;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class AgentDetailPage : Page, IReloadablePage
{
    public AgentDetailViewModel Vm { get; } = new();

    public AgentDetailPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
        => await LoadPageAsync();

    /// <summary>Re-points this page at whichever agent is now selected, without rebuilding it —
    /// this page carries a <see cref="ChatComposer"/> (Win2D canvas and all), so clicking through
    /// the sidebar's agents was constructing and discarding one per click.</summary>
    public System.Threading.Tasks.Task ReloadAsync() => LoadPageAsync();

    private async System.Threading.Tasks.Task LoadPageAsync()
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

    private async void OfflineNotice_RecheckRequested(object? sender, EventArgs e)
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
