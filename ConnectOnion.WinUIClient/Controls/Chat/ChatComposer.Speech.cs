using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Devices.Enumeration;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.Render;
using Windows.Media.SpeechRecognition;
using Windows.UI;
using WinRT;
using ConnectOnion.WinUIClient.Services;

namespace ConnectOnion.WinUIClient.Controls;

// Voice input for the composer: the microphone speech-recognition session, the
// audio-meter AudioGraph, and the Win2D scrolling waveform. Split out of
// ChatComposer.xaml.cs so that file stays focused on text/attachments/submit.
// The constructor wiring (ApplyWaveformTheme, WaveformCanvas.Draw,
// _recordingElapsedTimer.Tick, ActualThemeChanged) and RefreshSendButtonState()
// live in ChatComposer.xaml.cs; both partials share this one ChatComposer class.
public sealed partial class ChatComposer
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
    //
    // Four states rather than a bool, because both transitions take real time and the UI has to
    // show something during them: Starting compiles speech constraints (hundreds of ms) and
    // Stopping drains the recognizer's final result. Treating either as "recording" would let a
    // second click re-enter mid-transition; treating either as "idle" would drop the audio the
    // user is still speaking. IsRecordingActive covers all three non-Idle states, which is what
    // the re-entrancy guards test.
    private enum RecordingState { Idle, Starting, Recording, Stopping }
    private RecordingState _recordingState = RecordingState.Idle;
    /// <summary>Audio is metered for this long before the recognizer starts, so the waveform is
    /// already moving when the user begins speaking rather than snapping to life late.</summary>
    private const int StartBufferMs = 500;
    /// <summary>Capture continues for this long after Stop, so a word still in flight when the
    /// user clicks lands in the transcript instead of being cut off.</summary>
    private const int StopBufferMs = 500;
    /// <summary>Cancels the in-progress start or stop sequence. Replaced (not reused) at each
    /// transition, so a Stop cleanly aborts a Start that is still compiling constraints.</summary>
    private CancellationTokenSource? _recordingCts;

    // ---- Speech recognition ----
    private SpeechRecognizer? _speechRecognizer;
    private AudioGraph? _audioGraph;
    private AudioDeviceInputNode? _audioInputNode;
    private AudioFrameOutputNode? _audioFrameOutputNode;
    /// <summary>Guards the amplitude fields, which are written from the AudioGraph's callback
    /// thread and read by the Win2D draw handler on the UI thread.</summary>
    private readonly object _amplitudeLock = new();
    private readonly DispatcherTimer _recordingElapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _latestSpeechText = "";
    private double _targetAmplitude;
    private double _smoothedAmplitude;
    private DateTimeOffset _recordingStartedAt;

    // ---- Waveform ring buffer ----
    // Fixed-size and overwriting: the waveform scrolls, so only the most recent MaxRingSamples
    // are ever drawn and an unbounded list would grow for the whole recording to no purpose.
    // _sampleCount saturates at capacity and exists to distinguish a partially-filled buffer
    // (draw only what we have) from a wrapped one (draw everything, oldest first).
    private readonly double[] _sampleRing = new double[MaxRingSamples];
    private int _sampleWritePos;
    private int _sampleCount;

    // ---- Baseline dash style (created once) ----
    private CanvasStrokeStyle? _dashStyle;

    // ---- Theme-aware colours ----
    private Color _bgColor;
    private Color _baselineColor;
    private Color _barColor;

    private bool IsRecordingActive => _recordingState is RecordingState.Starting
                                                        or RecordingState.Recording
                                                        or RecordingState.Stopping;

    private async void Speech_Click(object sender, RoutedEventArgs e)
    {
        if (IsRecordingActive || !CanSubmit || IsBusy) return;

        _recordingCts?.Cancel();
        _recordingCts?.Dispose();
        _recordingCts = new CancellationTokenSource();
        var ct = _recordingCts.Token;

        try
        {
            // --- Starting: show spinner, start audio meter, buffer delay ---
            _recordingState = RecordingState.Starting;
            RefreshSendButtonState();
            // Not awaited: the meter runs for the whole session, so awaiting it here would
            // never return. It observes the same token, so a cancelled start tears it down.
            _ = StartAudioMeterAsync(ct);

            await Task.Delay(StartBufferMs, ct);

            // --- Start recognizer ---
            // A fresh recognizer per session, with the old one's handlers detached first: a
            // SpeechRecognizer cannot be restarted after cancellation, and leaving handlers on
            // a disposed instance would deliver a previous session's results into this one.
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
        // The three ways speech legitimately fails on a user's machine: microphone or online
        // speech permission denied (UnauthorizedAccess), the speech stack unavailable or
        // already in use (InvalidOperation), and a WinRT/COM-level failure. All three are the
        // user's environment, not a bug, so they surface as one actionable message rather than
        // a crash. Anything else is left to propagate.
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
        _recordingCts?.Dispose();
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
        // Hypotheses are the recognizer's running guess and get revised as the user keeps
        // speaking, so this only *stores* the latest — it must not append. It is the safety net
        // for the final utterance, which cancellation can end before a Result is generated;
        // AppendLatestSpeechText flushes it during teardown.
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

    private void AppendRecognizedText(string text)
    {
        var current = InputBox.Text;
        InputBox.Text = string.IsNullOrWhiteSpace(current)
            ? text
            : $"{current.TrimEnd()} {text}";
        InputBox.SelectionStart = InputBox.Text.Length;
    }

    /// <summary>Flushes the pending hypothesis at teardown. Cleared afterwards so the same text
    /// cannot be appended twice — a hypothesis that did get confirmed as a Result was already
    /// appended by <see cref="SpeechRecognition_ResultGenerated"/>.</summary>
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
        // Exponential moving average. Raw RMS jitters frame to frame and would render as noise;
        // this lags the signal slightly in exchange for bars that read as speech.
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

        // Only advance and render bars while recording. Note the ring buffer is advanced from
        // *inside* the draw callback, which ties the scroll speed to the render loop rather
        // than to real time — deliberate, since the waveform is an activity indicator, not a
        // measurement, and it keeps the bars evenly spaced whatever the frame rate.
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

                // Walk backwards from the newest sample so the waveform scrolls right-to-left.
                // The double modulo is the standard fix for C#'s % returning a negative
                // remainder for negative operands, which a single % would produce here.
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

        // Matches the Border painted behind WaveformCanvas in XAML (SurfaceElevatedColor,
        // aliased from CardBackgroundFillColorDefaultBrush) — the canvas fills the exact
        // same rect, so the two must agree or the seam shows.
        _bgColor = ThemeService.GetColor("SurfaceElevatedColor", isDark);
        _baselineColor = ThemeService.GetColor("BorderStrongColor", isDark);
        _barColor = ThemeService.GetColor("TextPrimaryColor", isDark);
    }

    // ---- Audio meter ----

    private async System.Threading.Tasks.Task StartAudioMeterAsync(CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();

            // Try the user's preferred microphone first, then fall back to
            // the system default.  Use the multimedia-role endpoint so we
            // don't collide with the recognizer's communications-role device.
            DeviceInformation? captureDevice = null;
            try
            {
                var prefs = await AppServices.Preferences.LoadAsync(cancellationToken);
                if (!string.IsNullOrEmpty(prefs.MicrophoneDeviceId))
                    captureDevice = await DeviceInformation.CreateFromIdAsync(prefs.MicrophoneDeviceId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
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
            cancellationToken.ThrowIfCancellationRequested();
            _audioFrameOutputNode = _audioGraph.CreateFrameOutputNode();
            _audioInputNode.AddOutgoingConnection(_audioFrameOutputNode);
            _audioGraph.QuantumStarted += AudioGraph_QuantumStarted;
            _audioFrameOutputNode.Start();
            _audioInputNode.Start();
            _audioGraph.Start();
        }
        catch (OperationCanceledException)
        {
            StopAudioMeter();
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

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
