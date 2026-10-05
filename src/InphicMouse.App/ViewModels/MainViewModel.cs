using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using InphicMouse.Protocol.Services;

namespace InphicMouse.App.ViewModels;

/// <summary>主壳 VM：顶部状态栏（连接/模式/电量/固件）。设备服务内部有 ~1s 插拔监视+刷新，这里只随其 StatusChanged 更新。</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IMouseService _svc;
    private readonly DispatcherQueue _dispatcher;

    public MainViewModel(IMouseService svc)
    {
        _svc = svc;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _svc.StatusChanged += OnStatusChanged;
        Refresh();
    }

    public string ModelName => string.IsNullOrEmpty(_svc.Status.ModelName) ? "未连接" : _svc.Status.ModelName;
    public bool IsConnected => _svc.Status.IsConnected;
    public bool IsSimulated => _svc.Status.IsSimulated;
    public string ConnectionText => !_svc.Status.IsConnected ? "未连接"
        : _svc.Status.IsSimulated ? "演示模式（未检测到鼠标）"
        : !_svc.Status.IsOnline ? "无设备（鼠标休眠）"
        : "已连接";
    public string ModeText => _svc.Status.ModeText;
    public int BatteryPercent => _svc.Status.BatteryPercent;
    public string BatteryText => _svc.Status.IsCharging ? $"{BatteryPercent}% ⚡" : $"{BatteryPercent}%";
    public string FirmwareText => _svc.Status.FirmwareText;

    private void OnStatusChanged(object? sender, EventArgs e) =>
        _dispatcher.TryEnqueue(Refresh);

    private void Refresh()
    {
        OnPropertyChanged(nameof(ModelName));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsSimulated));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(ModeText));
        OnPropertyChanged(nameof(BatteryPercent));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(FirmwareText));
    }
}
