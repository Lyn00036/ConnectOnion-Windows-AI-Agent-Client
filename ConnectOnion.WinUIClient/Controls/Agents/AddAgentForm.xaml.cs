using System;
using System.Threading;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// Window-level Add Agent overlay. Network, validation, and persistence state live in
/// <see cref="AddAgentViewModel"/>; this class owns modal lifecycle, input, and the optional
/// agent icon — picking it, previewing it, and cleaning it up if the agent is never created.
/// </summary>
public sealed partial class AddAgentForm : UserControl, IDisposable
{
    private CancellationTokenSource? _operationCts;
    private FrameworkElement? _focusReturnTarget;
    private int _disposed;

    /// <summary>The processed-but-uncommitted icon. It lives in the temporary directory until the
    /// agent it belongs to exists, and is deleted on every path that closes the form without
    /// creating one.</summary>
    private string? _temporaryIconPath;

    /// <summary>Set by the commit hook so a failed save can delete the file it just wrote.</summary>
    private string? _committedIconPath;

    /// <summary>Guards the two icon buttons against re-entry while a picker is open.</summary>
    private int _iconBusy;

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
            ResetIcon();
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
        ResetIcon();

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
        DiscardTemporaryIcon();
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
        if (!Vm.CanAdd || !TryBeginIconOperation()) return;
        var token = _operationCts?.Token ?? CancellationToken.None;

