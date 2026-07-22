using ConnectOnion.WinUIClient.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ConnectOnion.WinUIClient.Controls;

public sealed partial class AskUserCard : UserControl
{
    public AskUserCard() => InitializeComponent();
    public ChatMessage Message { get => (ChatMessage)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(ChatMessage), typeof(AskUserCard), new PropertyMetadata(null));

    public event RoutedEventHandler? SubmitRequested;
    public event RoutedEventHandler? SkipRequested;

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AskUserOptionEntry option) option.Toggle();
    }
    private void Submit_Click(object sender, RoutedEventArgs e) => SubmitRequested?.Invoke(this, e);
    private void Skip_Click(object sender, RoutedEventArgs e) => SkipRequested?.Invoke(this, e);
    private void AnswerTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl && e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            SubmitRequested?.Invoke(this, new RoutedEventArgs());
        }
    }
}
