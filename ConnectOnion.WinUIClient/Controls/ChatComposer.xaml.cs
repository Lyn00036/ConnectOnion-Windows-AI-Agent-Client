using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Devices.Enumeration;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.Render;
using Windows.Media.SpeechRecognition;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using WinRT;
using ConnectOnion.WinUIClient.Services;

namespace ConnectOnion.WinUIClient.Controls;

public sealed partial class ChatComposer : UserControl
{
    // ---- Waveform constants ----
    private const float BarWidth = 2;
    private const float BarGap = 1;
    private const float SampleStride = BarWidth + BarGap;
    private const int MaxRingSamples = 600;
    private const double SmoothingFactor = 0.35;
    // Speech RMS is small (~0.02-0.08 normalized); boost so bars are visibly tall.
    private const double AmplitudeGain = 12.0;

    // ---- Recording state machine ----
    private enum RecordingState { Idle, Starting, Recording, Stopping }
    private RecordingState _recordingState = RecordingState.Idle;
    private const int StartBufferMs = 500;  // capture audio before recognition starts
    private const int StopBufferMs = 500;   // capture final speech before teardown
    private CancellationTokenSource? _recordingCts;

    // ---- Speech recognition ----
    private SpeechRecognizer? _speechRecognizer;
    private AudioGraph? _audioGraph;
    private AudioDeviceInputNode? _audioInputNode;
    private AudioFrameOutputNode? _audioFrameOutputNode;
    private readonly object _amplitudeLock = new();
    private readonly DispatcherTimer _recordingElapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _latestSpeechText = "";
    private double _targetAmplitude;
    private double _smoothedAmplitude;
    private DateTimeOffset _recordingStartedAt;

    // ---- Waveform ring buffer ----
    private readonly double[] _sampleRing = new double[MaxRingSamples];
    private int _sampleWritePos;
    private int _sampleCount;

    // ---- Baseline dash style (created once) ----
    private CanvasStrokeStyle? _dashStyle;

    // ---- Theme-aware colours ----
    private Color _bgColor;
    private Color _baselineColor;
    private Color _barColor;

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

    public event EventHandler<string>? SubmitRequested;

