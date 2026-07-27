using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// Window-level Add Agent overlay.
///
/// Network, validation, and Agent persistence state live in
/// <see cref="AddAgentViewModel"/>.
///
/// This class owns:
/// - modal lifecycle;
/// - keyboard and pointer input;
/// - Agent-icon selection and preview;
/// - temporary icon cleanup;
/// - committing the selected icon after the Agent is created.
/// </summary>
public sealed partial class AddAgentForm : UserControl, IDisposable
{
    private readonly IAgentIconService _agentIconService;

    private CancellationTokenSource? _operationCts;
    private FrameworkElement? _focusReturnTarget;
    private string? _temporaryIconPath;

    private int _disposed;
    private int _iconBusy;

    public AddAgentViewModel Vm { get; } =
        App.GetService<AddAgentViewModel>();

    public event Action<AgentConfig>? AgentAdded;

    public bool IsOpen =>
        Visibility == Visibility.Visible;

    private bool IsIconBusy =>
        Volatile.Read(ref _iconBusy) != 0;

    public AddAgentForm()
    {
        InitializeComponent();

        _agentIconService = AppServices.AgentIcons;

        ResetIconUi();
    }

    /// <summary>
    /// Opens and resets the Add Agent modal.
    /// </summary>
    public void Show(FrameworkElement? focusReturnTarget)
    {
        ThrowIfDisposed();

        _focusReturnTarget = focusReturnTarget;

        if (!IsOpen)
        {
            CancelPendingOperation();

            // Clean up any temporary image left by an earlier interrupted form.
            var staleTemporaryPath =
                Interlocked.Exchange(
                    ref _temporaryIconPath,
                    null);

            if (!string.IsNullOrWhiteSpace(staleTemporaryPath))
            {
                _ = DeleteTemporaryIconBestEffortAsync(
                    staleTemporaryPath);
            }

            Vm.Reset();
            ResetIconUi();

            _operationCts =
                new CancellationTokenSource();

            Visibility = Visibility.Visible;
            IsHitTestVisible = true;
        }

        DispatcherQueue.TryEnqueue(
            () => InputBox.Focus(
                FocusState.Programmatic));
    }

    /// <summary>
    /// Closes the modal and removes any uncommitted temporary icon.
    /// </summary>
    public void Hide()
    {
        if (!IsOpen)
        {
            return;
        }

        CancelPendingOperation();

        var temporaryPath =
            Interlocked.Exchange(
                ref _temporaryIconPath,
                null);

        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;

        Vm.Reset();
        ResetIconUi();

        if (!string.IsNullOrWhiteSpace(temporaryPath))
        {
            _ = DeleteTemporaryIconBestEffortAsync(
                temporaryPath);
        }

        _focusReturnTarget?.Focus(
            FocusState.Programmatic);

        _focusReturnTarget = null;
    }

    /// <summary>
    /// Cancels the current connection, add, or image-selection operation.
    /// </summary>
    public void CancelPendingOperation()
    {
        var cancellationSource =
            Interlocked.Exchange(
                ref _operationCts,
                null);

        if (cancellationSource is null)
        {
            return;
        }

        try
        {
            cancellationSource.Cancel();
        }
        finally
        {
            cancellationSource.Dispose();
        }
    }

    public void Shutdown()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        CancelPendingOperation();

        var temporaryPath =
            Interlocked.Exchange(
                ref _temporaryIconPath,
                null);

        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;

        _focusReturnTarget = null;
        AgentAdded = null;

