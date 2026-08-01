using System;
using ConnectOnion.WinUIClient.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient;

/// <summary>Lazy construction and lifetime wiring for shell-owned full-window modals.</summary>
public sealed partial class MainWindow
{
    private AddAgentForm? _addAgentOverlay;
    private SettingsOverlay? _settingsOverlay;
    private KeyboardShortcutsDialog? _keyboardShortcutsDialog;
    private AboutOverlay? _aboutOverlay;
    private SessionSearchOverlay? _sessionSearchOverlay;

    private AddAgentForm EnsureAddAgentOverlay()
    {
        if (_addAgentOverlay is not null) return _addAgentOverlay;
        var overlay = new AddAgentForm();
        Canvas.SetZIndex(overlay, 1004);
        overlay.AgentAdded += AddAgentOverlay_AgentAdded;
        FloatingOverlayLayer.Children.Add(overlay);
        return _addAgentOverlay = overlay;
    }

    private SettingsOverlay EnsureSettingsOverlay()
    {
        if (_settingsOverlay is not null) return _settingsOverlay;
        var overlay = new SettingsOverlay();
        Canvas.SetZIndex(overlay, 1001);
        overlay.CloseRequested += SettingsOverlay_CloseRequested;
        FloatingOverlayLayer.Children.Add(overlay);
        return _settingsOverlay = overlay;
    }

    private KeyboardShortcutsDialog EnsureKeyboardShortcutsDialog()
    {
        if (_keyboardShortcutsDialog is not null) return _keyboardShortcutsDialog;
        var overlay = new KeyboardShortcutsDialog();
        Canvas.SetZIndex(overlay, 1002);
        overlay.CloseRequested += KeyboardShortcutsDialog_CloseRequested;
        FloatingOverlayLayer.Children.Add(overlay);
        return _keyboardShortcutsDialog = overlay;
    }

    private AboutOverlay EnsureAboutOverlay()
    {
        if (_aboutOverlay is not null) return _aboutOverlay;
        var overlay = new AboutOverlay();
        Canvas.SetZIndex(overlay, 1003);
        overlay.CloseRequested += AboutOverlay_CloseRequested;
        FloatingOverlayLayer.Children.Add(overlay);
        return _aboutOverlay = overlay;
    }

    private SessionSearchOverlay EnsureSessionSearchOverlay()
    {
        if (_sessionSearchOverlay is not null) return _sessionSearchOverlay;
        var overlay = new SessionSearchOverlay();
        Canvas.SetZIndex(overlay, 1005);
        overlay.CloseRequested += SessionSearchOverlay_CloseRequested;
        overlay.SessionSelected += SessionSearchOverlay_SessionSelected;
        FloatingOverlayLayer.Children.Add(overlay);
        return _sessionSearchOverlay = overlay;
    }

    private void SettingsOverlay_CloseRequested(object? sender, EventArgs e) => CloseSettingsOverlay();
    private void KeyboardShortcutsDialog_CloseRequested(object? sender, EventArgs e) => CloseKeyboardShortcutsOverlay();
    private void AboutOverlay_CloseRequested(object? sender, EventArgs e) => CloseAboutOverlay();
    private void SessionSearchOverlay_CloseRequested(object? sender, EventArgs e) => CloseSessionSearchOverlay();

    private void ShutdownOverlays()
    {
        if (_addAgentOverlay is { } addAgent)
        {
            addAgent.AgentAdded -= AddAgentOverlay_AgentAdded;
            addAgent.Shutdown();
        }
        if (_settingsOverlay is { } settings)
        {
            settings.CloseRequested -= SettingsOverlay_CloseRequested;
            settings.Shutdown();
        }
        if (_keyboardShortcutsDialog is { } shortcuts)
            shortcuts.CloseRequested -= KeyboardShortcutsDialog_CloseRequested;
        if (_aboutOverlay is { } about) about.CloseRequested -= AboutOverlay_CloseRequested;
        if (_sessionSearchOverlay is { } search)
        {
            search.CloseRequested -= SessionSearchOverlay_CloseRequested;
            search.SessionSelected -= SessionSearchOverlay_SessionSelected;
            search.Hide();
            search.Dispose();
        }
    }
}
