using System;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ConnectOnion.WinUIClient;

/// <summary><see cref="MainWindow"/> position restore and normal-state tracking.</summary>
public sealed partial class MainWindow
{
    private WindowPosition? _lastNormalPosition;
    private bool _lastNonMinimizedWasMaximized;

    private void InitializeWindowPlacement()
    {
        if (_windowPlacementStore.Current is { } saved)
        {
            var restored = ClampToVisibleWorkArea(saved.Position);
            _appWindow.Move(new PointInt32(restored.X, restored.Y));
            _lastNormalPosition = restored;
            _lastNonMinimizedWasMaximized = saved.IsMaximized;
            if (saved.IsMaximized && _appWindow.Presenter is OverlappedPresenter presenter)
                presenter.Maximize();
        }
        else
        {
            _lastNormalPosition = new WindowPosition(_appWindow.Position.X, _appWindow.Position.Y);
        }

        _appWindow.Changed += OnAppWindowPlacementChanged;
    }

    private void OnAppWindowPlacementChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (sender.Presenter is OverlappedPresenter statePresenter)
        {
            if (statePresenter.State == OverlappedPresenterState.Maximized)
                _lastNonMinimizedWasMaximized = true;
            else if (statePresenter.State == OverlappedPresenterState.Restored)
                _lastNonMinimizedWasMaximized = false;
        }

        // A maximized/minimized AppWindow reports the presenter's temporary coordinates. Retain
        // the last restored position instead, so exiting while maximized still reopens on the
        // monitor and location where the normal window actually lived.
        if (!args.DidPositionChange
            || sender.Presenter is not OverlappedPresenter presenter
            || presenter.State != OverlappedPresenterState.Restored)
        {
            return;
        }

        _lastNormalPosition = new WindowPosition(sender.Position.X, sender.Position.Y);
    }

    private WindowPosition ClampToVisibleWorkArea(WindowPosition saved)
    {
        try
        {
            var point = new PointInt32(saved.X, saved.Y);
            var display = DisplayArea.GetFromPoint(point, DisplayAreaFallback.Nearest);
            var work = display.WorkArea;
            var size = _appWindow.Size;

            // Keep the whole window inside the work area whenever it fits. If it is larger than
            // the current display (for example after a resolution/DPI change), anchor it at the
            // work area's top-left so its caption and resize affordances remain reachable.
            return WindowPlacementPolicy.ClampToWorkArea(
                saved,
                new PixelSize(size.Width, size.Height),
                new PixelRect(work.X, work.Y, work.Width, work.Height));
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Saved window position could not be validated");
            return saved;
        }
    }

    internal Task SaveWindowPlacementAsync()
    {
        if (System.Threading.Interlocked.Exchange(ref _windowPlacementSaved, 1) != 0)
            return Task.CompletedTask;

        _appWindow.Changed -= OnAppWindowPlacementChanged;
        var position = _lastNormalPosition
            ?? new WindowPosition(_appWindow.Position.X, _appWindow.Position.Y);
        var isMaximized = _appWindow.Presenter is OverlappedPresenter presenter
            ? presenter.State == OverlappedPresenterState.Maximized
                || presenter.State == OverlappedPresenterState.Minimized && _lastNonMinimizedWasMaximized
            : _lastNonMinimizedWasMaximized;
        return _windowPlacementStore.SaveAsync(new WindowPlacement(position, isMaximized));
    }
}
