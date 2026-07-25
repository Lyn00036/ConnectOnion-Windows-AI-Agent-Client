using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Services.Attachments;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>Text plus whatever attachments were ready to send at submit time.</summary>
public sealed record ComposerSubmission(string Text, IReadOnlyList<PendingAttachment> Attachments);

public sealed partial class ChatComposer : UserControl, IDisposable
{

    public static readonly DependencyProperty CanSubmitProperty =
        DependencyProperty.Register(
            nameof(CanSubmit),
            typeof(bool),
            typeof(ChatComposer),
            new PropertyMetadata(true, OnCanSubmitChanged));

    public static readonly DependencyProperty EnterToSubmitProperty =
        DependencyProperty.Register(
            nameof(EnterToSubmit),
            typeof(bool),
            typeof(ChatComposer),
            new PropertyMetadata(true));

    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.Register(
            nameof(IsBusy),
            typeof(bool),
            typeof(ChatComposer),
            new PropertyMetadata(false, OnIsBusyChanged));

    public static readonly DependencyProperty AcceptedInputsProperty =
        DependencyProperty.Register(
            nameof(AcceptedInputs),
            typeof(AgentAcceptedInputs),
            typeof(ChatComposer),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ShowSuggestionsProperty =
        DependencyProperty.Register(
            nameof(ShowSuggestions),
            typeof(bool),
            typeof(ChatComposer),
            new PropertyMetadata(true, OnShowSuggestionsChanged));

    /// <summary>
    /// The agent's declared skills, from its <c>/info</c>. Set it and the composer offers them as
    /// slash-command completions; leave it null and nothing about the composer changes.
    /// </summary>
    public static readonly DependencyProperty SkillsProperty =
        DependencyProperty.Register(
            nameof(Skills),
            typeof(IReadOnlyList<SkillInfo>),
            typeof(ChatComposer),
            new PropertyMetadata(null, OnSkillsChanged));

    /// <summary>
    /// The running turn's spend, rendered beside the connection status. Empty collapses the slot.
    /// </summary>
    public static readonly DependencyProperty UsageTextProperty =
        DependencyProperty.Register(
            nameof(UsageText),
            typeof(string),
            typeof(ChatComposer),
            new PropertyMetadata("", OnUsageTextChanged));

    /// <summary>
    /// Ready-made prompts to offer above the input, replacing the generic starters. Separate from
    /// <see cref="Skills"/> because these are already-phrased sentences the caller chose (see
    /// <c>AgentSkills.BestOffers</c>), not names to complete.
    /// </summary>
    public static readonly DependencyProperty SuggestionPromptsProperty =
        DependencyProperty.Register(
            nameof(SuggestionPrompts),
            typeof(IReadOnlyList<string>),
            typeof(ChatComposer),
            new PropertyMetadata(null, OnShowSuggestionsChanged));

    public static readonly DependencyProperty ShowModeSelectorProperty =
        DependencyProperty.Register(
            nameof(ShowModeSelector),
            typeof(bool),
            typeof(ChatComposer),
            new PropertyMetadata(false, OnModeChanged));

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(
            nameof(Mode),
            typeof(string),
            typeof(ChatComposer),
            new PropertyMetadata(AgentModes.Safe, OnModeChanged));

    public static readonly DependencyProperty IsStoppingProperty =
        DependencyProperty.Register(
            nameof(IsStopping),
            typeof(bool),
            typeof(ChatComposer),
            new PropertyMetadata(false, OnIsBusyChanged));

    public static readonly DependencyProperty CanStopProperty =
        DependencyProperty.Register(
            nameof(CanStop),
            typeof(bool),
            typeof(ChatComposer),
            new PropertyMetadata(false, OnIsBusyChanged));

    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(
            nameof(PlaceholderText),
            typeof(string),
            typeof(ChatComposer),
            new PropertyMetadata("Write a message..."));

    public static readonly DependencyProperty ShowConnectionStatusProperty =
        DependencyProperty.Register(
            nameof(ShowConnectionStatus),
            typeof(bool),
            typeof(ChatComposer),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ConnectionPhaseProperty =
        DependencyProperty.Register(
            nameof(ConnectionPhase),
            typeof(ConnectOnion.WinUIClient.Models.ConnectionPhase),
            typeof(ChatComposer),
            new PropertyMetadata(ConnectOnion.WinUIClient.Models.ConnectionPhase.Idle));

    public static readonly DependencyProperty ConnectionStatusTextProperty =
        DependencyProperty.Register(
            nameof(ConnectionStatusText),
            typeof(string),
            typeof(ChatComposer),
            new PropertyMetadata(""));

    public event EventHandler<ComposerSubmission>? SubmitRequested;

    /// <summary>The stop button was pressed. The host decides what that can achieve — the composer
    /// only reports the press, and re-renders from <see cref="IsStopping"/> when told to.</summary>
    public event EventHandler? StopRequested;

    /// <summary>The user picked an approval mode from the pill. Carries the wire value
    /// (<see cref="AgentModes"/>), not the menu label.</summary>
    public event EventHandler<string>? ModeChangeRequested;

    /// <summary>Attachments picked but not yet sent. Cleared (for the ones actually sent) on submit.</summary>
    public ObservableCollection<PendingAttachment> PendingAttachments { get; } = new();

    private CancellationTokenSource _lifetimeCts = new();
    private int _disposed;

    public ChatComposer()
    {
        InitializeComponent();
        ApplyWaveformTheme();
        _waveformTimer.Tick += WaveformTimer_Tick;
        _recordingElapsedTimer.Tick += RecordingElapsedTimer_Tick;
        ActualThemeChanged += Composer_ActualThemeChanged;
        Loaded += Composer_Loaded;
        Unloaded += Composer_Unloaded;
        PendingAttachments.CollectionChanged += PendingAttachments_CollectionChanged;
        RefreshSendButtonState();
    }

    /// <summary>Starts a fresh lifetime for work initiated while this composer is loaded.</summary>
    private void Composer_Loaded(object sender, RoutedEventArgs e)
    {
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        _lifetimeCts = new CancellationTokenSource();
    }

    private void Composer_ActualThemeChanged(FrameworkElement sender, object args)
        => ApplyWaveformTheme();

    private void PendingAttachments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AttachmentsScrollViewer.Visibility = PendingAttachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshSendButtonState();
    }

    public bool CanSubmit
    {
        get => (bool)GetValue(CanSubmitProperty);
        set => SetValue(CanSubmitProperty, value);
    }

    public bool EnterToSubmit
    {
        get => (bool)GetValue(EnterToSubmitProperty);
        set => SetValue(EnterToSubmitProperty, value);
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    /// <summary>The connected agent's advertised input capabilities, from <c>/info</c>. Null while unknown (not yet fetched, or offline) — attachments are not blocked in that case.</summary>
    public AgentAcceptedInputs? AcceptedInputs
    {
        get => (AgentAcceptedInputs?)GetValue(AcceptedInputsProperty);
        set => SetValue(AcceptedInputsProperty, value);
    }

    /// <summary>
    /// Whether the "What can you do?" / "Show system info" / "List files" quick
    /// prompt chips appear above the input box. True by default (the agent-detail
    /// page's first-message composer); ChatPage sets this false since those
    /// starter prompts don't make sense once a conversation is already underway.
    /// </summary>
    public bool ShowSuggestions
    {
        get => (bool)GetValue(ShowSuggestionsProperty);
        set => SetValue(ShowSuggestionsProperty, value);
    }

    /// <summary>The agent's declared skills, offered as <c>/</c> completions. Null or empty leaves
    /// the composer exactly as it behaves without a palette.</summary>
    public IReadOnlyList<SkillInfo>? Skills
    {
        get => (IReadOnlyList<SkillInfo>?)GetValue(SkillsProperty);
        set => SetValue(SkillsProperty, value);
    }

    /// <summary>Prompts shown as chips above the input in place of the generic starters. Empty or
    /// null falls back to those starters.</summary>
    public IReadOnlyList<string>? SuggestionPrompts
    {
        get => (IReadOnlyList<string>?)GetValue(SuggestionPromptsProperty);
        set => SetValue(SuggestionPromptsProperty, value);
    }

    /// <summary>The running turn's spend ("3.1K→157 tok · ctx 1.3% · 1 tools"). Empty hides it.</summary>
    public string UsageText
    {
        get => (string)GetValue(UsageTextProperty);
        set => SetValue(UsageTextProperty, value);
    }

    /// <summary>Whether there is anything to show. A separate property rather than a
    /// string-to-visibility converter so the slot's rule lives beside the value it describes.</summary>
    public bool HasUsageText => !string.IsNullOrEmpty(UsageText);

    private static void OnUsageTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // HasUsageText is derived, and a plain CLR property raises nothing on its own — without
        // this the slot would never appear after the first usage report.
        if (d is ChatComposer composer) composer.Bindings.Update();
    }

    private static void OnSkillsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Skills usually arrive after the page loads (the /info fetch is async), so a draft the
        // user already started typing has to be re-evaluated rather than waiting for the next
        // keystroke to open the palette.
        if (d is ChatComposer composer) composer.RefreshSkillPalette();
    }

