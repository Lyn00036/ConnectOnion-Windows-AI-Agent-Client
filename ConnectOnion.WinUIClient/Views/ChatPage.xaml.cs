using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient;
using ConnectOnion.WinUIClient.Controls;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.ViewModels;
using FluentIcons.Common;
using FluentIcons.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class ChatPage : Page, IFindHost, IReloadablePage
{
    /// <summary>Width of the centred conversation column; see the ChatColumnWidth resource in App.xaml.</summary>
    private static double ChatColumnMaxWidth => (double)Application.Current.Resources["ChatColumnWidth"];

    /// <summary>Persisted scroll positions keyed by session id, survives page recreation.
    /// Bounded — see <see cref="SaveScrollPosition"/> for why an unrestored entry would
    /// otherwise stay forever.</summary>
    private static readonly ConcurrentDictionary<string, double> ScrollPositions = new();
    private static readonly ConcurrentQueue<string> ScrollPositionOrder = new();
    private const int MaxRememberedScrollPositions = 64;

    public ChatViewModel Vm { get; } = App.GetService<ChatViewModel>();
    private ScrollViewer? _messageScrollViewer;
    private bool _isLoadingConversation;
    private bool _isAutoScrollQueued;
    private bool _stickToBottom = true;
    private ComposerSubmission? _pendingInitialSubmission;
    private MainWindow? _hostWindow;
    private CancellationTokenSource? _loadCts;

    public ChatPage()
    {
        InitializeComponent();

        // Reuse this one instance across navigations instead of rebuilding the whole page —
        // XAML tree, ChatComposer's Win2D canvas, view model, bindings — every time the user
        // crosses page types (e.g. AgentDetailPage → ChatPage). Without this the same-type
        // reuse in MainWindow.NavigateTo only covers chat→chat, so an agent→chat switch built a
        // fresh tree each click and the discarded ones piled up. The Load/Unload wiring below
        // is symmetric so a reused instance behaves like a fresh one.
        NavigationCacheMode = NavigationCacheMode.Required;

        Vm.Messages.CollectionChanged += Messages_CollectionChanged;
        Vm.PropertyChanged += Vm_PropertyChanged;
        // Permanent, like the two above: this is the single reused instance (the constructor never
        // runs again), so one hook is correct and OnUnloaded deliberately leaves it attached.
        Vm.SendCancelled += Vm_SendCancelled;
        Vm.ConversationChanged += Vm_ConversationChanged;
        _findDebounceTimer.Tick += FindDebounceTimer_Tick;
        ChatRoot.KeyDown += ChatRoot_KeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loadCts?.Cancel();
        // No longer viewing this conversation — clear it so notifications resume.
        ReportViewing(active: false);

        // Persist scroll position so returning to this conversation restores it.
        SaveScrollPosition();

        _findDebounceTimer.Stop();
        // The elapsed-seconds ticker lives on the view model, which outlives this handler (the
        // page is cached and reused), so it has to be disarmed explicitly here too.
        Vm.StopThinkingTicker();

        // The page is cached and reused (NavigationCacheMode.Required above), so only the
        // per-view work is torn down here: drop the live run subscription so a backgrounded page
        // stops projecting into a list nobody is looking at — LoadAsync re-subscribes on return.
        // The presence hook and Vm.PropertyChanged handler are left attached on purpose: this is
        // the single, reused instance (the constructor never runs again), so one permanent hook
        // is correct, not a leak. Detaching them here would leave them dead after the first
        // round-trip.
        try { Vm.DetachRun(); }
        catch { /* Best-effort cleanup. */ }
    }

    /// <summary>Reports to <c>WindowPresenceService</c> whether this window is
    /// currently showing this conversation, so the notification policy can suppress
    /// notifications the user would already see. The host window is captured while
    /// loaded so it can still be cleared during unload (when XamlRoot may be gone).</summary>
    private void ReportViewing(bool active)
    {
        try
        {
            if (active)
            {
                _hostWindow = MainWindow.FromXamlRoot(XamlRoot);
                if (_hostWindow is not null)
                    AppServices.WindowPresence.SetViewing(_hostWindow, Vm.AgentId, Vm.SessionId);
            }
            else
            {
                if (_hostWindow is not null)
                    AppServices.WindowPresence.SetViewing(_hostWindow, null, null);
                _hostWindow = null;
            }
        }
        catch { /* Best-effort presence reporting. */ }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Vm.CanSend) or nameof(Vm.IsProcessing))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Composer.CanSubmit = Vm.CanSend;
                Composer.IsBusy = Vm.IsProcessing;
            });
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // AgentDetailPage navigates here with a ComposerSubmission (text + any
        // attachments); a plain string is also accepted for robustness against
        // any other caller that only ever had a prompt to pass.
        _pendingInitialSubmission = e.Parameter switch
        {
            ComposerSubmission submission => submission,
            string prompt => new ComposerSubmission(prompt, Array.Empty<PendingAttachment>()),
            _ => null,
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _messageScrollViewer ??= FindDescendant<ScrollViewer>(MessageList);
        if (_messageScrollViewer is not null)
        {
            _messageScrollViewer.ViewChanged -= MessageScrollViewer_ViewChanged;
            _messageScrollViewer.ViewChanged += MessageScrollViewer_ViewChanged;
        }

        await LoadConversationAsync();
    }

    /// <summary>
    /// Re-points this page at whichever conversation is now active, without rebuilding the page
    /// (see <see cref="IFindHost"/>'s neighbour, <see cref="IReloadablePage"/>, for why).
    /// </summary>
    public async System.Threading.Tasks.Task ReloadAsync()
    {
        SaveScrollPosition();
        _pendingInitialSubmission = null;
        _stickToBottom = true;
        ScrollToLatestButton.Visibility = Visibility.Collapsed;
        LoadingOverlay.Visibility = Visibility.Visible;
        await LoadConversationAsync();
    }

    /// <summary>Remembers where the *outgoing* conversation was scrolled to, so coming back to
    /// it lands in the same place. Keyed by session id, consumed (and removed) on restore.
    /// <para>Entries are only removed on restore, and a conversation the user leaves and never
    /// returns to is never restored — so the map is FIFO-capped rather than left to grow one
    /// entry per conversation visited. Losing the oldest offset just means that conversation
    /// opens at the bottom, which is where a chat opens by default anyway.</para></summary>
    private void SaveScrollPosition()
    {
        try
        {
            if (Vm.SessionId is not { } sid || _messageScrollViewer is null) return;

            if (ScrollPositions.TryAdd(sid, _messageScrollViewer.VerticalOffset))
                ScrollPositionOrder.Enqueue(sid);
            else
                ScrollPositions[sid] = _messageScrollViewer.VerticalOffset;

            while (ScrollPositionOrder.Count > MaxRememberedScrollPositions
                && ScrollPositionOrder.TryDequeue(out var oldest))
            {
                ScrollPositions.TryRemove(oldest, out _);
            }
        }
        catch { /* Best-effort scroll persistence. */ }
    }

    private async System.Threading.Tasks.Task LoadConversationAsync()
    {
        _loadCts?.Cancel();
        var loadCts = new CancellationTokenSource();
        _loadCts = loadCts;
        _isLoadingConversation = true;
        try
        {
            await Vm.LoadAsync(loadCts.Token);
        }
        catch (OperationCanceledException) when (loadCts.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            // Failed to restore the conversation (e.g. corrupted local
            // storage); the page still loads in an empty state so the user
            // can start a fresh chat.
        }
        finally
        {
            _isLoadingConversation = false;
            if (ReferenceEquals(_loadCts, loadCts)) _loadCts = null;
            loadCts.Dispose();
        }

        // Rapid switching can navigate away — and unload this cached page — during the DB load
        // above. Touching the composer/overlay dependency properties or the message list while
        // this page is detached from the visual tree throws E_UNEXPECTED (0x8000FFFF). Bail if we
        // are no longer loaded; the next OnLoaded re-runs against the current selection.
        if (!IsLoaded) return;

        // Tell the notification layer this window is now showing this conversation,
        // so a reply that lands here is suppressed (the user already sees it).
        ReportViewing(active: true);

        // The page is cached and reused across conversations. A previous empty
        // conversation may have left this panel visible, while history changes
        // raised during LoadAsync are intentionally ignored by
        // Messages_CollectionChanged. Always derive the final state from the
        // conversation that just finished loading instead of only handling the
        // empty case.
        EmptyStatePanel.Visibility = Vm.Messages.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Wait for the ListView to materialize its virtualized item containers
        // before removing the overlay.  A fixed delay cannot account for
        // varying message counts and hardware speed — use ContainerContentChanging
        // to detect when visible items are actually realized.
        _ = DismissOverlayWhenItemsReadyAsync();
        Composer.CanSubmit = Vm.CanSend;
        Composer.IsBusy = Vm.IsProcessing;
        Composer.FocusInput();

        var pending = _pendingInitialSubmission;
        if (pending is not null && (!string.IsNullOrWhiteSpace(pending.Text) || pending.Attachments.Count > 0))
        {
            _pendingInitialSubmission = null;
            await Vm.SendAsync(pending.Text, pending.Attachments);
            QueueScrollToEnd(force: true);
        }
    }

    private async void Composer_SubmitRequested(object? sender, ComposerSubmission submission)
        => await SendAsync(submission.Text, submission.Attachments);

    /// <summary>
    /// One button, two meanings. While the message is still queued it has not reached the agent,
    /// so Stop takes it back outright; once the agent has it, Stop is the cooperative request the
    /// agent may decline. The composer only reports the press — deciding which of the two applies
    /// is the page's job, because only the run's state can answer it.
    /// </summary>
    private async void Composer_StopRequested(object? sender, System.EventArgs e)
    {
        if (Vm.CanCancelSend)
        {
            await Vm.CancelSendAsync();
            return;
        }
        await Vm.StopAsync();
    }

    /// <summary>Puts a taken-back message back on the composer.</summary>
    private async void Vm_SendCancelled(string text, IReadOnlyList<PendingAttachment> attachments)
        => await Composer.RestoreDraftAsync(text, attachments);

    /// <summary>
    /// The page is now showing a different conversation than the one it loaded (New / Edit /
    /// Retry all branch in place, without navigating), so re-report presence against the new id.
    /// Without this the policy keeps matching against the conversation we left, and a turn the
    /// user is watching notifies as though they were somewhere else.
    /// </summary>
    private void Vm_ConversationChanged()
    {
        // Only meaningful while attached: ReportViewing resolves the host window through XamlRoot,
        // and a detached page has none. OnLoaded reports again anyway when it comes back.
        if (IsLoaded) ReportViewing(active: true);
    }

    private async void Composer_ModeChangeRequested(object? sender, string mode)
        => await Vm.SetModeAsync(mode);

    private async void OfflineNotice_RecheckRequested(object? sender, System.EventArgs e)
        => await Vm.RecheckPresenceAsync();

    private async void SubmitOnboard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ChatMessage message)
        {
            await Vm.SubmitOnboardInviteCodeAsync(message);
            ScrollToEnd();
        }
    }



}
