using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using System.Runtime.InteropServices.WindowsRuntime;

namespace ConnectOnion.WinUIClient.ViewModels;

public sealed partial class SettingsViewModel : Common.ObservableObject
{
    private readonly PreferencesRepository _repo;
    private PreferencesSnapshot _prefs = new();
    private bool _loaded;

    public ThemeMode Theme => _prefs.Theme;
    public MessageFontSize MessageFontSize => _prefs.MessageFontSize;
    public bool EnterToSend => _prefs.EnterToSend;
    public string MicrophoneDeviceId => _prefs.MicrophoneDeviceId;

    public IReadOnlyList<MicrophoneItem> Microphones { get; private set; } = [];

    // -1 default seeded in the ctor (partial-property declarations can't carry an initializer);
    // it fires the hook while _loaded is still false, so it no-ops.
    public SettingsViewModel(PreferencesRepository preferences)
    {
        _repo = preferences;
        SelectedMicIndex = -1;
    }

    [ObservableProperty]
    public partial int SelectedMicIndex { get; set; }

    // Only a user-driven change persists a new device; the initial selection in LoadAsync is set
    // while _loaded is still false, so this hook no-ops for it (the load happens before _loaded is
    // flipped, precisely so restoring the saved index doesn't re-save it).
    partial void OnSelectedMicIndexChanged(int value)
    {
        if (_loaded && value >= 0 && value < Microphones.Count)
            _ = SetMicrophoneAsync(Microphones[value].DeviceId);
    }

    public async Task LoadAsync()
    {
        _prefs = await _repo.LoadAsync();

        Microphones = await EnumerateMicrophonesAsync();

        var idx = Microphones
            .Select((m, i) => (m, i))
            .FirstOrDefault(x => x.m.DeviceId == _prefs.MicrophoneDeviceId).i;
        if (idx < 0)
            idx = Microphones.Select((m, i) => (m, i))
                .FirstOrDefault(x => x.m.IsDefault).i;
        SelectedMicIndex = idx >= 0 ? idx : 0;

        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(MessageFontSize));
        OnPropertyChanged(nameof(EnterToSend));
        OnPropertyChanged(nameof(Microphones));

        // Flipped last: only now do user-driven SelectedMicIndex changes persist (see the hook).
        _loaded = true;
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

    private async Task SetMicrophoneAsync(string deviceId)
    {
        if (!_loaded || _prefs.MicrophoneDeviceId == deviceId) return;
        _prefs.MicrophoneDeviceId = deviceId;
        await _repo.SaveAsync(_prefs);
    }

    private static async Task<IReadOnlyList<MicrophoneItem>> EnumerateMicrophonesAsync()
    {
        var devices = await Task.Run(() =>
        {
            var op = DeviceInformation.FindAllAsync(DeviceClass.AudioCapture);
            var tcs = new TaskCompletionSource<DeviceInformationCollection>();
            op.Completed = (_, status) =>
            {
                if (status == AsyncStatus.Completed)
                    tcs.SetResult(op.GetResults());
                else if (status == AsyncStatus.Error)
                    tcs.SetException(op.ErrorCode);
                else
                    tcs.SetCanceled();
            };
            return tcs.Task;
        });
        var list = new List<MicrophoneItem>(devices.Count + 1)
        {
            new("System default", "", IsDefault: true),
        };
        foreach (var d in devices)
        {
            if (d.IsEnabled)
                list.Add(new(d.Name, d.Id));
        }
        return list;
    }
}

public sealed record MicrophoneItem(string Name, string DeviceId, bool IsDefault = false);