    /// <summary>Whether the approval-mode pill is shown. Off by default: it belongs to an ongoing
    /// conversation (ChatPage), not to the agent-detail page's one-shot first message.</summary>
    public bool ShowModeSelector
    {
        get => (bool)GetValue(ShowModeSelectorProperty);
        set => SetValue(ShowModeSelectorProperty, value);
    }

    /// <summary>The conversation's current approval mode, one of <see cref="AgentModes"/>.</summary>
    public string Mode
    {
        get => (string)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    /// <summary>A graceful stop was sent and the current iteration is still finishing.</summary>
    public bool IsStopping
    {
        get => (bool)GetValue(IsStoppingProperty);
        set => SetValue(IsStoppingProperty, value);
    }

    /// <summary>INPUT has reached the socket and the turn has not reached OUTPUT/ERROR.</summary>
    public bool CanStop
    {
        get => (bool)GetValue(CanStopProperty);
        set => SetValue(CanStopProperty, value);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>Whether the compact connection indicator is rendered inside the composer.</summary>
    public bool ShowConnectionStatus
    {
        get => (bool)GetValue(ShowConnectionStatusProperty);
        set => SetValue(ShowConnectionStatusProperty, value);
    }

    /// <summary>The current transport phase used to color the composer's status indicator.</summary>
    public ConnectionPhase ConnectionPhase
    {
        get => (ConnectionPhase)GetValue(ConnectionPhaseProperty);
        set => SetValue(ConnectionPhaseProperty, value);
    }

    /// <summary>The user-facing transport status shown beside the indicator.</summary>
    public string ConnectionStatusText
    {
        get => (string)GetValue(ConnectionStatusTextProperty);
        set => SetValue(ConnectionStatusTextProperty, value);
    }

    public void FocusInput()
        => InputBox.Focus(FocusState.Programmatic);

    /// <summary>Replaces the current draft and focuses the editor without submitting it.</summary>
    public void SetDraft(string text)
    {
        InputBox.Text = text;
        InputBox.SelectionStart = InputBox.Text.Length;
        RefreshSendButtonState();
        FocusInput();
    }

    /// <summary>
    /// Puts a submitted message back on the composer after the send was cancelled before it
    /// reached the agent.
    ///
    /// <para>Attachments are re-encoded rather than re-attached as they were: the send path nulls
    /// each <see cref="PendingAttachment.DataUrl"/> the moment the payload is handed to the wire
    /// lists (so a megabyte of base64 does not linger on a draft), which leaves the drafts holding
    /// metadata and a local path only. Reading the file again is what makes them sendable a second
    /// time. Anything whose file has since gone is dropped rather than restored as a chip that
    /// would fail on submit.</para>
    ///
    /// <para>Appends to whatever is already there instead of replacing it — the user may well have
    /// started typing again in the moment between submitting and cancelling, and that text is
    /// theirs, not ours to discard.</para>
    /// </summary>
    public async Task RestoreDraftAsync(string text, IReadOnlyList<PendingAttachment> attachments)
    {
        if (!string.IsNullOrEmpty(text))
        {
            InputBox.Text = InputBox.Text.Length == 0 ? text : text + "\n" + InputBox.Text;
            InputBox.SelectionStart = InputBox.Text.Length;
        }

        foreach (var attachment in attachments)
        {
            if (string.IsNullOrEmpty(attachment.LocalPath) || !File.Exists(attachment.LocalPath)) continue;
            try
            {
                attachment.DataUrl = await AttachmentEncoder
                    .EncodeToDataUrlAsync(attachment.LocalPath, attachment.MimeType, _lifetimeCts.Token)
                    .ConfigureAwait(true);
                attachment.Status = AttachmentStatus.Ready;
                PendingAttachments.Add(attachment);
            }
            catch (Exception ex)
            {
                // A file that has moved or become unreadable simply does not come back. Restoring
                // it as a chip that cannot be sent would be worse than losing it quietly here.
                Debug.WriteLine($"[composer] could not restore attachment {attachment.FileName}: {ex}");
            }
        }

        RefreshSendButtonState();
        FocusInput();
    }

    private static void OnCanSubmitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ChatComposer)d).RefreshSendButtonState();