        if (!string.IsNullOrWhiteSpace(temporaryPath))
        {
            _ = DeleteTemporaryIconBestEffortAsync(
                temporaryPath);
        }
    }

    // ---------------------------------------------------------------------
    // Connection testing
    // ---------------------------------------------------------------------

    private async void TestConnection_Click(
        object sender,
        RoutedEventArgs e)
    {
        await TestConnectionAsync();
    }

    private async Task TestConnectionAsync()
    {
        if (IsIconBusy)
        {
            return;
        }

        var token =
            _operationCts?.Token
            ?? CancellationToken.None;

        try
        {
            await Vm.TestConnectionAsync(token);
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            // Closing the modal cancels the active operation.
        }
    }

    // ---------------------------------------------------------------------
    // Icon selection
    // ---------------------------------------------------------------------

    private async void ChooseIcon_Click(
        object sender,
        RoutedEventArgs e)
    {
        await ChooseIconAsync();
    }

    private async Task ChooseIconAsync()
    {
        if (Vm.IsBusy || !TryBeginIconOperation())
        {
            return;
        }

        HideIconError();
        SetIconControlsBusy(
            isBusy: true,
            showProgress: true);

        var token =
            _operationCts?.Token
            ?? CancellationToken.None;

        string? newTemporaryPath = null;

        try
        {
            newTemporaryPath =
                await _agentIconService
                    .PickTemporaryIconAsync(token);

            // Closing the picker without selecting a file is not an error.
            if (newTemporaryPath is null)
            {
                return;
            }

            if (token.IsCancellationRequested || !IsOpen)
            {
                await DeleteTemporaryIconBestEffortAsync(
                    newTemporaryPath);

                return;
            }

            var previousTemporaryPath =
                Interlocked.Exchange(
                    ref _temporaryIconPath,
                    newTemporaryPath);

            AvatarPreview.ImagePath =
                newTemporaryPath;

            RemoveIconButton.Visibility =
                Visibility.Visible;

            if (!string.IsNullOrWhiteSpace(
                    previousTemporaryPath)
                && !string.Equals(
                    previousTemporaryPath,
                    newTemporaryPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                await DeleteTemporaryIconBestEffortAsync(
                    previousTemporaryPath);
            }
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            if (!string.IsNullOrWhiteSpace(
                    newTemporaryPath))
            {
                await DeleteTemporaryIconBestEffortAsync(
                    newTemporaryPath);
            }
        }
        catch (AgentIconException exception)
        {
            ShowIconError(
                GetIconErrorMessage(exception.Error));
        }
        catch
        {
            ShowIconError(
                "The selected image could not be processed.");
        }
        finally
        {
            EndIconOperation();

            if (Volatile.Read(ref _disposed) == 0)
            {
                SetIconControlsBusy(
                    isBusy: false,
                    showProgress: false);
            }
        }
    }

    private async void RemoveIcon_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RemoveIconAsync();
    }

    private async Task RemoveIconAsync()
    {
        if (Vm.IsBusy || !TryBeginIconOperation())
        {
            return;
        }

        HideIconError();

        var temporaryPath =
            Volatile.Read(ref _temporaryIconPath);

        if (string.IsNullOrWhiteSpace(temporaryPath))
        {
            EndIconOperation();
            ResetIconPreview();
            return;
        }

        SetIconControlsBusy(
            isBusy: true,
            showProgress: true);

        var token =
            _operationCts?.Token
            ?? CancellationToken.None;

        try
        {
            await _agentIconService
                .DeleteTemporaryIconAsync(
                    temporaryPath,
                    token);

            Interlocked.CompareExchange(
                ref _temporaryIconPath,
                null,
                temporaryPath);

            ResetIconPreview();
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            // Modal closure handles remaining cleanup.
        }
        catch (AgentIconException exception)
        {
            ShowIconError(
                GetIconErrorMessage(exception.Error));
        }
        catch
        {
            ShowIconError(
                "The selected Agent icon could not be removed.");
        }
        finally
        {
            EndIconOperation();

            if (Volatile.Read(ref _disposed) == 0)
            {
                SetIconControlsBusy(
                    isBusy: false,
                    showProgress: false);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Agent creation
    // ---------------------------------------------------------------------

    private async void AddAgent_Click(
        object sender,
        RoutedEventArgs e)
    {
        await AddAgentAsync();
    }

    private async Task AddAgentAsync()
    {
        if (!Vm.CanAdd || !TryBeginIconOperation())
        {
            return;
        }

        var token =
            _operationCts?.Token
            ?? CancellationToken.None;

        // Prevent image replacement while the Agent is being created.
        SetIconControlsBusy(
            isBusy: true,
            showProgress: false);

        try
        {
            var agent =
                await Vm.AddAsync(token);

            if (agent is null
                || token.IsCancellationRequested
                || !IsOpen)
            {
                return;
            }

            var temporaryPath =
                Volatile.Read(ref _temporaryIconPath);

            if (!string.IsNullOrWhiteSpace(
                    temporaryPath))
            {
                await AttachIconToNewAgentAsync(
                    agent,
                    temporaryPath,
                    token);
            }

            if (token.IsCancellationRequested || !IsOpen)
            {
                return;
            }

            AgentAdded?.Invoke(agent);
            Hide();
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            // Closing the modal cancels the operation.
        }
        finally
        {
            EndIconOperation();

            if (Volatile.Read(ref _disposed) == 0
                && IsOpen)
            {
                SetIconControlsBusy(
                    isBusy: false,
                    showProgress: false);
            }
        }
    }

    /// <summary>
    /// Commits the temporary image and performs a second repository save to
    /// attach its relative path to the Agent created by the current
    /// AddAgentViewModel implementation.
    ///
    /// The Agent itself remains valid if icon processing fails. In that case
    /// the control falls back to the generated initial avatar.
    /// </summary>
    private async Task AttachIconToNewAgentAsync(
        AgentConfig agent,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        string? committedRelativePath = null;

        try
        {
            committedRelativePath =
                await _agentIconService
                    .CommitTemporaryIconAsync(
                        agent.Id,
                        temporaryPath,
                        cancellationToken);

            // CommitTemporaryIconAsync moves the temporary file, so it must no
            // longer be deleted as a temporary file when the form closes.
            Interlocked.CompareExchange(
                ref _temporaryIconPath,
                null,
                temporaryPath);

            await PersistAgentIconPathAsync(
                agent,
                committedRelativePath,
                cancellationToken);

            AvatarPreview.ImagePath =
                committedRelativePath;
        }
        catch (OperationCanceledException)
        {
            if (!string.IsNullOrWhiteSpace(
                    committedRelativePath))
            {
                await DeleteCommittedIconBestEffortAsync(
                    committedRelativePath);
            }

            throw;
        }
        catch (AgentIconException exception)
        {
            if (!string.IsNullOrWhiteSpace(
                    committedRelativePath))
            {
                await DeleteCommittedIconBestEffortAsync(
                    committedRelativePath);
            }

            agent.IconPath = null;

            // The Agent was already created successfully. A failed optional
            // icon must not create a duplicate Agent if the user retries.
            ShowIconError(
                GetIconErrorMessage(exception.Error));
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(
                    committedRelativePath))
            {
                await DeleteCommittedIconBestEffortAsync(
                    committedRelativePath);
            }

            agent.IconPath = null;

            // The Agent remains usable with its fallback initial avatar.
            ShowIconError(
                "The Agent was added, but its custom icon could not be saved.");
        }
    }

    /// <summary>
    /// Updates the newly-created Agent row with its committed icon path.
    /// </summary>
    private static async Task PersistAgentIconPathAsync(
        AgentConfig agent,
        string relativeIconPath,
        CancellationToken cancellationToken)
    {
        var state =
            await AppServices.Agents
                .LoadAsync(cancellationToken);

        var storedAgent =
            state.Agents.FirstOrDefault(
                item => string.Equals(
                    item.Id,
                    agent.Id,
                    StringComparison.Ordinal));

        if (storedAgent is null)
        {
            throw new InvalidOperationException(
                "The newly-created Agent could not be found in local storage.");
        }

        storedAgent.IconPath =
            relativeIconPath;

        await AppServices.Agents
            .SaveAsync(
                state,
                cancellationToken);

        // Keep the instance sent through AgentAdded synchronized with SQLite.
        agent.IconPath =
            relativeIconPath;
    }

    // ---------------------------------------------------------------------
    // Existing modal input handlers
    // ---------------------------------------------------------------------

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        Hide();
    }

    private void InputBox_LostFocus(
        object sender,
        RoutedEventArgs e)
    {
        Vm.MarkInteracted();
    }

    private async void InputBox_KeyDown(
        object sender,
        KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter
            || Vm.IsBusy
            || IsIconBusy)
        {
            return;
        }

        e.Handled = true;

        if (Vm.CanAdd)
        {
            await AddAgentAsync();
        }
        else
        {
            await TestConnectionAsync();
        }
    }

    private void OverlayRoot_KeyDown(
        object sender,
        KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        Hide();
    }

    private void Backdrop_Tapped(
        object sender,
        TappedRoutedEventArgs e)
    {
        Hide();
    }

    private void ModalContainer_Tapped(
        object sender,
        TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    // ---------------------------------------------------------------------
    // Icon UI helpers
    // ---------------------------------------------------------------------

    private bool TryBeginIconOperation()
    {
        return Interlocked.CompareExchange(
                   ref _iconBusy,
                   1,
                   0)
               == 0;
    }

    private void EndIconOperation()
    {
        Volatile.Write(
            ref _iconBusy,
            0);
    }

    private void SetIconControlsBusy(
        bool isBusy,
        bool showProgress)
    {
        ChooseIconButton.IsEnabled =
            !isBusy;

        RemoveIconButton.IsEnabled =
            !isBusy;

        IconProgressRing.IsActive =
            isBusy && showProgress;

        IconProgressRing.Visibility =
            isBusy && showProgress
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void ResetIconUi()
    {
        EndIconOperation();
        ResetIconPreview();
        HideIconError();

        SetIconControlsBusy(
            isBusy: false,
            showProgress: false);
    }

    private void ResetIconPreview()
    {
        AvatarPreview.ImagePath = null;
        AvatarPreview.Initial = "?";

        RemoveIconButton.Visibility =
            Visibility.Collapsed;
    }

    private void ShowIconError(string message)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        IconErrorText.Text =
            message;

        IconErrorText.Visibility =
            Visibility.Visible;
    }

    private void HideIconError()
    {
        IconErrorText.Text =
            string.Empty;

        IconErrorText.Visibility =
            Visibility.Collapsed;
    }

    private static string GetIconErrorMessage(
        AgentIconError error)
    {
        return error switch
        {
            AgentIconError.WindowUnavailable =>
                "The image picker could not be opened.",

            AgentIconError.UnsupportedFileType =>
                "Choose a PNG, JPG, JPEG, or WebP image.",

            AgentIconError.FileTooLarge =>
                "The selected image must be 5 MB or smaller.",

            AgentIconError.ImageTooLarge =>
                "The selected image dimensions are too large.",

            AgentIconError.InvalidImage =>
                "The selected file is not a valid image.",

            AgentIconError.TemporaryFileMissing =>
                "The temporary Agent icon could not be found.",

            AgentIconError.InvalidTemporaryPath =>
                "The temporary Agent icon path is invalid.",

            AgentIconError.SaveFailed =>
                "The Agent icon could not be saved.",

            AgentIconError.DeleteFailed =>
                "The Agent icon could not be removed.",

            _ =>
                "The selected image could not be used.",
        };
    }

    // ---------------------------------------------------------------------
    // Best-effort cleanup
    // ---------------------------------------------------------------------

    private async Task DeleteTemporaryIconBestEffortAsync(
        string? temporaryPath)
    {
        if (string.IsNullOrWhiteSpace(temporaryPath))
        {
            return;
        }

        try
        {
            await _agentIconService
                .DeleteTemporaryIconAsync(
                    temporaryPath,
                    CancellationToken.None);
        }
        catch
        {
            // Cleanup must not crash modal closure or application shutdown.
        }
    }

    private async Task DeleteCommittedIconBestEffortAsync(
        string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        try
        {
            await _agentIconService
                .DeleteIconAsync(
                    relativePath,
                    CancellationToken.None);
        }
        catch
        {
            // Cleanup after a failed database update is best-effort.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed != 0,
            this);
    }
}