        // The icon can't be changed mid-create: the commit hook below reads _temporaryIconPath.
        SetIconControlsBusy(true, showProgress: false);
        try
        {
            _committedIconPath = null;
            var agent = await Vm.AddAsync(token, CommitIconForNewAgentAsync);
            if (agent is null || token.IsCancellationRequested)
            {
                // AddAsync swallows its own failures and returns null, so a file the hook already
                // committed would otherwise be left with nothing referencing it.
                await DiscardCommittedIconAsync();
                return;
            }

            AgentAdded?.Invoke(agent);
            Hide();
        }
        finally
        {
            EndIconOperation();
            if (Volatile.Read(ref _disposed) == 0 && IsOpen) SetIconControlsBusy(false, showProgress: false);
        }
    }

    // ---- Agent icon ----

    private async void ChooseIcon_Click(object sender, RoutedEventArgs e) => await ChooseIconAsync();

    private async Task ChooseIconAsync()
    {
        if (Vm.IsBusy || !TryBeginIconOperation()) return;

        HideIconError();
        SetIconControlsBusy(true, showProgress: true);
        var token = _operationCts?.Token ?? CancellationToken.None;
        string? newTemporaryPath = null;

        try
        {
            newTemporaryPath = await AppServices.AgentIcons.PickTemporaryIconAsync(token);
            // Dismissing the picker is not a failure and leaves the previous pick in place.
            if (newTemporaryPath is null) return;

            // The form can be closed while the picker is up, and its cleanup has already run.
            if (token.IsCancellationRequested || !IsOpen)
            {
                await DeleteTemporaryIconAsync(newTemporaryPath);
                return;
            }

            var previousTemporaryPath = Interlocked.Exchange(ref _temporaryIconPath, newTemporaryPath);
            newTemporaryPath = null;

            AvatarPreview.ImagePath = _temporaryIconPath;
            RemoveIconButton.Visibility = Visibility.Visible;

            // Replacing a pick leaves the earlier file with nothing pointing at it.
            await DeleteTemporaryIconAsync(previousTemporaryPath);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await DeleteTemporaryIconAsync(newTemporaryPath);
        }
        catch (AgentIconException exception)
        {
            ShowIconError(DescribeIconError(exception.Error));
        }
        catch
        {
            ShowIconError(LocalizedStrings.Get(
                "AgentIconProcessFailed", "The selected image could not be processed."));
        }
        finally
        {
            EndIconOperation();
            if (Volatile.Read(ref _disposed) == 0) SetIconControlsBusy(false, showProgress: false);
        }
    }

    private async void RemoveIcon_Click(object sender, RoutedEventArgs e) => await RemoveIconAsync();

    private async Task RemoveIconAsync()
    {
        if (Vm.IsBusy || !TryBeginIconOperation()) return;

        HideIconError();
        try
        {
            var temporaryPath = Interlocked.Exchange(ref _temporaryIconPath, null);
            ClearIconPreview();
            await DeleteTemporaryIconAsync(temporaryPath);
        }
        finally
        {
            EndIconOperation();
        }
    }

    /// <summary>
    /// Hands the picked icon to the agent being created. Runs inside
    /// <see cref="AddAgentViewModel.AddAsync"/> so the path is written in the same save as the
    /// agent itself; see that method's remarks for why it is a callback.
    /// </summary>
    private async Task<string?> CommitIconForNewAgentAsync(string agentId, CancellationToken cancellationToken)
    {
        var temporaryPath = Volatile.Read(ref _temporaryIconPath);
        if (string.IsNullOrWhiteSpace(temporaryPath)) return null;

        try
        {
            var committedPath = await AppServices.AgentIcons.CommitTemporaryIconAsync(
                agentId, temporaryPath, cancellationToken);

            // The commit moved the file, so the temporary cleanup paths must forget it.
            Interlocked.CompareExchange(ref _temporaryIconPath, null, temporaryPath);
            _committedIconPath = committedPath;
            return committedPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AgentIconException exception)
        {
            // The agent is still worth creating; it just keeps its initial avatar.
            ShowIconError(DescribeIconError(exception.Error));
            return null;
        }
        catch
        {
            ShowIconError(LocalizedStrings.Get(
                "AgentIconSaveFailed", "The agent was added, but its icon could not be saved."));
            return null;
        }
    }

    private bool TryBeginIconOperation() => Interlocked.CompareExchange(ref _iconBusy, 1, 0) == 0;

    private void EndIconOperation() => Volatile.Write(ref _iconBusy, 0);

    private void SetIconControlsBusy(bool isBusy, bool showProgress)
    {
        ChooseIconButton.IsEnabled = !isBusy;
        RemoveIconButton.IsEnabled = !isBusy;
        IconProgressRing.IsActive = isBusy && showProgress;
        IconProgressRing.Visibility = isBusy && showProgress ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Returns the form to "no icon chosen" and deletes whatever was pending.</summary>
    private void ResetIcon()
    {
        DiscardTemporaryIcon();
        _committedIconPath = null;
        EndIconOperation();
        ClearIconPreview();
        HideIconError();
        SetIconControlsBusy(false, showProgress: false);
    }

    private void ClearIconPreview()
    {
        AvatarPreview.ImagePath = null;
        RemoveIconButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>Fire-and-forget deletion for the synchronous lifecycle methods, which cannot await.</summary>
    private void DiscardTemporaryIcon()
    {
        var temporaryPath = Interlocked.Exchange(ref _temporaryIconPath, null);
        if (!string.IsNullOrWhiteSpace(temporaryPath)) _ = DeleteTemporaryIconAsync(temporaryPath);
    }

    private async Task DiscardCommittedIconAsync()
    {
        var committedPath = Interlocked.Exchange(ref _committedIconPath, null);
        if (string.IsNullOrWhiteSpace(committedPath)) return;

        try
        {
            await AppServices.AgentIcons.DeleteIconAsync(committedPath);
        }
        catch
        {
            // Nothing references the file; an orphan is the acceptable outcome here.
        }
    }

    private static async Task DeleteTemporaryIconAsync(string? temporaryPath)
    {
        if (string.IsNullOrWhiteSpace(temporaryPath)) return;

        try
        {
            await AppServices.AgentIcons.DeleteTemporaryIconAsync(temporaryPath);
        }
        catch
        {
            // Cleanup must never break closing the modal or shutting the app down; the startup
            // sweep in AppStorage.PurgeTemporaryAgentIcons picks up whatever is left behind.
        }
    }

    private void ShowIconError(string message)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        IconErrorText.Text = message;
        IconErrorText.Visibility = Visibility.Visible;
    }

    private void HideIconError()
    {
        IconErrorText.Text = string.Empty;
        IconErrorText.Visibility = Visibility.Collapsed;
    }

    /// <summary>Maps a failure code to text. The service raises codes rather than messages so the
    /// wording lives here, next to the rest of this form's UI strings.</summary>
    private static string DescribeIconError(AgentIconError error) => error switch
    {
        AgentIconError.WindowUnavailable or AgentIconError.PickerFailed =>
            LocalizedStrings.Get("AgentIconPickerFailed", "The image picker could not be opened."),
        AgentIconError.UnsupportedFileType =>
            LocalizedStrings.Get("AgentIconUnsupportedType", "Choose a PNG, JPG, JPEG or WebP image."),
        AgentIconError.FileTooLarge =>
            LocalizedStrings.Get("AgentIconFileTooLarge", "The image must be 5 MB or smaller."),
        AgentIconError.ImageTooLarge =>
            LocalizedStrings.Get("AgentIconImageTooLarge", "The image's dimensions are too large."),
        AgentIconError.InvalidImage =>
            LocalizedStrings.Get("AgentIconInvalidImage", "That file is not an image this app can read."),
        AgentIconError.DeleteFailed =>
            LocalizedStrings.Get("AgentIconDeleteFailed", "The agent icon could not be removed."),
        _ =>
            LocalizedStrings.Get("AgentIconSaveFailed", "The agent icon could not be saved."),
    };

    private void Cancel_Click(object sender, RoutedEventArgs e) => Hide();

    private void InputBox_LostFocus(object sender, RoutedEventArgs e)
        => Vm.MarkInteracted();

    private async void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || Vm.IsBusy || Volatile.Read(ref _iconBusy) != 0) return;
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
