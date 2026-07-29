using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Devices.Enumeration;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.Render;
using Windows.Media.SpeechRecognition;
using WinRT;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Controls;

// Voice input for the composer: the microphone speech-recognition session, the
// audio-meter AudioGraph, and the lightweight XAML scrolling waveform. Split out of
// ChatComposer.xaml.cs so that file stays focused on text/attachments/submit.
// The constructor wiring (ApplyWaveformTheme, _waveformTimer.Tick,
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
    /// <summary>
    /// Upper bound on how long the start sequence waits for the audio meter to come up before
    /// showing the recording UI anyway.
    ///
    /// <para>This used to be an unconditional <c>Task.Delay</c> in front of all the recogniser
    /// work, so every start paid it in full and it was pure serialised latency: measured, the
    /// meter is ready in ~460 ms and the recogniser takes ~1.2 s (first use) or ~220 ms
    /// (subsequent) to start, and none of that overlapped. It is now a cap on an actual
    /// readiness signal — the start proceeds as soon as the meter is running — which keeps the
    /// original intent (waveform already moving before recording begins) without charging every
    /// user a fixed 500 ms for it.</para>
    /// </summary>
    private const int MeterReadyTimeoutMs = 1500;
    /// <summary>Capture continues for this long after Stop, so a word still in flight when the
    /// user clicks lands in the transcript instead of being cut off.</summary>
    private const int StopBufferMs = 500;
    /// <summary>Cancels the in-progress start or stop sequence. Replaced (not reused) at each
    /// transition, so a Stop cleanly aborts a Start that is still compiling constraints.</summary>
    private CancellationTokenSource? _recordingCts;

    // ---- Speech recognition ----
    private SpeechRecognizer? _speechRecognizer;

    /// <summary>
    /// Completes when the audio meter is running — or has failed, which counts as "ready" because
    /// the start must not hang waiting for a microphone that will never arrive. Recreated per
    /// recording session.
    /// </summary>
    private TaskCompletionSource? _meterReady;

    private AudioGraph? _audioGraph;
    private AudioDeviceInputNode? _audioInputNode;
    private AudioFrameOutputNode? _audioFrameOutputNode;
    /// <summary>Guards the amplitude fields, which are written from the AudioGraph's callback
    /// thread and read by the Win2D draw handler on the UI thread.</summary>
    private readonly object _amplitudeLock = new();
    private readonly DispatcherTimer _recordingElapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _waveformTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
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

    private readonly System.Collections.Generic.List<Rectangle> _waveformBars = new();
    private readonly SolidColorBrush _waveformBrush = new();

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
            // --- Starting: spinner up, then everything that can run at once, does ---
            //
            // The meter and the recogniser touch different audio endpoints (see the category note
            // in StartAudioMeterAsync), so neither has to wait for the other. Running them in
            // sequence — which is what the old fixed buffer delay did — made every start cost the
            // sum of two independent waits instead of the longer one.
            _recordingState = RecordingState.Starting;
            // Before RefreshSendButtonState, which is what makes the panel visible — clearing it
            // afterwards would show the previous session's timer for a frame.
            ResetRecordingVisuals();
            _transcript.Begin();
            RefreshSendButtonState();
            LogSpeechFootprint("starting");

            _meterReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            // Not awaited: the meter runs for the whole session, so awaiting it here would never
            // return. It observes the same token, so a cancelled start tears it down, and it
            // signals _meterReady when the graph is live (or has failed).
            _ = StartAudioMeterAsync(ct);

            var recognizerStarting = StartRecognizerAsync(ct);

            // Wait for the meter only as long as it is plausibly still coming. A microphone that
            // never opens must not block speech recognition, which does not depend on it.
            var meterWait = Task.WhenAny(_meterReady.Task, Task.Delay(MeterReadyTimeoutMs, ct));

            var started = await recognizerStarting;
            if (!started)
            {
                _recordingState = RecordingState.Idle;
                RefreshSendButtonState();
                StopAudioMeter();
                await ShowSpeechIssueAsync("Speech recognition could not be started.");
                return;
            }

            // Recording begins only once the waveform has something to draw, preserving the
            // behaviour the old delay existed for: the meter is normally ready long before this,
            // so this await usually returns immediately.
            await meterWait;
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

    /// <summary>
    /// Takes the recogniser from idle to listening. Returns false when the constraints could not
    /// be compiled, which the caller reports as an environment problem rather than a crash.
    /// </summary>
    /// <remarks>
    /// Constructed here, on the UI thread, and not ahead of time on a background thread. A
    /// pre-warm was tried — it saves ~220 ms of construction and constraint compilation on the
    /// first use of a session — and removed again: building the recogniser on a thread-pool
    /// (MTA) thread changes which apartment WinRT delivers its events on, and the speech
    /// callbacks then arrive off the UI thread. That is survivable now (see
    /// the note on the speech callbacks below), but it also meant holding a live
    /// native speech engine resident between recordings for a saving that the far larger cost
    /// here — <c>StartAsync</c>, measured at ~1.2 s on first use and unaffected by any amount of
    /// pre-warming — dwarfs.
    /// </remarks>
    private async System.Threading.Tasks.Task<bool> StartRecognizerAsync(CancellationToken ct)
    {
        // Handlers off the previous instance first: a SpeechRecognizer cannot be restarted after
        // cancellation, and leaving handlers attached to a disposed one would deliver a previous
        // session's results into this one.
        DetachSpeechRecognizerEvents();
        _speechRecognizer?.Dispose();

        _speechRecognizer = new SpeechRecognizer();
        _speechRecognizer.Constraints.Add(
            new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "message"));
        _speechRecognizer.HypothesisGenerated += SpeechRecognizer_HypothesisGenerated;
        _speechRecognizer.ContinuousRecognitionSession.ResultGenerated += SpeechRecognition_ResultGenerated;

        var compilation = await _speechRecognizer.CompileConstraintsAsync();
        ct.ThrowIfCancellationRequested();
        if (compilation.Status != SpeechRecognitionResultStatus.Success) return false;

        await _speechRecognizer.ContinuousRecognitionSession.StartAsync();
        ct.ThrowIfCancellationRequested();
        return true;
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
        FlushTranscriptToComposer();

        // Released here rather than being left for the next start to dispose. A SpeechRecognizer
        // cannot be reused after its session is cancelled, so between recordings this object is
        // pure cost: it holds the native speech engine and its capture endpoint for as long as it
        // lives, which also keeps the microphone attributed to this app after the user has
        // visibly stopped recording. Disposal is idempotent, so the next start's defensive
        // Dispose stays harmless.
        _speechRecognizer?.Dispose();
        _speechRecognizer = null;

        _recordingState = RecordingState.Idle;
        _recordingCts?.Cancel();
        RefreshSendButtonState();
        FocusInput();

        LogSpeechFootprint("stopped");
    }

    /// <summary>
    /// Records the process footprint at each speech transition.
    ///
    /// <para>Here because "memory goes up every time I use the microphone" is otherwise
    /// unfalsifiable: the speech and audio stacks load native DLLs and buffers on first use and
    /// .NET does not hand working set back to the OS promptly, so some rise is expected and only
    /// a per-session trend distinguishes that from a leak. Two lines per recording is low enough
    /// volume to keep permanently, and turns the question into a number that can be read out of
    /// the log after a few clicks.</para>
    ///
    /// <para>Managed heap is the diagnostic half: a rising working set with a flat heap is the
    /// native stacks warming up, whereas a heap that climbs per session is a real managed leak.</para>
    /// </summary>
    private static void LogSpeechFootprint(string phase)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            process.Refresh();
            Serilog.Log.Information(
                "Speech {Phase}: working set {WorkingSetMb} MB, managed heap {HeapKb} KB",
                phase,
                process.WorkingSet64 / 1024 / 1024,
                GC.GetTotalMemory(forceFullCollection: false) / 1024);
        }
        catch
        {
            // Diagnostics must never be able to break the feature they observe.
        }
    }

    private void SpeechRecognizer_HypothesisGenerated(
        SpeechRecognizer sender,
        SpeechRecognitionHypothesisGeneratedEventArgs args)
    {
        // Stored, never displayed. It is the safety net for the utterance in flight when the user
        // stops: cancellation can end the session before the recogniser finalises what it was
        // hearing, and for a short dictation that is the entire transcript.
        _transcript.SetHypothesis(args.Hypothesis.Text);
    }

    private void SpeechRecognition_ResultGenerated(
        SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        var result = args.Result;
        if (result.Status == SpeechRecognitionResultStatus.Success)
        {
            _transcript.AddResult(result.Text);
        }
    }

    // ---- Composer text ----
    //
    // There is exactly one path from speech into the composer: FlushTranscriptToComposer, on stop.
    // Nothing is written while recording, so the box is entirely the user's until they finish —
    // they can type during dictation without the recogniser rewriting under them, and a revised
    // guess never has to be withdrawn from text already inserted.
    //
    // What was heard is accumulated by DictationTranscript in the Core project, where the
    // ordering rules are unit tested without a microphone.

    private readonly Services.Speech.DictationTranscript _transcript = new();

    /// <summary>
    /// Inserts everything the session heard, once. Called from the stop sequence on the UI thread.
    /// </summary>
    private void FlushTranscriptToComposer()
    {
        if (_transcript.IsEmpty) return;

        // Read the box now rather than at start: whatever the user typed while dictating is kept,
        // and the spoken text goes after it.
        var composed = _transcript.BuildInsertion(InputBox.Text ?? "");
        if (string.Equals(composed, InputBox.Text, StringComparison.Ordinal)) return;

        InputBox.Text = composed;
        InputBox.SelectionStart = composed.Length;
        // Speech filling the box is what makes Send become available, so the button has to be
        // re-evaluated here as it would be for typing.
        RefreshSendButtonState();
    }

    // ---- Recording controls ----

    /// <summary>
    /// Clears the waveform panel to its empty state. Called when entering <c>Starting</c>, which
    /// is when the panel becomes <i>visible</i> — <c>RefreshSendButtonState</c> shows it for the
    /// whole of <c>IsRecordingActive</c>, not just while recording.
    ///
    /// <para>Doing this in <see cref="StartRecordingVisuals"/> instead was a bug: that runs on the
    /// transition to <c>Recording</c>, so for the half-second-plus that starting the recogniser
    /// takes, the panel was on screen still showing the <i>previous</i> session's elapsed time.</para>
    /// </summary>
    private void ResetRecordingVisuals()
    {
        WaveformTimerText.Text = "0:00";
        _targetAmplitude = 0;
        _smoothedAmplitude = 0;
        _sampleWritePos = 0;
        _sampleCount = 0;
        Array.Clear(_sampleRing);
        foreach (var bar in _waveformBars) bar.Visibility = Visibility.Collapsed;
    }

    private void StartRecordingVisuals()
    {
        // The clock starts when recording actually starts, not when the panel appeared: the
        // spinner time spent waiting for the recogniser is not part of the recording.
        _recordingStartedAt = DateTimeOffset.Now;
        ResetRecordingVisuals();
        InputBox.PlaceholderText = LocalizedStrings.Get("VoiceListening", "Listening...");
        _recordingElapsedTimer.Start();
        _waveformTimer.Start();
    }

    private void StopRecordingVisuals()
    {
        _recordingElapsedTimer.Stop();
        _waveformTimer.Stop();
        _sampleCount = 0;
        _sampleWritePos = 0;
        Array.Clear(_sampleRing);
        InputBox.PlaceholderText = LocalizedStrings.Get("ComposerDoAnything", "Do anything");
    }

    private void RecordingElapsedTimer_Tick(object? sender, object e)
    {
        var elapsed = DateTimeOffset.Now - _recordingStartedAt;
        WaveformTimerText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
    }

    // ---- Lightweight XAML waveform ----

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

    private void WaveformTimer_Tick(object? sender, object e)
    {
        if (_recordingState != RecordingState.Recording) return;
        AdvanceWaveform();
        RenderWaveform();
    }

    private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var centerY = e.NewSize.Height / 2;
        WaveformBaseline.X1 = 0;
        WaveformBaseline.X2 = e.NewSize.Width;
        WaveformBaseline.Y1 = centerY;
        WaveformBaseline.Y2 = centerY;
        EnsureWaveformBars((int)(e.NewSize.Width / SampleStride));
        RenderWaveform();
    }

    private void EnsureWaveformBars(int count)
    {
        count = Math.Clamp(count, 0, MaxRingSamples);
        while (_waveformBars.Count < count)
        {
            var bar = new Rectangle
            {
                Width = BarWidth,
                RadiusX = 1,
                RadiusY = 1,
                Fill = _waveformBrush,
                Visibility = Visibility.Collapsed,
            };
            _waveformBars.Add(bar);
            WaveformCanvas.Children.Add(bar);
        }
    }

    private void RenderWaveform()
    {
        var width = WaveformCanvas.ActualWidth;
        var centerY = WaveformCanvas.ActualHeight / 2;
        if (width <= 0 || centerY <= 2) return;

        var visibleBars = Math.Min(_waveformBars.Count, (int)(width / SampleStride));
        var count = Math.Min(_sampleCount, visibleBars);
        for (var index = 0; index < visibleBars; index++)
        {
            var bar = _waveformBars[index];
            if (index >= count)
            {
                bar.Visibility = Visibility.Collapsed;
                continue;
            }

            var ringIndex = ((_sampleWritePos - 1 - index) % MaxRingSamples + MaxRingSamples) % MaxRingSamples;
            var halfHeight = Math.Max(1, _sampleRing[ringIndex] * (centerY - 2));
            bar.Height = halfHeight * 2;
            Canvas.SetLeft(bar, width - BarWidth - index * SampleStride);
            Canvas.SetTop(bar, centerY - halfHeight);
            bar.Visibility = Visibility.Visible;
        }
    }

    // ---- Theme ----

    private void ApplyWaveformTheme()
    {
        var isDark = ActualTheme == ElementTheme.Dark
                  || (ActualTheme == ElementTheme.Default
                      && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        _waveformBrush.Color = ThemeService.GetColor("TextPrimaryColor", isDark);
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
        finally
        {
            // Signalled on every path, success or not. This method has several early returns and
            // two catch arms; a start sequence waiting on a meter that quietly gave up would sit
            // out the whole MeterReadyTimeoutMs and be slower than the fixed delay this replaced.
            // "Ready" therefore means "no longer coming", not "succeeded".
            _meterReady?.TrySetResult();
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
            Title = LocalizedStrings.Get("VoiceInputTitle", "Voice input"),
            Content = message,
            CloseButtonText = LocalizedStrings.Get("CommonOk", "OK"),
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
