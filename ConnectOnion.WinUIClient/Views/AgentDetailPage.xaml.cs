using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Controls;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class AgentDetailPage : Page, IReloadablePage, IShutdownDisarmable
{
    public AgentDetailViewModel Vm { get; } =
        App.GetService<AgentDetailViewModel>();

    public AgentDetailPage()
    {
        InitializeComponent();

        // Reuse this one instance across navigations rather than rebuilding it — this page also
        // hosts a ChatComposer (Win2D canvas) — every time the user crosses page types (e.g.
        // ChatPage → AgentDetailPage). MainWindow.NavigateTo's same-type reuse only covers
        // agent→agent, so a chat→agent switch was constructing and discarding a fresh tree per
        // click. The presence hook stays attached for this single reused instance (see OnLoaded);
        // there is no per-view state to tear down, so there is no Unloaded teardown.
        NavigationCacheMode =
            Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        await LoadPageAsync();
    }

    /// <summary>
    /// Re-points this page at whichever Agent is now selected without
    /// reconstructing the page or its ChatComposer.
    /// </summary>
    public Task ReloadAsync()
    {
        return LoadPageAsync();
    }

    /// <summary>
    /// Synchronously releases the cached composer's Win2D, timer, audio,
    /// and speech resources.
    /// </summary>
    public void DisarmForShutdown()
    {
        Vm.Cleanup();
        Composer.Dispose();
    }

    private async Task LoadPageAsync()
    {
        await Vm.LoadAsync();

        // Rapid switching can navigate away — and unload this cached page — during the await
        // above. Touching the composer's dependency properties while it is detached from the
        // visual tree throws E_UNEXPECTED (0x8000FFFF). Bail if we are no longer loaded; the next
        // OnLoaded applies the current selection.
        if (!IsLoaded)
        {
            return;
        }

        /*
         * Keep the original asynchronous icon-refresh sequence.
         *
         * The Agent details page only displays the icon. Changing and removing
         * an icon is handled exclusively by ShellSidebar's More actions menu.
         */
        await LoadCurrentAgentIconAsync();

        if (!IsLoaded)
        {
            return;
        }

        Composer.CanSubmit =
            Vm.CanStartConversation;

        Composer.FocusInput();

        // Account balance is useful secondary information, never a gate on the first interactive
        // frame. The resilient HTTP pipeline may retry, so refresh only after the composer works.
        _ = RefreshBalanceAsync();
    }

    // This composer starts a fresh conversation before navigating to ChatPage.
    // The whole submission (text + attachments) is carried across the
    // navigation via the Frame parameter so an image/file sent from here
    // behaves exactly like one sent from ChatPage's own composer: it creates
    // the session, navigates, and is actually sent as the first turn.
    private async void Composer_SubmitRequested(
        object? sender,
        ComposerSubmission submission)
    {
        await StartConversationAsync(
            submission);
    }

    private async void OfflineNotice_RecheckRequested(
        object? sender,
        EventArgs e)
    {
        await Vm.RecheckPresenceAsync();
    }

    private void Suggestion_Click(
        object sender,
        RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string prompt)
        {
            Composer.SetDraft(prompt);
        }
    }

    private async void Share_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!Vm.CanShareAgent ||
            string.IsNullOrWhiteSpace(Vm.AgentAddress))
        {
            return;
        }

        var dialog =
            new AgentShareDialog(Vm.AgentAddress)
            {
                XamlRoot = XamlRoot,
            };

        await dialog.ShowAsync();
    }

    private async Task RefreshBalanceAsync()
    {
        try
        {
            var profile =
                await AppServices.OpenOnionAccount.RefreshAsync();

            if (!IsLoaded)
            {
                return;
            }

            BalanceButton.Content =
                $"Balance ${profile.BalanceUsd:0.00} · Top up";

            BalanceButton.Visibility =
                Visibility.Visible;
        }
        catch
        {
            if (IsLoaded)
            {
                BalanceButton.Visibility =
                    Visibility.Collapsed;
            }
        }
    }

    private async void Balance_Click(
        object sender,
        RoutedEventArgs e)
    {
        var address =
            Uri.EscapeDataString(
                Vm.AgentAddress);

        await AppServices.UriLauncher.LaunchAsync(
            new Uri(
                $"https://o.openonion.ai/purchase?agent={address}"));
    }

    private async Task StartConversationAsync(
        ComposerSubmission submission)
    {
        var initialPrompt =
            submission.Text.Trim();

        var hasAttachments =
            submission.Attachments.Count > 0;

        if (initialPrompt.Length == 0 &&
            !hasAttachments)
        {
            return;
        }

        if (await Vm.StartConversationAsync(
                initialPrompt,
                hasAttachments))
        {
            Frame.Navigate(
                typeof(ChatPage),
                new ComposerSubmission(
                    initialPrompt,
                    submission.Attachments),
                new DrillInNavigationTransitionInfo());
        }
    }

    // ---------------------------------------------------------------------
    // Agent icon display only
    // ---------------------------------------------------------------------

    /// <summary>
    /// Loads the selected Agent from storage and displays its current icon.
    ///
    /// This page deliberately contains no icon picker, commit, remove, or
    /// cleanup operations. Those actions belong to ShellSidebar.
    /// </summary>
    private async Task LoadCurrentAgentIconAsync()
    {
        try
        {
            var selected =
                await LoadSelectedAgentAsync(
                    CancellationToken.None);

            if (!IsLoaded)
            {
                return;
            }

            ApplyAgentIconToUi(
                selected?.Agent.IconPath);
        }
        catch
        {
            if (!IsLoaded)
            {
                return;
            }

            // A failed icon read must not prevent the Agent page from loading.
            ApplyAgentIconToUi(null);
        }
    }

    /// <summary>
    /// Resolves the currently selected Agent from a freshly loaded repository
    /// state. The address is retained as a defensive fallback.
    /// </summary>
    private async Task<(AgentsState State, AgentConfig Agent)?>
        LoadSelectedAgentAsync(
            CancellationToken cancellationToken)
    {
        var state =
            await AppServices.Agents.LoadAsync(
                cancellationToken);

        AgentConfig? agent = null;

        if (!string.IsNullOrWhiteSpace(
                state.SelectedAgentId))
        {
            agent =
                state.Agents.FirstOrDefault(
                    candidate =>
                        string.Equals(
                            candidate.Id,
                            state.SelectedAgentId,
                            StringComparison.Ordinal));
        }

        if (agent is null &&
            !string.IsNullOrWhiteSpace(
                Vm.AgentAddress))
        {
            agent =
                state.Agents.FirstOrDefault(
                    candidate =>
                        string.Equals(
                            candidate.Address,
                            Vm.AgentAddress,
                            StringComparison.OrdinalIgnoreCase));
        }

        return agent is null
            ? null
            : (state, agent);
    }

    /// <summary>
    /// Updates only the Agent details avatar. The explicit reload handles the
    /// case where a sidebar icon operation overwrote the same deterministic
    /// file path.
    /// </summary>
    private void ApplyAgentIconToUi(
        string? iconPath)
    {
        if (!IsLoaded)
        {
            return;
        }

        AgentAvatarView.ImagePath =
            iconPath;

        AgentAvatarView.ReloadImage();
    }
}
