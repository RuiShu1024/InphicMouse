using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using InphicMouse.Protocol.Services;

namespace InphicMouse.App.ViewModels;

/// <summary>设备信息页：型号/连接/模式/电量/固件/IC，及刷新与重连。订阅 StatusChanged 实时刷新。</summary>
public sealed partial class InfoViewModel : ObservableObject
{
    private readonly IMouseService _svc;
    private readonly DispatcherQueue _dispatcher;

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _diagnostics = "";

    public InfoViewModel(IMouseService svc)
    {
        _svc = svc;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _svc.StatusChanged += OnStatusChanged;
    }

    private void OnStatusChanged(object? sender, System.EventArgs e) => _dispatcher.TryEnqueue(RaiseAll);

    /// <summary>页面卸载时调用，断开事件订阅避免泄漏。</summary>
    public void Detach() => _svc.StatusChanged -= OnStatusChanged;

    public string ModelName => _svc.Status.ModelName;
    public string ConnectionText => !_svc.Status.IsConnected ? "未连接"
        : _svc.Status.IsSimulated ? "演示模式"
        : !_svc.Status.IsOnline ? "无设备（鼠标休眠）"
        : "已连接";
    public string ModeText => _svc.Status.ModeText;
    public string BatteryText => _svc.Status.IsCharging ? $"{_svc.Status.BatteryPercent}%（充电中）" : $"{_svc.Status.BatteryPercent}%";
    public string FirmwareText => _svc.Status.FirmwareText;
    public string IcText => _svc.Status.Info is { } i ? $"0x{i.IcType:X2}" : "—";
    public bool IsSimulated => _svc.Status.IsSimulated;

    [RelayCommand]
    private async Task Refresh()
    {
        await _svc.RefreshInfoAsync();
        RaiseAll();
        Status = "已刷新设备信息";
    }

    [RelayCommand]
    private async Task Reconnect()
    {
        bool ok = await _svc.ConnectAsync();
        RaiseAll();
        Status = ok ? "已重新连接" : "未检测到设备（仍为演示模式或未连接）";
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(ModelName));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(ModeText));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(FirmwareText));
        OnPropertyChanged(nameof(IcText));
        OnPropertyChanged(nameof(IsSimulated));
    }

    [RelayCommand]
    private async Task ExportDiagnostics()
    {
        Status = "正在采集诊断…";
        string text = await _svc.RunDiagnosticsAsync();
        Diagnostics = text;
        try
        {
            // 诊断文件固定放到 %APPDATA%\InphicMouse（不污染安装目录，保证卸载干净）
            string path = System.IO.Path.Combine(AppPaths.RoamingDir, $"诊断_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
            await System.IO.File.WriteAllTextAsync(path, text);
            Status = $"已保存到: {path}";
        }
        catch (System.Exception ex) { Status = "已显示（保存失败: " + ex.Message + "）"; }
    }

    /// <summary>把当前诊断文本复制到剪贴板，方便直接贴给开发者。</summary>
    [RelayCommand]
    private void CopyDiagnostics()
    {
        if (string.IsNullOrWhiteSpace(Diagnostics)) { Status = "暂无诊断内容，请先点「导出诊断」。"; return; }
        try
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(Diagnostics);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            Status = "诊断内容已复制到剪贴板。";
        }
        catch (System.Exception ex) { Status = "复制失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task ReadLightId()
    {
        Diagnostics = await _svc.ReadLightModeAsync();
        Status = "已读取当前灯光模式";
    }

    [RelayCommand]
    private async Task ReadMacro()
    {
        Diagnostics = await _svc.ReadMacroAsync(0);
        Status = "已读取宏1内容";
    }
}