    public ChatComposer()
    {
        InitializeComponent();
        ApplyWaveformTheme();
        WaveformCanvas.Draw += WaveformCanvas_Draw;
        _recordingElapsedTimer.Tick += RecordingElapsedTimer_Tick;
        ActualThemeChanged += (_, _) => ApplyWaveformTheme();
        Unloaded += Composer_Unloaded;
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

    public void FocusInput()
        => InputBox.Focus(FocusState.Programmatic);

    private static void OnCanSubmitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ChatComposer)d).RefreshSendButtonState();

    private static void OnIsBusyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ChatComposer)d).RefreshSendButtonState();

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (IsRecordingActive)
            await StopSpeechRecognitionAsync();
        SubmitCurrentText();
    }

    private async void Composer_Unloaded(object sender, RoutedEventArgs e)
    {
        WaveformCanvas.RemoveFromVisualTree();
        await StopSpeechRecognitionAsync();
        _speechRecognizer?.Dispose();
        _speechRecognizer = null;
        StopAudioMeter();
    }

    private bool IsRecordingActive => _recordingState is RecordingState.Starting
                                                        or RecordingState.Recording
                                                        or RecordingState.Stopping;

    private async void Speech_Click(object sender, RoutedEventArgs e)
    {
        if (IsRecordingActive || !CanSubmit || IsBusy) return;

        _recordingCts?.Cancel();
        _recordingCts = new CancellationTokenSource();
        var ct = _recordingCts.Token;

        try
        {
            // --- Starting: show spinner, start audio meter, buffer delay ---
            _recordingState = RecordingState.Starting;
            RefreshSendButtonState();
            _ = StartAudioMeterAsync();

            await Task.Delay(StartBufferMs, ct);

            // --- Start recognizer ---
            DetachSpeechRecognizerEvents();
            _speechRecognizer?.Dispose();
            _speechRecognizer = new SpeechRecognizer();
            _latestSpeechText = "";
            _speechRecognizer.HypothesisGenerated += SpeechRecognizer_HypothesisGenerated;
            _speechRecognizer.ContinuousRecognitionSession.ResultGenerated += SpeechRecognition_ResultGenerated;
            _speechRecognizer.Constraints.Add(
                new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "message"));

            var compilation = await _speechRecognizer.CompileConstraintsAsync();
            ct.ThrowIfCancellationRequested();
            if (compilation.Status != SpeechRecognitionResultStatus.Success)
            {
                _recordingState = RecordingState.Idle;
                RefreshSendButtonState();
                StopAudioMeter();
                await ShowSpeechIssueAsync("Speech recognition could not be started.");
                return;
            }

            await _speechRecognizer.ContinuousRecognitionSession.StartAsync();
            ct.ThrowIfCancellationRequested();

            // --- Recording: show waveform, start timer ---
            _recordingState = RecordingState.Recording;
            StartRecordingVisuals();
            RefreshSendButtonState();
        }
        catch (OperationCanceledException) { /* aborted */ }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or COMException)
        {
            _recordingState = RecordingState.Idle;
            StopAudioMeter();
            RefreshSendButtonState();
            await ShowSpeechIssueAsync("Speech recognition is unavailable. Check microphone and speech privacy settings.");
            FocusInput();
        }
    }

    private void StopSpeech_Click(object sender, RoutedEventArgs e)
        => _ = StopSpeechRecognitionAsync();

    private async System.Threading.Tasks.Task StopSpeechRecognitionAsync()
    {
        if (!IsRecordingActive || _recordingState == RecordingState.Stopping) return;

        // --- Stopping: show spinner, cancel recognizer, keep audio meter for buffer ---
        _recordingState = RecordingState.Stopping;
        RefreshSendButtonState();

        _recordingCts?.Cancel();
        _recordingCts = new CancellationTokenSource();
        var ct = _recordingCts.Token;

        try
        {
            if (_speechRecognizer is not null)
            {
                await _speechRecognizer.ContinuousRecognitionSession.CancelAsync();
            }
        }
        catch { }

        try
        {
            // Buffer delay to capture final speech
            await Task.Delay(StopBufferMs, ct);
        }
        catch (OperationCanceledException) { /* timeout expired */ }

        // --- Idle: stop canvas, stop audio, append text ---
        DetachSpeechRecognizerEvents();
        StopRecordingVisuals();
        StopAudioMeter();
        AppendLatestSpeechText();

        _recordingState = RecordingState.Idle;
        _recordingCts?.Cancel();
        RefreshSendButtonState();
        FocusInput();
    }

    private void SpeechRecognizer_HypothesisGenerated(
        SpeechRecognizer sender,
        SpeechRecognitionHypothesisGeneratedEventArgs args)
    {
        _latestSpeechText = args.Hypothesis.Text;
    }

    private void SpeechRecognition_ResultGenerated(
        SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        var result = args.Result;
        if (result.Status == SpeechRecognitionResultStatus.Success
            && !string.IsNullOrWhiteSpace(result.Text))
        {
            // Append immediately — incremental so text accumulates during
            // recording and the last utterance is never lost on CancelAsync.
            AppendRecognizedText(result.Text.Trim());
            _latestSpeechText = result.Text;
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
        if (text.Length == 0 || !CanSubmit) return;

        InputBox.Text = "";
        RefreshSendButtonState();
        SubmitRequested?.Invoke(this, text);
    }

    private void AppendRecognizedText(string text)
    {
        var current = InputBox.Text;
        InputBox.Text = string.IsNullOrWhiteSpace(current)
            ? text
            : $"{current.TrimEnd()} {text}";
        InputBox.SelectionStart = InputBox.Text.Length;
    }

    private void AppendLatestSpeechText()
    {
        if (string.IsNullOrWhiteSpace(_latestSpeechText)) return;
        AppendRecognizedText(_latestSpeechText.Trim());
        _latestSpeechText = "";
    }

    // ---- Recording controls ----

    private void StartRecordingVisuals()
    {
        _recordingStartedAt = DateTimeOffset.Now;
        WaveformTimerText.Text = "0:00";
        InputBox.PlaceholderText = "Listening...";
        _targetAmplitude = 0;
        _smoothedAmplitude = 0;
        _sampleWritePos = 0;
        _sampleCount = 0;
        Array.Clear(_sampleRing);
        _recordingElapsedTimer.Start();
    }

    private void StopRecordingVisuals()
    {
        _recordingElapsedTimer.Stop();
        _sampleCount = 0;
        _sampleWritePos = 0;
        Array.Clear(_sampleRing);
        InputBox.PlaceholderText = "Do anything";
    }

    private void RecordingElapsedTimer_Tick(object? sender, object e)
    {
        var elapsed = DateTimeOffset.Now - _recordingStartedAt;
        WaveformTimerText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
    }

    // ---- Win2D drawing ----

    /// <summary>Per-frame update: read amplitude, push to ring buffer.</summary>
    private void AdvanceWaveform()
    {
        double target;
        lock (_amplitudeLock) { target = _targetAmplitude; }
        _smoothedAmplitude = _smoothedAmplitude * (1 - SmoothingFactor) + target * SmoothingFactor;

        _sampleRing[_sampleWritePos] = Math.Clamp(_smoothedAmplitude, 0, 1);
        _sampleWritePos = (_sampleWritePos + 1) % MaxRingSamples;
        if (_sampleCount < MaxRingSamples) _sampleCount++;
    }

    /// <summary>GPU-accelerated drawing of the scrolling waveform, driven by the
    /// control's own ~60fps render loop (runs on the Win2D render thread).</summary>
    private void WaveformCanvas_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        float w = (float)sender.Size.Width;
        float h = (float)sender.Size.Height;

        if (w <= 0 || h <= 0) return;

        if (_dashStyle is null)
        {
            _dashStyle = new CanvasStrokeStyle
            {
                DashStyle = CanvasDashStyle.Dash,
                DashCap = CanvasCapStyle.Round,
            };
        }

        float centerY = h / 2f;

        // Background + baseline — always drawn, even when idle
        ds.FillRectangle(0, 0, w, h, _bgColor);
        ds.DrawLine(0, centerY, w, centerY, _baselineColor, 1, _dashStyle);

        // Only advance and render bars while recording
        if (_recordingState == RecordingState.Recording)
        {
            AdvanceWaveform();

            float maxHalf = centerY - 2f;
            int visibleBars = (int)(w / SampleStride);
            if (visibleBars <= 0) return;

            int count = Math.Min(_sampleCount, visibleBars);
            for (int k = 0; k < count; k++)
            {
                float barX = w - BarWidth - k * SampleStride;
                if (barX < 0) break;

                int ringIndex = ((_sampleWritePos - 1 - k) % MaxRingSamples + MaxRingSamples) % MaxRingSamples;
                double amp = _sampleRing[ringIndex];
                float halfH = Math.Max(1, (float)(amp * maxHalf));
                float topY = centerY - halfH;

                ds.FillRectangle(barX, topY, BarWidth, halfH * 2, _barColor);
            }
        }
    }

    // ---- Theme ----

    private void ApplyWaveformTheme()
    {
        var isDark = ActualTheme == ElementTheme.Dark
                  || (ActualTheme == ElementTheme.Default
                      && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        _bgColor = isDark ? Color.FromArgb(255, 27, 27, 27)     // #1B1B1B
                          : Color.FromArgb(255, 243, 243, 243);  // #F3F3F3
        _baselineColor = isDark ? Color.FromArgb(255, 80, 80, 80)    // #505050
                                : Color.FromArgb(255, 204, 204, 204); // #CCCCCC
        _barColor = isDark ? Colors.White
                           : Color.FromArgb(255, 26, 26, 26);     // #1A1A1A
    }

    // ---- Audio meter ----

    private async System.Threading.Tasks.Task StartAudioMeterAsync()
    {
        try
        {
            StopAudioMeter();

            // Use the multimedia render/capture category, NOT Speech. The SpeechRecognizer
            // already holds the communications-role microphone; requesting the same
            // communications endpoint here returns DeviceNotAvailable. The default
            // multimedia endpoint can share the mic in shared mode.
            var settings = new AudioGraphSettings(AudioRenderCategory.Media)
            {
                QuantumSizeSelectionMode = QuantumSizeSelectionMode.ClosestToDesired,
                DesiredSamplesPerQuantum = 480,
            };

            var graphResult = await AudioGraph.CreateAsync(settings);
            if (graphResult.Status != AudioGraphCreationStatus.Success) return;

            _audioGraph = graphResult.Graph;

            // Try the user's preferred microphone first, then fall back to
            // the system default.  Use the multimedia-role endpoint so we
            // don't collide with the recognizer's communications-role device.
            DeviceInformation? captureDevice = null;
            try
            {
                var prefs = await AppServices.Preferences.LoadAsync();
                if (!string.IsNullOrEmpty(prefs.MicrophoneDeviceId))
                    captureDevice = await DeviceInformation.CreateFromIdAsync(prefs.MicrophoneDeviceId);
            }
            catch { }

            if (captureDevice is null)
            {
                try
                {
                    var id = MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Default);
                    if (!string.IsNullOrEmpty(id))
                        captureDevice = await DeviceInformation.CreateFromIdAsync(id);
                }
                catch { }
            }

            var inputResult = captureDevice is null
                ? await _audioGraph.CreateDeviceInputNodeAsync(MediaCategory.Other)
                : await _audioGraph.CreateDeviceInputNodeAsync(
                    MediaCategory.Other, _audioGraph.EncodingProperties, captureDevice);
            if (inputResult.Status != AudioDeviceNodeCreationStatus.Success)
            {
                StopAudioMeter();
                return;
            }

            _audioInputNode = inputResult.DeviceInputNode;
            _audioFrameOutputNode = _audioGraph.CreateFrameOutputNode();
            _audioInputNode.AddOutgoingConnection(_audioFrameOutputNode);
            _audioGraph.QuantumStarted += AudioGraph_QuantumStarted;
            _audioFrameOutputNode.Start();
            _audioInputNode.Start();
            _audioGraph.Start();
        }
        catch
        {
            StopAudioMeter();
        }
    }

    private void StopAudioMeter()
    {
        if (_audioGraph is not null)
            _audioGraph.QuantumStarted -= AudioGraph_QuantumStarted;

        try { _audioGraph?.Stop(); } catch { }

        _audioFrameOutputNode?.Dispose();
        _audioInputNode?.Dispose();
        _audioGraph?.Dispose();
        _audioFrameOutputNode = null;
        _audioInputNode = null;
        _audioGraph = null;

        lock (_amplitudeLock) { _targetAmplitude = 0; }
    }

    private void AudioGraph_QuantumStarted(AudioGraph sender, object args)
    {
        if (_audioFrameOutputNode is null) return;

        using var frame = _audioFrameOutputNode.GetFrame();
        var amplitude = ReadAmplitude(frame);
        var scaled = Math.Clamp(amplitude * AmplitudeGain, 0, 1);
        lock (_amplitudeLock) { _targetAmplitude = scaled; }
    }

    private static unsafe double ReadAmplitude(AudioFrame frame)
    {
        using var buffer = frame.LockBuffer(AudioBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        // Under C#/WinRT a direct cast to the COM interface throws InvalidCastException;
        // QueryInterface through CsWinRT's As<T>() instead.
        var byteAccess = reference.As<IMemoryBufferByteAccess>();
        byteAccess.GetBuffer(out var dataInBytes, out var capacity);

        var sampleCount = (int)capacity / sizeof(float);
        if (sampleCount <= 0) return 0;

        var samples = (float*)dataInBytes;
        double sum = 0;
        for (var i = 0; i < sampleCount; i++)
        {
            sum += samples[i] * samples[i];
        }
        return Math.Sqrt(sum / sampleCount);
    }

    private void DetachSpeechRecognizerEvents()
    {
        if (_speechRecognizer is null) return;
        _speechRecognizer.HypothesisGenerated -= SpeechRecognizer_HypothesisGenerated;
        _speechRecognizer.ContinuousRecognitionSession.ResultGenerated -= SpeechRecognition_ResultGenerated;
    }

    private async System.Threading.Tasks.Task ShowSpeechIssueAsync(string message)
    {
        if (XamlRoot is null) return;

        var dialog = new ContentDialog
        {
            Title = "Voice input",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        _ = await dialog.ShowAsync();
    }

    private void RefreshSendButtonState()
    {
        var isStarting = _recordingState == RecordingState.Starting;
        var isRecording = _recordingState == RecordingState.Recording;
        var isStopping = _recordingState == RecordingState.Stopping;
        var isActive = IsRecordingActive;

        var canSend = CanSubmit && !IsBusy && !string.IsNullOrWhiteSpace(InputBox.Text);
        InputBox.IsEnabled = CanSubmit && !IsBusy;
        SpeechButton.IsEnabled = CanSubmit && !IsBusy && !isActive;
        SendButton.IsEnabled = canSend || isActive;
        SendButton.Background = isActive || canSend || IsBusy
            ? Brush("ComposerPrimaryButtonBackgroundBrush")
            : Brush("ComposerDisabledButtonBackgroundBrush");
        SendIcon.Visibility = IsBusy ? Visibility.Collapsed : Visibility.Visible;
        SendIcon.Foreground = isActive || canSend
            ? Brush("ComposerPrimaryButtonForegroundBrush")
            : Brush("ComposerDisabledButtonForegroundBrush");
        BusyRing.IsActive = IsBusy;
        BusyRing.Visibility = IsBusy ? Visibility.Visible : Visibility.Collapsed;
        BusyRing.Foreground = Brush("ApplicationPageBackgroundThemeBrush");

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
        SuggestionPanel.Visibility = e.NewSize.Width < 620 ? Visibility.Collapsed : Visibility.Visible;
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