    private static void OnIsBusyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ChatComposer)d).RefreshSendButtonState();

    private static void OnShowSuggestionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var composer = (ChatComposer)d;
        composer.ApplySuggestionVisibility(composer.ActualWidth);
    }

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ChatComposer)d).RefreshModeSelector();

    private void RefreshModeSelector()
    {
        ModeButton.Visibility = ShowModeSelector ? Visibility.Visible : Visibility.Collapsed;
        if (!ShowModeSelector) return;

        var mode = Mode;
        ModeLabel.Text = AgentModes.DisplayName(mode);
        // Radio state is driven from the property, never from the click: the agent can change mode
        // on its own (enter_plan_mode), and when it does the pill has to follow it rather than keep
        // showing whatever the user last picked.
        ModeSafeItem.IsChecked = mode == AgentModes.Safe;
        ModeAcceptEditsItem.IsChecked = mode == AgentModes.AcceptEdits;
        ModePlanItem.IsChecked = mode == AgentModes.Plan;
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string mode || mode == Mode)
        {
            RefreshModeSelector();
            return;
        }
        // Not applied locally: the page owns the mode and will set it back on us once it has been
        // persisted and pushed, so the pill can never claim a mode the agent was never told about.
        ModeChangeRequested?.Invoke(this, mode);
        RefreshModeSelector();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStop || IsStopping) return;
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Picks which suggestion row is on screen: the agent's own skill offers when it has any,
    /// otherwise the generic starters. Never both — they answer the same question, and two rows of
    /// chips above an empty input reads as a menu rather than a nudge.
    ///
    /// <para>The 620px floor is unchanged: below it the chips wrap into a second row and push the
    /// input down, which costs more than the suggestion is worth.</para>
    /// </summary>
    private void ApplySuggestionVisibility(double width)
    {
        var room = ShowSuggestions && width >= 620;
        var offers = SuggestionPrompts;
        var hasOffers = offers is { Count: > 0 };

        SkillSuggestionList.ItemsSource = hasOffers ? offers : null;
        SkillSuggestionList.Visibility = room && hasOffers ? Visibility.Visible : Visibility.Collapsed;
        SuggestionPanel.Visibility = room && !hasOffers ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (IsRecordingActive)
            await StopSpeechRecognitionAsync();
        SubmitCurrentText();
    }

    private async void Composer_Unloaded(object sender, RoutedEventArgs e)
    {
        // Cancel, don't Dispose: the page uses NavigationCacheMode.Required so this
        // composer is reused on re-entry, and Dispose would destroy _lifetimeCts
        // permanently — every operation using its Token (file picker, drag-drop,
        // attachment encoding) would throw ObjectDisposedException and silently fail.
        _lifetimeCts.Cancel();
        _waveformTimer.Stop();
        await StopSpeechRecognitionAsync();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        Unloaded -= Composer_Unloaded;
        Loaded -= Composer_Loaded;
        ActualThemeChanged -= Composer_ActualThemeChanged;
        PendingAttachments.CollectionChanged -= PendingAttachments_CollectionChanged;
        _waveformTimer.Tick -= WaveformTimer_Tick;
        _waveformTimer.Stop();
        _recordingElapsedTimer.Tick -= RecordingElapsedTimer_Tick;
        _recordingElapsedTimer.Stop();

        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        _recordingCts?.Cancel();
        _recordingCts?.Dispose();
        _recordingCts = null;

        DetachSpeechRecognizerEvents();
        _speechRecognizer?.Dispose();
        _speechRecognizer = null;
        StopAudioMeter();
        WaveformCanvas.Children.Clear();
        _waveformBars.Clear();
    }

    // ---- Attachments ----

    // Built once and reused. Rebuilding a MenuFlyout (with fresh lambda Click handlers) on every
    // click leaked: WinUI 3 does not reliably release a Popup/MenuFlyout once shown, so a new one
    // per open accumulated for the process's life. The two items' enabled state is the only thing
    // that varies per open (with the agent's accepted inputs), so that is refreshed in place.
    private MenuFlyout? _attachmentFlyout;
    private MenuFlyoutItem? _addImageItem;
    private MenuFlyoutItem? _addFileItem;

    private void AddAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (_attachmentFlyout is null)
        {
            _addImageItem = new MenuFlyoutItem { Text = "Add image..." };
            _addImageItem.Click += async (_, _) => await AddAttachmentsAsync(AttachmentKind.Image);

            _addFileItem = new MenuFlyoutItem { Text = "Add file..." };
            _addFileItem.Click += async (_, _) => await AddAttachmentsAsync(AttachmentKind.File);

            _attachmentFlyout = new MenuFlyout();
            _attachmentFlyout.Items.Add(_addImageItem);
            _attachmentFlyout.Items.Add(_addFileItem);
        }

        var accepted = AcceptedInputs;

        var imagesBlocked = accepted?.Images == false;
        _addImageItem!.IsEnabled = !imagesBlocked;
        ToolTipService.SetToolTip(_addImageItem, imagesBlocked ? "This agent does not accept image input." : null);

        var filesBlocked = accepted is not null && accepted.Files is null;
        _addFileItem!.IsEnabled = !filesBlocked;
        ToolTipService.SetToolTip(_addFileItem, filesBlocked ? "This agent does not accept file input." : null);

        _attachmentFlyout.ShowAt((FrameworkElement)sender);
    }

    private async Task AddAttachmentsAsync(AttachmentKind kind)
    {
        IReadOnlyList<PendingAttachment> picked;
        try
        {
            picked = await AttachmentPickerService.PickAsync(kind, _lifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Attachment] pick failed kind={kind} error={ex.GetType().Name}");
            return;
        }

        // Empty result means the user dismissed the picker — a normal outcome, not an error.
        AddAttachments(picked);
    }

    /// <summary>
    /// Validates each candidate and kicks off encoding for the ones that pass. Shared by the
    /// file picker and drag-and-drop, so both routes get identical capability gating — the
    /// only difference between them is how the <see cref="PendingAttachment"/> list was
    /// produced. Rejected candidates are still added to the rail (as Failed) so the user sees
    /// *why* a file didn't take.
    /// </summary>
    private void AddAttachments(IReadOnlyList<PendingAttachment> candidates)
    {
        foreach (var attachment in candidates)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Attachment] selected name={attachment.FileName} mime={attachment.MimeType} size={attachment.SizeBytes}");

            // Counted per-kind and re-read each iteration: a mixed drop of images and files
            // is checked against each kind's own count limit, including the ones just added.
            var existingCount = PendingAttachments.Count(p => p.Kind == attachment.Kind);
            var error = AttachmentValidationService.Validate(attachment, AcceptedInputs, existingCount);

            PendingAttachments.Add(attachment);

            if (error is not null)
            {
                attachment.Status = AttachmentStatus.Failed;
                attachment.Error = error;
                System.Diagnostics.Debug.WriteLine($"[Attachment] rejected name={attachment.FileName} reason={error}");
                continue;
            }

            _ = EncodeAttachmentAsync(attachment);
        }
    }

    // ---- Drag and drop ----

    private void Composer_DragEnter(object sender, DragEventArgs e)
        => UpdateDragFeedback(e);

    private void Composer_DragOver(object sender, DragEventArgs e)
        => UpdateDragFeedback(e);

    private void Composer_DragLeave(object sender, DragEventArgs e)
        => DropOverlay.Visibility = Visibility.Collapsed;

    /// <summary>
    /// Runs on every pointer move during a drag, so it stays synchronous — inspecting the
    /// payload's *format* is cheap, whereas actually materializing the storage items is not
    /// and is deferred to <see cref="Composer_Drop"/>.
    /// </summary>
    private void UpdateDragFeedback(DragEventArgs e)
    {
        if (!CanAcceptDrop(e.DataView))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            DropOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is { } dragUi)
        {
            dragUi.Caption = "Attach";
            dragUi.IsCaptionVisible = true;
            dragUi.IsGlyphVisible = false;
        }
        DropOverlay.Visibility = Visibility.Visible;

        // Claims the drag: the event still bubbles to MainWindow (which listens with
        // handledEventsToo), where Handled is the signal to drop the window-wide "drop it on
        // the message box" hint now that the pointer has reached the real target.
        e.Handled = true;
    }

    /// <summary>
    /// Whether the payload is droppable at all. Deliberately does not check per-file
    /// capability or type — the dropped files aren't readable from here — so an unsupported
    /// file still lands in the rail and gets a visible rejection from
    /// <see cref="AttachmentValidationService"/> rather than being refused by a cursor the
    /// user can't interrogate.
    /// </summary>
    private bool CanAcceptDrop(DataPackageView data)
        => CanSubmit && !IsBusy && !IsRecordingActive && AttachmentDropService.ContainsFiles(data);

    private async void Composer_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!CanAcceptDrop(e.DataView)) return;

        // The drag source stays blocked until the deferral completes, so it must be released
        // on every path out of this handler.
        var deferral = e.GetDeferral();
        try
        {
            var dropped = await AttachmentDropService.ExtractAsync(e.DataView, _lifetimeCts.Token);
            AddAttachments(dropped);
        }
        catch (OperationCanceledException)
        {
            // Composer unloaded mid-drop; the attachments are being discarded anyway.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Attachment] drop failed error={ex.GetType().Name}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task EncodeAttachmentAsync(PendingAttachment attachment)
    {
        attachment.Status = AttachmentStatus.Encoding;
        try
        {
            var dataUrl = await AttachmentEncoder
                .EncodeToDataUrlAsync(attachment.LocalPath, attachment.MimeType, _lifetimeCts.Token)
                .ConfigureAwait(true);
            attachment.DataUrl = dataUrl;
            attachment.Status = AttachmentStatus.Ready;
            System.Diagnostics.Debug.WriteLine($"[Attachment] encoded name={attachment.FileName} bytes={attachment.SizeBytes}");
        }
        catch (OperationCanceledException)
        {
            // Control unloaded mid-encode; the attachment is being discarded anyway.
        }
        catch (Exception ex)
        {
            attachment.Status = AttachmentStatus.Failed;
            attachment.Error = "Could not read this file.";
            System.Diagnostics.Debug.WriteLine($"[Attachment] encode failed name={attachment.FileName} error={ex.GetType().Name}");
        }
        RefreshSendButtonState();
    }

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PendingAttachment attachment)
        {
            PendingAttachments.Remove(attachment);
        }
    }


    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string prompt) return;
        SetDraft(prompt);
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshSendButtonState();
        RefreshSkillPalette();
    }

    private void InputBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // The palette gets first refusal on every key it uses. It has to: while it is open, Enter
        // means "complete this command", and letting the submit rule below see that key first
        // would send "/pos" as a message the moment the user tried to accept "/post".
        if (IsSkillPaletteOpen && HandleSkillPaletteKey(e.Key)) { e.Handled = true; return; }

        var isShiftDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (EnterToSubmit && e.Key == VirtualKey.Enter && !isShiftDown)
        {
            e.Handled = true;
            SubmitCurrentText();
        }
    }

    // ---- Slash-command palette ---------------------------------------------
    //
    // The agent's declared skills, offered as completions while the user is typing a "/" command.
    // The matching rules live in Core (AgentSkills) because they are the testable half; this is
    // only the keyboard and visibility plumbing.

    private bool IsSkillPaletteOpen => SkillPalette.Visibility == Visibility.Visible;

    /// <summary>Opens, filters or closes the palette for the current draft text.</summary>
    private void RefreshSkillPalette()
    {
        if (Skills is not { Count: > 0 } skills
            || !AgentSkills.TryGetSlashQuery(InputBox.Text, out var query))
        {
            CloseSkillPalette();
            return;
        }

        var matches = AgentSkills.Match(skills, query);
        if (matches.Count == 0)
        {
            // A query that matches nothing closes the palette rather than showing an empty box:
            // an empty popup over the input says less than the input itself, and keeping it open
            // would go on stealing Enter for a command that does not exist.
            CloseSkillPalette();
            return;
        }

        SkillList.ItemsSource = matches;
        // Reset to the top on every keystroke: the list has just been re-ranked, so a preserved
        // index would point at whatever happens to sit in that slot now.
        SkillList.SelectedIndex = 0;
        SkillPalette.Visibility = Visibility.Visible;
    }

    private void CloseSkillPalette()
    {
        if (SkillPalette.Visibility == Visibility.Collapsed) return;
        SkillPalette.Visibility = Visibility.Collapsed;
        // Dropped rather than left in place: the list holds the agent's skills, and a closed
        // palette has no reason to keep a realized item container per skill alive.
        SkillList.ItemsSource = null;
    }

    /// <summary>Handles the keys the palette owns while it is open. Returns false for anything
    /// else, so ordinary typing (and the Enter-to-submit rule) is untouched.</summary>
    private bool HandleSkillPaletteKey(VirtualKey key)
    {
        var count = SkillList.Items.Count;
        if (count == 0) return false;

        switch (key)
        {
            case VirtualKey.Down:
                SkillList.SelectedIndex = (SkillList.SelectedIndex + 1) % count;
                SkillList.ScrollIntoView(SkillList.SelectedItem);
                return true;

            case VirtualKey.Up:
                SkillList.SelectedIndex = (SkillList.SelectedIndex - 1 + count) % count;
                SkillList.ScrollIntoView(SkillList.SelectedItem);
                return true;

            case VirtualKey.Tab:
            case VirtualKey.Enter:
                CompleteSkill(SkillList.SelectedItem as SkillInfo);
                return true;

            case VirtualKey.Escape:
                // Closes the palette and leaves the text alone. The reference client clears the
                // draft here; discarding what someone typed because they dismissed a popup is a
                // worse trade than making them press Backspace.
                CloseSkillPalette();
                return true;

            default:
                return false;
        }
    }

    private void SkillList_ItemClick(object sender, ItemClickEventArgs e)
        => CompleteSkill(e.ClickedItem as SkillInfo);

    /// <summary>Completes the draft to <c>/name </c> — with the trailing space, which is what
    /// closes the palette (see <c>AgentSkills.TryGetSlashQuery</c>) and puts the caret where the
    /// command's arguments go.</summary>
    private void CompleteSkill(SkillInfo? skill)
    {
        if (skill is null) return;
        SetDraft($"/{skill.Name} ");
        CloseSkillPalette();
        InputBox.Focus(FocusState.Programmatic);
    }

    private void SubmitCurrentText()
    {
        var text = InputBox.Text.Trim();
        var isAttachmentsBusy = PendingAttachments.Any(a => a.IsBusy);
        var readyAttachments = PendingAttachments.Where(a => a.Status == AttachmentStatus.Ready).ToList();

        if (!CanSubmit || isAttachmentsBusy) return;
        if (text.Length == 0 && readyAttachments.Count == 0) return;

        InputBox.Text = "";
        // Only the attachments that made it to Ready are sent; Failed ones stay in
        // the rail so the user notices and can remove/retry them — a bad attachment
        // must never silently disappear or block the rest of the message.
        foreach (var attachment in readyAttachments) PendingAttachments.Remove(attachment);
        RefreshSendButtonState();
        SubmitRequested?.Invoke(this, new ComposerSubmission(text, readyAttachments));
    }


    private void RefreshSendButtonState()
    {
        var isStarting = _recordingState == RecordingState.Starting;
        var isRecording = _recordingState == RecordingState.Recording;
        var isStopping = _recordingState == RecordingState.Stopping;
        var isActive = IsRecordingActive;

        var isAttachmentsBusy = PendingAttachments.Any(a => a.IsBusy);
        var hasReadyAttachment = PendingAttachments.Any(a => a.Status == AttachmentStatus.Ready);
        var canSend = CanSubmit && !IsBusy && !isAttachmentsBusy
            && (!string.IsNullOrWhiteSpace(InputBox.Text) || hasReadyAttachment);
        InputBox.IsEnabled = CanSubmit && !IsBusy;
        AddAttachmentButton.IsEnabled = CanSubmit && !IsBusy;
        SpeechButton.IsEnabled = CanSubmit && !IsBusy && !isActive;

        // Send and Stop share the slot: a turn is running or it isn't.
        SendButton.Visibility = IsBusy ? Visibility.Collapsed : Visibility.Visible;
        SendButton.IsEnabled = canSend || isActive;
        SendButton.Background = isActive || canSend
            ? Brush("ComposerPrimaryButtonBackgroundBrush")
            : Brush("ComposerDisabledButtonBackgroundBrush");
        SendIcon.Foreground = isActive || canSend
            ? Brush("ComposerPrimaryButtonForegroundBrush")
            : Brush("ComposerDisabledButtonForegroundBrush");

        StopButton.Visibility = CanStop ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = CanStop && !IsStopping;
        StopButton.Width = IsStopping ? 112 : 36;
        StopRing.IsActive = IsStopping;
        StopRing.Visibility = IsStopping ? Visibility.Visible : Visibility.Collapsed;
        StopGlyph.Visibility = IsStopping ? Visibility.Collapsed : Visibility.Visible;
        StopLabel.Visibility = IsStopping ? Visibility.Visible : Visibility.Collapsed;
        StopRing.Foreground = Brush("AppBackgroundBrush");
        var stopLabel = IsStopping ? "Stopping..." : "Stop";
        ToolTipService.SetToolTip(StopButton, stopLabel);
        AutomationProperties.SetName(StopButton, stopLabel);

        RefreshModeSelector();

        SpeechIcon.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;
        SpeechRing.IsActive = isActive;
        SpeechRing.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;

        RecordingWaveformHost.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        StopSpeechButton.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        StopSpeechButton.IsEnabled = isActive && !isStopping;
        SpeechButton.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;

        BufferSpinner.IsActive = isStarting || isStopping;
        BufferSpinner.Visibility = (isStarting || isStopping) ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Brush Brush(string resourceKey)
        => (Brush)Application.Current.Resources[resourceKey];

    private void Composer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var composerWidth = (double)Application.Current.Resources["ChatComposerWidth"];
        ComposerPanel.Width = Math.Max(280, Math.Min(composerWidth, e.NewSize.Width - 48));
        ApplySuggestionVisibility(e.NewSize.Width);
    }

}
