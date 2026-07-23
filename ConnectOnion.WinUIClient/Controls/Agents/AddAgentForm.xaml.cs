using System;
using System.Threading;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// Window-level Add Agent overlay. Network, validation, and persistence state live in
/// <see cref="AddAgentViewModel"/>; this class owns only modal lifecycle and input.
/// </summary>
public sealed partial class AddAgentForm : UserControl, IDisposable
{
    private CancellationTokenSource? _operationCts;
    private FrameworkElement? _focusReturnTarget;
    private int _disposed;

    public AddAgentViewModel Vm { get; } = App.GetService<AddAgentViewModel>();

    public event Action<AgentConfig>? AgentAdded;

    public bool IsOpen => Visibility == Visibility.Visible;

    public AddAgentForm() => InitializeComponent();

    public void Show(FrameworkElement? focusReturnTarget)
    {
        ThrowIfDisposed();
        _focusReturnTarget = focusReturnTarget;

        if (!IsOpen)
        {
            CancelPendingOperation();
            Vm.Reset();
            _operationCts = new CancellationTokenSource();
            Visibility = Visibility.Visible;
            IsHitTestVisible = true;
        }

        DispatcherQueue.TryEnqueue(() => InputBox.Focus(FocusState.Programmatic));
    }

    public void Hide()
    {
        if (!IsOpen) return;

        CancelPendingOperation();
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        Vm.Reset();

        _focusReturnTarget?.Focus(FocusState.Programmatic);
        _focusReturnTarget = null;
    }

    public void CancelPendingOperation()
    {
        var cts = Interlocked.Exchange(ref _operationCts, null);
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
    }

    public void Shutdown() => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        CancelPendingOperation();
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        _focusReturnTarget = null;
        AgentAdded = null;
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
        => await TestConnectionAsync();

    private async Task TestConnectionAsync()
    {
        var token = _operationCts?.Token ?? CancellationToken.None;
        await Vm.TestConnectionAsync(token);
    }

    private async void AddAgent_Click(object sender, RoutedEventArgs e)
        => await AddAgentAsync();

    private async Task AddAgentAsync()
    {
        if (!Vm.CanAdd) return;
        var token = _operationCts?.Token ?? CancellationToken.None;
        var agent = await Vm.AddAsync(token);
        if (agent is null || token.IsCancellationRequested) return;

        AgentAdded?.Invoke(agent);
        Hide();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Hide();

    private void InputBox_LostFocus(object sender, RoutedEventArgs e)
        => Vm.MarkInteracted();

    private async void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || Vm.IsBusy) return;
        e.Handled = true;
        if (Vm.CanAdd)
            await AddAgentAsync();
        else
            await TestConnectionAsync();
    }

    private void OverlayRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        Hide();
    }

    private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e) => Hide();

    private void ModalContainer_Tapped(object sender, TappedRoutedEventArgs e)
        => e.Handled = true;

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed != 0, this);
}
