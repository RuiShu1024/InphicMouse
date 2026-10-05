using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using InphicMouse.Protocol.Services;

namespace InphicMouse.App.ViewModels;

/// <summary>单个 DPI 档的可编辑项（含 RGB 调色）。</summary>
public sealed partial class DpiStageItem : ObservableObject
{
    public int Index { get; }
    [ObservableProperty] private int _value;
    [ObservableProperty] private Color _pickerColor;
    /// <summary>是否在"档位数"范围内（超出则整行隐藏）。</summary>
    [ObservableProperty] private bool _isActive = true;

    public DpiStageItem(int index, int value, string colorHex)
    {
        Index = index; _value = value; _pickerColor = ColorHelpers.FromHex(colorHex);
    }

    public string Label => $"DPI {Index}";
    public string ColorHex => ColorHelpers.ToHex(PickerColor);
    public SolidColorBrush ColorBrush => new(PickerColor);
    public Visibility RowVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;

    partial void OnPickerColorChanged(Color value)
    {
        OnPropertyChanged(nameof(ColorHex));
        OnPropertyChanged(nameof(ColorBrush));
    }

    partial void OnIsActiveChanged(bool value) => OnPropertyChanged(nameof(RowVisibility));
}

public sealed partial class DpiViewModel : ObservableObject
{
    /// <summary>DPI 档数上限（协议 0x03 的 count 字段 4 位，官方最多 6）。</summary>
    public const int MaxStages = 6;

    private readonly IMouseService _svc;
    private readonly DispatcherQueue _dispatcher;

    /// <summary>全部 6 个档位槽（编辑用；超出"档位数"的行隐藏）。</summary>
    public ObservableCollection<DpiStageItem> Stages { get; } = new();

    /// <summary>当前生效的前 N 档（供"当前档"下拉使用）。</summary>
    public ObservableCollection<DpiStageItem> ActiveStages { get; } = new();

    [ObservableProperty] private int _stageCount = MaxStages;
    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private string _status = "";

    public DpiViewModel(IMouseService svc)
    {
        _svc = svc;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Load();
        _svc.StatusChanged += OnStatusChanged;
    }

    // 实时监测：设备/物理 DPI 键改变当前档时，只同步当前档指示，不覆盖用户正在编辑的数值。
    private void OnStatusChanged(object? sender, EventArgs e) =>
        _dispatcher.TryEnqueue(() =>
        {
            int idx = _svc.Settings.CurrentDpiIndex;
            if (idx >= 0 && idx < ActiveStages.Count && idx != CurrentIndex) CurrentIndex = idx;
        });

    public void Detach() => _svc.StatusChanged -= OnStatusChanged;

    partial void OnStageCountChanged(int value) => RebuildActive();

    /// <summary>按"档位数"重算生效档：<c>IsActive</c> 控制行显隐，"当前档"下拉只列前 N 档。</summary>
    private void RebuildActive()
    {
        int n = Math.Clamp(StageCount, 1, Math.Max(1, Stages.Count));
        for (int i = 0; i < Stages.Count; i++) Stages[i].IsActive = i < n;

        ActiveStages.Clear();
        for (int i = 0; i < n; i++) ActiveStages.Add(Stages[i]);

        if (CurrentIndex > n - 1) CurrentIndex = n - 1;
        if (CurrentIndex < 0) CurrentIndex = 0;
    }

    private void Load()
    {
        Stages.Clear();
        ActiveStages.Clear();
        var s = _svc.Settings;
        var caps = _svc.Capabilities;

        // 始终铺满 6 个槽：优先用设备读回值，其次型号默认，最后兜底。
        for (int i = 0; i < MaxStages; i++)
        {
            int val = i < s.DpiValues.Count ? s.DpiValues[i]
                    : (caps is not null && i < caps.DpiStages.Count ? caps.DpiStages[i].Value : 800);
            string hex = caps is not null && i < caps.DpiStages.Count ? caps.DpiStages[i].ColorHex
                       : ColorUtil.ToHex(i < s.DpiColors.Count ? s.DpiColors[i] : 0xFFFFFF);
            Stages.Add(new DpiStageItem(i + 1, val, hex));
        }

        int count = s.DpiValues.Count > 0 ? s.DpiValues.Count : MaxStages;
        _stageCount = Math.Clamp(count, 1, MaxStages);   // 直接写字段，避免中途触发 RebuildActive
        RebuildActive();
        CurrentIndex = Math.Clamp(s.CurrentDpiIndex, 0, ActiveStages.Count - 1);
    }

    [RelayCommand]
    private async Task Apply()
    {
        var s = _svc.Settings;
        s.DpiValues.Clear();
        s.DpiColors.Clear();
        // 颜色帧固定下发 6 档(0x12)；档位帧按"档位数"下发(count)。
        foreach (var st in Stages) s.DpiColors.Add(ColorUtil.FromHex(st.ColorHex));
        foreach (var st in ActiveStages) s.DpiValues.Add(st.Value);
        s.CurrentDpiIndex = Math.Clamp(CurrentIndex, 0, ActiveStages.Count - 1);

        bool ok = await _svc.ApplyDpiAsync();
        ok &= await _svc.ApplyDpiColorsAsync();
        Status = ok ? $"已下发 DPI 设置（{ActiveStages.Count} 档）" : "下发失败";
    }

    [RelayCommand]
    private async Task Reset()
    {
        await _svc.ResetDpiAsync();
        Load();
        Status = "已恢复默认 DPI";
    }
}