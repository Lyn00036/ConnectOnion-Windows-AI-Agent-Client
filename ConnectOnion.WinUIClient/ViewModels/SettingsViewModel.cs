using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Data;

namespace ConnectOnion.WinUIClient.ViewModels;

/// <summary>
/// Backs the Settings page. Loads SQLite-backed preferences, exposes the current
/// appearance settings, and persists (and live-applies the theme) on each change.
/// Port of the appearance section of <c>SettingsPanel.tsx</c> / <c>store.ts</c>.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly PreferencesRepository _repo = AppServices.Preferences;
    private PreferencesSnapshot _prefs = new();
    private bool _loaded;

    public ThemeMode Theme => _prefs.Theme;
    public MessageFontSize MessageFontSize => _prefs.MessageFontSize;
    public bool EnterToSend => _prefs.EnterToSend;

    public async Task LoadAsync()
    {
        _prefs = await _repo.LoadAsync();
        _loaded = true;
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(MessageFontSize));
        OnPropertyChanged(nameof(EnterToSend));
    }

    public async Task SetThemeAsync(ThemeMode theme)
    {
        if (!_loaded || _prefs.Theme == theme) return;
        _prefs.Theme = theme;
        ThemeService.Apply(theme);
        OnPropertyChanged(nameof(Theme));
        await _repo.SaveAsync(_prefs);
    }

    public async Task SetMessageFontSizeAsync(MessageFontSize size)
    {
        if (!_loaded || _prefs.MessageFontSize == size) return;
        _prefs.MessageFontSize = size;
        OnPropertyChanged(nameof(MessageFontSize));
        await _repo.SaveAsync(_prefs);
    }

    public async Task SetEnterToSendAsync(bool enterToSend)
    {
        if (!_loaded || _prefs.EnterToSend == enterToSend) return;
        _prefs.EnterToSend = enterToSend;
        OnPropertyChanged(nameof(EnterToSend));
        await _repo.SaveAsync(_prefs);
    }
}
