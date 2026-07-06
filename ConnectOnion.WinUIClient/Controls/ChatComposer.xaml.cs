using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace ConnectOnion.WinUIClient.Controls;

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

    public event EventHandler<string>? SubmitRequested;

    public ChatComposer()
    {
        InitializeComponent();
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

    private void Send_Click(object sender, RoutedEventArgs e)
        => SubmitCurrentText();

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

    private void RefreshSendButtonState()
    {
        var canSend = CanSubmit && !IsBusy && !string.IsNullOrWhiteSpace(InputBox.Text);
        InputBox.IsEnabled = CanSubmit && !IsBusy;
        SendButton.IsEnabled = canSend;
        SendButton.Background = Brush(canSend || IsBusy
            ? "TextFillColorPrimaryBrush"
            : "ControlFillColorDisabledBrush");
        SendIcon.Visibility = IsBusy ? Visibility.Collapsed : Visibility.Visible;
        SendIcon.Foreground = Brush(canSend
            ? "ApplicationPageBackgroundThemeBrush"
            : "TextFillColorDisabledBrush");
        BusyRing.IsActive = IsBusy;
        BusyRing.Visibility = IsBusy ? Visibility.Visible : Visibility.Collapsed;
        BusyRing.Foreground = Brush("ApplicationPageBackgroundThemeBrush");
    }

    private static Brush Brush(string resourceKey)
        => (Brush)Application.Current.Resources[resourceKey];

    private void Composer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ComposerPanel.Width = Math.Max(280, Math.Min(880, e.NewSize.Width - 48));
        SuggestionPanel.Visibility = e.NewSize.Width < 620 ? Visibility.Collapsed : Visibility.Visible;
    }
}
