using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using InphicMouse.Protocol.Services;

namespace InphicMouse.App.ViewModels;

public sealed partial class LightViewModel : ObservableObject
{
    private readonly IMouseService _svc;

    public ObservableCollection<string> Modes { get; } = new();

    [ObservableProperty] private int _selectedIndex;
    [ObservableProperty] private int _brightness;
    [ObservableProperty] private int _speed;
    [ObservableProperty] private Color _pickerColor = ColorHelpers.FromHex("#FF0000");
    [ObservableProperty] private string _status = "";

    public SolidColorBrush ColorBrush => new(PickerColor);
    public string ColorHex => ColorHelpers.ToHex(PickerColor);

    partial void OnPickerColorChanged(Color value)
    {
        OnPropertyChanged(nameof(ColorBrush));
        OnPropertyChanged(nameof(ColorHex));
    }

    // 切换灯光模式时，刷新各控件可见性。
    partial void OnSelectedIndexChanged(int value) => RaiseVisibility();

    public LightViewModel(IMouseService svc)
    {
        _svc = svc;
        var caps = svc.Capabilities;
        if (caps is not null && caps.LightModes.Count > 0)
            foreach (var m in caps.LightModes)
            {
                if (m.Enabled) Modes.Add(m.Name);   // 只显示该型号启用的灯效
            }
        if (Modes.Count == 0)
            for (int i = 0; i < 6; i++) Modes.Add($"模式 {i}");

        // 进入页面即按设备当前灯光状态初始化(修复"从别的界面回来不识别当前灯光模式")。
        // LightViewModel 为 transient：每次导航到灯光页都会重建→构造时同步一次即可。
        // 不订阅 StatusChanged：~1s 轮询会在用户刚切换下拉、设备尚未写入时把选择又拨回旧模式(闪回 bug)。
        SyncFromDevice();
    }

    /// <summary>页面卸载回调（当前无订阅，保留以兼容 LightPage 调用）。</summary>
    public void Detach() { }

    /// <summary>按设备设置快照同步 模式/亮度/速度/颜色 到 UI。</summary>
    private void SyncFromDevice()
    {
        var s = _svc.Settings;
        SelectedIndex = IndexForModeId(s.LightMode);
        // 实机标定：亮度有效区间 1..4(4 最亮)，>4 固件表现异常(忽暗忽灭)，故限制并兜底 4。
        Brightness = s.LightBrightness is >= 1 and <= 4 ? s.LightBrightness : 4;
        // 速度有效区间 1..4(1 慢→4 快)，更大值过快/重复，故限制并兜底 2。
        Speed = s.LightSpeed is >= 1 and <= 4 ? s.LightSpeed : 2;
        if (s.LightColors.Count > 0)
            PickerColor = ColorHelpers.FromHex(ColorUtil.ToHex(s.LightColors[0]));
        RaiseVisibility();
    }

    /// <summary>当前选中灯效对应的固件模式 id。</summary>
    public int CurrentModeId => ModeIdFor(SelectedIndex);

    // 可见性规则(按用户要求)：
    //   关闭(0)      → 亮度/速度/颜色 全隐藏
    //   常亮(3)      → 隐藏"速度"(常亮无动画)，显示亮度+颜色
    //   呼吸(2)/霓虹(4)/其它动画效果 → 显示亮度+速度，隐藏颜色(非单色模式)
    public Visibility BrightnessVisibility => CurrentModeId == 0 ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SpeedVisibility => (CurrentModeId == 0 || CurrentModeId == 3) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ColorVisibility => CurrentModeId == 3 ? Visibility.Visible : Visibility.Collapsed;

    private void RaiseVisibility()
    {
        OnPropertyChanged(nameof(CurrentModeId));
        OnPropertyChanged(nameof(BrightnessVisibility));
        OnPropertyChanged(nameof(SpeedVisibility));
        OnPropertyChanged(nameof(ColorVisibility));
    }

    [RelayCommand]
    private async Task Apply()
    {
        var s = _svc.Settings;
        s.LightMode = ModeIdFor(SelectedIndex);
        s.LightBrightness = Brightness;
        s.LightSpeed = Speed;
        s.LightColors.Clear();
        s.LightColors.Add(ColorUtil.FromHex(ColorHex));
        bool ok = await _svc.ApplyLightAsync();
        Status = ok ? $"已下发灯光（模式 id={s.LightMode}）" : "下发失败";
    }

    /// <summary>
    /// UI 灯效名 → 固件模式 id(buf[5])。实机(IN9 3311)校准确认：
    /// 关闭=0 呼吸=2 常亮=3 霓虹=4；流光/七彩波浪该型号禁用，按序号 1/5 兜底(未校准)。
    /// 结构随 id：id2→7色 id3→单色 其余→纯效果(见 LightCodec)。
    /// </summary>
    private int ModeIdFor(int index)
    {
        string name = index >= 0 && index < Modes.Count ? Modes[index] : "";
        if (name.Contains("关闭")) return 0;
        if (name.Contains("呼吸")) return 2;
        if (name.Contains("常亮")) return 3;
        if (name.Contains("霓虹")) return 4;
        if (name.Contains("流光")) return 1;
        if (name.Contains("七彩") || name.Contains("波浪")) return 5;
        return index;
    }

    /// <summary>固件模式 id → 列表索引（ModeIdFor 的逆，用于按设备当前模式回选下拉）。</summary>
    private int IndexForModeId(int id)
    {
        for (int i = 0; i < Modes.Count; i++)
            if (ModeIdFor(i) == id) return i;
        return 0;
    }

    [RelayCommand]
    private async Task Reset()
    {
        await _svc.ResetLightAsync();
        SyncFromDevice();
        Status = "已恢复默认灯光";
    }
}
