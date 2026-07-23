using System;
using ConnectOnion.WinUIClient.Controls;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class AgentDetailPage : Page, IReloadablePage, IShutdownDisarmable
{
    public AgentDetailViewModel Vm { get; } = App.GetService<AgentDetailViewModel>();

    public AgentDetailPage()
    {
        InitializeComponent();

        // Reuse this one instance across navigations rather than rebuilding it — this page also
        // hosts a ChatComposer (Win2D canvas) — every time the user crosses page types (e.g.
        // ChatPage → AgentDetailPage). MainWindow.NavigateTo's same-type reuse only covers
        // agent→agent, so a chat→agent switch was constructing and discarding a fresh tree per
        // click. The presence hook stays attached for this single reused instance (see OnLoaded);
        // there is no per-view state to tear down, so there is no Unloaded teardown.
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
        => await LoadPageAsync();

    /// <summary>Re-points this page at whichever agent is now selected, without rebuilding it —
    /// this page carries a <see cref="ChatComposer"/> (Win2D canvas and all), so clicking through
    /// the sidebar's agents was constructing and discarding one per click.</summary>
    public System.Threading.Tasks.Task ReloadAsync() => LoadPageAsync();

    /// <summary>Synchronously releases the cached composer's Win2D, timer, audio, and speech resources.</summary>
    public void DisarmForShutdown()
    {
        Vm.Cleanup();
        Composer.Dispose();
    }

    private async System.Threading.Tasks.Task LoadPageAsync()
    {
        await Vm.LoadAsync();

        // Rapid switching can navigate away — and unload this cached page — during the await
        // above. Touching the composer's dependency properties while it is detached from the
        // visual tree throws E_UNEXPECTED (0x8000FFFF). Bail if we are no longer loaded; the next
        // OnLoaded applies the current selection.
        if (!IsLoaded) return;

        Composer.CanSubmit = Vm.CanStartConversation;
        Composer.FocusInput();
    }

    // This composer starts a fresh conversation before navigating to ChatPage.
    // The whole submission (text + attachments) is carried across the
    // navigation via the Frame parameter so an image/file sent from here
    // behaves exactly like one sent from ChatPage's own composer: it creates
    // the session, navigates, and is actually sent as the first turn.
    private async void Composer_SubmitRequested(object? sender, ComposerSubmission submission)
        => await StartConversationAsync(submission);

    private async void OfflineNotice_RecheckRequested(object? sender, EventArgs e)
        => await Vm.RecheckPresenceAsync();

    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string prompt)
            Composer.SetDraft(prompt);
    }

    private void ContentScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        => ContentLayout.MinHeight = Math.Max(
            0,
            e.NewSize.Height - ContentScroll.Padding.Top - ContentScroll.Padding.Bottom);

    private async System.Threading.Tasks.Task StartConversationAsync(ComposerSubmission submission)
    {
        var initialPrompt = submission.Text.Trim();
        var hasAttachments = submission.Attachments.Count > 0;
        if (initialPrompt.Length == 0 && !hasAttachments) return;

        if (await Vm.StartConversationAsync(initialPrompt, hasAttachments))
        {
            Frame.Navigate(
                typeof(ChatPage),
                new ComposerSubmission(initialPrompt, submission.Attachments),
                new DrillInNavigationTransitionInfo());
        }
    }
}
