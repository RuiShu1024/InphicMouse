using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using InphicMouse.Protocol.Services;

namespace InphicMouse.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IMouseService _svc;
    private readonly DispatcherQueue _dispatcher;

    // 实机确认：休眠原始值 = 分钟 × 6 (1分钟=6, 2分钟=12, 5分钟=30)。UI 用分钟。
    [ObservableProperty] private int _sleepMinutes;
    [ObservableProperty] private bool _moveWakeup;
    [ObservableProperty] private bool _moveCloseLight;
    [ObservableProperty] private int _buttonRespondTime;
    [ObservableProperty] private int _liftoffHeight;
    [ObservableProperty] private string _status = "";

    public SettingsViewModel(IMouseService svc)
    {
        _svc = svc;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Load();
        // 不订阅定时刷新：休眠/传感器不会自行变化，避免轮询覆盖用户正在输入的值。
        // 每次进入本页都会由 DI 重建 VM 并从最新 Settings(连接/刷新时已读回)加载。
    }

    // ---- 外观：跟随系统 / 亮色 / 暗色（即时生效并持久化到 settings.json）----
    public bool IsThemeSystem
    {
        get => AppPrefs.ThemeMode == AppThemeMode.System;
        set { if (value) SetTheme(AppThemeMode.System); }
    }
    public bool IsThemeLight
    {
        get => AppPrefs.ThemeMode == AppThemeMode.Light;
        set { if (value) SetTheme(AppThemeMode.Light); }
    }
    public bool IsThemeDark
    {
        get => AppPrefs.ThemeMode == AppThemeMode.Dark;
        set { if (value) SetTheme(AppThemeMode.Dark); }
    }
    private void SetTheme(AppThemeMode mode)
    {
        if (AppPrefs.ThemeMode == mode) return;
        AppPrefs.ThemeMode = mode;
        AppPrefs.Save();
        App.ApplyTheme();
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
    }

    private void OnStatusChanged(object? sender, System.EventArgs e) => _dispatcher.TryEnqueue(Load);

    public void Detach() => _svc.StatusChanged -= OnStatusChanged;

    private void Load()
    {
        var s = _svc.Settings;
        SleepMinutes = s.SleepLight / 6;
        MoveWakeup = s.MoveWakeup;
        MoveCloseLight = s.MoveCloseLight;
        ButtonRespondTime = s.ButtonRespondTime;
        LiftoffHeight = s.LiftoffHeight;
    }

    [RelayCommand]
    private async Task Apply()
    {
        var s = _svc.Settings;
        s.SleepLight = SleepMinutes * 6;
        s.MoveWakeup = MoveWakeup;
        s.MoveCloseLight = MoveCloseLight;
        s.ButtonRespondTime = ButtonRespondTime;
        s.LiftoffHeight = LiftoffHeight;

        bool ok = await _svc.ApplySleepAsync();
        ok &= await _svc.ApplySensorAsync();
        Status = ok ? "已下发休眠/传感器设置" : "下发失败";
    }
}
