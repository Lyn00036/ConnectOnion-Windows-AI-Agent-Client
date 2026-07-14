using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
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

public sealed partial class ChatComposer : UserControl
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

    public event EventHandler<ComposerSubmission>? SubmitRequested;

    /// <summary>The stop button was pressed. The host decides what that can achieve — the composer
    /// only reports the press, and re-renders from <see cref="IsStopping"/> when told to.</summary>
    public event EventHandler? StopRequested;

    /// <summary>The user picked an approval mode from the pill. Carries the wire value
    /// (<see cref="AgentModes"/>), not the menu label.</summary>
    public event EventHandler<string>? ModeChangeRequested;

    /// <summary>Attachments picked but not yet sent. Cleared (for the ones actually sent) on submit.</summary>
    public ObservableCollection<PendingAttachment> PendingAttachments { get; } = new();

    private readonly CancellationTokenSource _lifetimeCts = new();

    public ChatComposer()
    {
        InitializeComponent();
        ApplyWaveformTheme();
        WaveformCanvas.Draw += WaveformCanvas_Draw;
        _recordingElapsedTimer.Tick += RecordingElapsedTimer_Tick;
        ActualThemeChanged += (_, _) => ApplyWaveformTheme();
        Unloaded += Composer_Unloaded;
        PendingAttachments.CollectionChanged += (_, _) =>
        {
            AttachmentsScrollViewer.Visibility = PendingAttachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshSendButtonState();
        };
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

    /// <summary>A stop has been asked for but the turn is still going. Turns the stop button into
    /// a force-stop, because a cooperative stop is a request the agent is free to finish ignoring.</summary>
    public bool IsStopping
    {
        get => (bool)GetValue(IsStoppingProperty);
        set => SetValue(IsStoppingProperty, value);
    }

    public void FocusInput()
        => InputBox.Focus(FocusState.Programmatic);

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
        => StopRequested?.Invoke(this, EventArgs.Empty);

    private void ApplySuggestionVisibility(double width)
        => SuggestionPanel.Visibility = ShowSuggestions && width >= 620 ? Visibility.Visible : Visibility.Collapsed;

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (IsRecordingActive)
            await StopSpeechRecognitionAsync();
        SubmitCurrentText();
    }

    private async void Composer_Unloaded(object sender, RoutedEventArgs e)
    {
        _lifetimeCts.Cancel();
        WaveformCanvas.RemoveFromVisualTree();
        await StopSpeechRecognitionAsync();
        _speechRecognizer?.Dispose();
        _speechRecognizer = null;
        StopAudioMeter();
    }

    // ---- Attachments ----

    private void AddAttachment_Click(object sender, RoutedEventArgs e)
    {
        var accepted = AcceptedInputs;
        var flyout = new MenuFlyout();

        var imageItem = new MenuFlyoutItem { Text = "Add image..." };
        var imagesBlocked = accepted?.Images == false;
        imageItem.IsEnabled = !imagesBlocked;
        if (imagesBlocked) ToolTipService.SetToolTip(imageItem, "This agent does not accept image input.");
        imageItem.Click += async (_, _) => await AddAttachmentsAsync(AttachmentKind.Image);
        flyout.Items.Add(imageItem);

        var fileItem = new MenuFlyoutItem { Text = "Add file..." };
        var filesBlocked = accepted is not null && accepted.Files is null;
        fileItem.IsEnabled = !filesBlocked;
        if (filesBlocked) ToolTipService.SetToolTip(fileItem, "This agent does not accept file input.");
        fileItem.Click += async (_, _) => await AddAttachmentsAsync(AttachmentKind.File);
        flyout.Items.Add(fileItem);

        flyout.ShowAt((FrameworkElement)sender);
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
        InputBox.Text = prompt;
        FocusInput();
        SubmitCurrentText();
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshSendButtonState();

    private void InputBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var isShiftDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (EnterToSubmit && e.Key == VirtualKey.Enter && !isShiftDown)
        {
            e.Handled = true;
            SubmitCurrentText();
        }
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

        StopButton.Visibility = IsBusy ? Visibility.Visible : Visibility.Collapsed;
        StopRing.IsActive = IsBusy;
        StopRing.Foreground = Brush("AppBackgroundBrush");
        ToolTipService.SetToolTip(
            StopButton,
            IsStopping ? "Force stop — abandons the turn here; the agent may keep running" : "Stop");

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
        ComposerPanel.Width = Math.Max(280, Math.Min(880, e.NewSize.Width - 48));
        ApplySuggestionVisibility(e.NewSize.Width);
    }

}
