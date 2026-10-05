using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InphicMouse.Protocol.Codecs;
using InphicMouse.Protocol.Services;

namespace InphicMouse.App.ViewModels;

/// <summary>
/// 单个物理键的快捷指令编辑器：选择分类→具体功能，或切到"键盘快捷键"后按下组合键捕获。
/// 最终产出一条 3 字节 <see cref="KeyMapCodec.KeyEntry"/>，与官方编辑器 1:1。
/// </summary>
public sealed partial class KeyEditItem : ObservableObject
{
    public string KeyName { get; }

    /// <summary>所有分类名（固定目录 + 键盘快捷键）。</summary>
    public ObservableCollection<string> Categories { get; } = new();

    /// <summary>当前分类下的功能选项（键盘分类时为空，改用捕获）。</summary>
    public ObservableCollection<KeyFunctionCatalog.KeyOption> Options { get; } = new();

    [ObservableProperty] private int _categoryIndex;
    [ObservableProperty] private int _optionIndex;

    /// <summary>键盘快捷键捕获结果（修饰掩码 + 主键 usage + 显示文本）。</summary>
    [ObservableProperty] private byte _capturedMask;
    [ObservableProperty] private byte _capturedUsage;
    [ObservableProperty] private string _capturedText = "（点击此处后按下组合键）";

    private readonly IReadOnlyList<KeyFunctionCatalog.KeyCategory> _cats;

    public KeyEditItem(string keyName, KeyMapCodec.KeyEntry initial,
                       IReadOnlyList<KeyFunctionCatalog.KeyOption>? macroOptions = null)
    {
        KeyName = keyName;
        // 固定目录；若有本地宏，则把"宏"分类替换成动态宏列表。
        var cats = new List<KeyFunctionCatalog.KeyCategory>();
        foreach (var c in KeyFunctionCatalog.FixedCategories)
        {
            if (c.Name == "宏" && macroOptions is { Count: > 0 })
                cats.Add(new KeyFunctionCatalog.KeyCategory("宏", macroOptions));
            else if (c.Name == "宏" && (macroOptions is null || macroOptions.Count == 0))
                cats.Add(new KeyFunctionCatalog.KeyCategory("宏", new[]
                {
                    new KeyFunctionCatalog.KeyOption("（无宏，请先在快捷指令页新建）", KeyMapCodec.KeyEntry.Mouse(0x01)),
                }));
            else cats.Add(c);
        }
        _cats = cats;
        foreach (var c in _cats) Categories.Add(c.Name);
        Categories.Add(KeyFunctionCatalog.KeyboardCategoryName);

        // 用初始条目回选分类/选项（匹配不到则落到键盘捕获，若其为键盘类型则还原文本）。
        if (!SelectByEntry(initial))
        {
            if (initial.Type == 0x70)
            {
                CapturedMask = initial.V1;
                CapturedUsage = initial.V2;
                CapturedText = ComboText(initial.V1, initial.V2);
                CategoryIndex = Categories.Count - 1; // 键盘快捷键
            }
            else
            {
                CategoryIndex = 0;
                RebuildOptions();
            }
        }
    }

    public bool IsKeyboardMode => CategoryIndex == Categories.Count - 1;

    public Microsoft.UI.Xaml.Visibility OptionsVisibility =>
        IsKeyboardMode ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public Microsoft.UI.Xaml.Visibility CaptureVisibility =>
        IsKeyboardMode ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    partial void OnCategoryIndexChanged(int value)
    {
        RebuildOptions();
        OnPropertyChanged(nameof(IsKeyboardMode));
        OnPropertyChanged(nameof(OptionsVisibility));
        OnPropertyChanged(nameof(CaptureVisibility));
    }

    private void RebuildOptions()
    {
        Options.Clear();
        if (IsKeyboardMode) { OptionIndex = -1; return; }
        if (CategoryIndex >= 0 && CategoryIndex < _cats.Count)
            foreach (var o in _cats[CategoryIndex].Options) Options.Add(o);
        if (Options.Count > 0 && (OptionIndex < 0 || OptionIndex >= Options.Count)) OptionIndex = 0;
    }

    /// <summary>当前编辑结果 → 下发条目。</summary>
    public KeyMapCodec.KeyEntry Entry
    {
        get
        {
            if (IsKeyboardMode)
                return KeyMapCodec.KeyEntry.KeyboardCombo(CapturedMask, CapturedUsage);
            if (CategoryIndex >= 0 && CategoryIndex < _cats.Count
                && OptionIndex >= 0 && OptionIndex < Options.Count)
                return Options[OptionIndex].Entry;
            return KeyMapCodec.KeyEntry.Mouse(0x01); // 兜底左键
        }
    }

    /// <summary>设置捕获的键盘组合（由页面码隐藏事件调用）。</summary>
    public void SetCapture(byte mask, byte usage)
    {
        CapturedMask = mask;
        CapturedUsage = usage;
        CapturedText = ComboText(mask, usage);
    }

    private bool SelectByEntry(KeyMapCodec.KeyEntry e)
    {
        // 1) 精确匹配（正常情况下宏条目低半字节 = 本地宏槽+1，能直接对上）
        for (int ci = 0; ci < _cats.Count; ci++)
            for (int oi = 0; oi < _cats[ci].Options.Count; oi++)
                if (_cats[ci].Options[oi].Entry == e)
                    return Select(ci, oi);

        // 2) 宏条目兜底：低半字节保存的可能是"键下标+1"（下发时按目标键重定位过的旧数据），
        //    此时按"类型 + 循环标志 + 次数"匹配对应宏，避免回落显示成"鼠标功能/左键"。
        if (e.Type == 0x90)
            for (int ci = 0; ci < _cats.Count; ci++)
                for (int oi = 0; oi < _cats[ci].Options.Count; oi++)
                {
                    if (KeyMapCodec.MacroEntryMatches(e, _cats[ci].Options[oi].Entry))
                        return Select(ci, oi);
                }
        return false;
    }

    private bool Select(int ci, int oi)
    {
        CategoryIndex = ci;
        RebuildOptions();
        OptionIndex = oi;
        return true;
    }

    /// <summary>把修饰掩码 + 主键 usage 还原为可读文本（如 "Ctrl + C"）。</summary>
    public static string ComboText(byte mask, byte usage)
    {
        if (mask == 0 && usage == 0) return "（点击此处后按下组合键）";
        var parts = new List<string>();
        if ((mask & 0x01) != 0) parts.Add("LCtrl");
        if ((mask & 0x10) != 0) parts.Add("RCtrl");
        if ((mask & 0x02) != 0) parts.Add("LShift");
        if ((mask & 0x20) != 0) parts.Add("RShift");
        if ((mask & 0x04) != 0) parts.Add("LAlt");
        if ((mask & 0x40) != 0) parts.Add("RAlt");
        if ((mask & 0x08) != 0) parts.Add("LWin");
        if ((mask & 0x80) != 0) parts.Add("RWin");
        string main = HidUsageName(usage);
        if (main.Length > 0) parts.Add(main);
        return parts.Count > 0 ? string.Join(" + ", parts) : "（无）";
    }

    private static string HidUsageName(byte usage)
    {
        if (usage == 0) return "";
        if (usage is >= 0x04 and <= 0x1D) return ((char)('A' + (usage - 0x04))).ToString();
        if (usage is >= 0x1E and <= 0x26) return ((char)('1' + (usage - 0x1E))).ToString();
        if (usage == 0x27) return "0";
        if (usage is >= 0x3A and <= 0x45) return "F" + (usage - 0x3A + 1);
        return usage switch
        {
            0x28 => "Enter", 0x29 => "Esc", 0x2A => "Backspace", 0x2B => "Tab", 0x2C => "Space",
            0x2D => "-", 0x2E => "=", 0x2F => "[", 0x30 => "]", 0x31 => "\\",
            0x33 => ";", 0x34 => "'", 0x35 => "`", 0x36 => ",", 0x37 => ".", 0x38 => "/",
            0x4F => "→", 0x50 => "←", 0x51 => "↓", 0x52 => "↑",
            0x49 => "Insert", 0x4A => "Home", 0x4B => "PageUp", 0x4C => "Delete", 0x4D => "End", 0x4E => "PageDown",
            _ => $"0x{usage:X2}",
        };
    }
}

public sealed partial class KeyMapViewModel : ObservableObject
{
    private readonly IMouseService _svc;
    private readonly MacroStore _macros;

    public ObservableCollection<KeyEditItem> Keys { get; } = new();
    [ObservableProperty] private string _status = "";

    public KeyMapViewModel(IMouseService svc, MacroStore macros)
    {
        _svc = svc;
        _macros = macros;
        var caps = svc.Capabilities;
        int count = caps is { Keys.Count: > 0 } ? caps.Keys.Count : 5;
        var saved = svc.Settings.Keys;

        var macroOpts = BuildMacroOptions();
        for (int i = 0; i < count; i++)
        {
            KeyMapCodec.KeyEntry init = i < saved.Count ? saved[i] : DefaultFor(caps, i);
            Keys.Add(new KeyEditItem($"按键{i + 1}", init, macroOpts));
        }
    }

    /// <summary>把本地已保存的宏做成"宏"分类的选项（显示名=宏名，Entry=该宏的 0x90 绑定）。</summary>
    private List<KeyFunctionCatalog.KeyOption> BuildMacroOptions() =>
        _macros.Macros.Select(m =>
            new KeyFunctionCatalog.KeyOption(m.Name, m.BindEntry())).ToList();

    private static KeyMapCodec.KeyEntry DefaultFor(InphicMouse.Core.Models.DeviceCapabilities? caps, int i)
    {
        // func_type==5 为官方 DPI 按钮默认
        if (caps is not null && i < caps.Keys.Count && caps.Keys[i].FuncType == 5)
            return KeyMapCodec.KeyEntry.Dpi(0x01);
        byte[] bits = { 0x01, 0x02, 0x04, 0x08, 0x10 };
        return KeyMapCodec.KeyEntry.Mouse(i < bits.Length ? bits[i] : (byte)0x01);
    }

    [RelayCommand]
    private async Task Apply()
    {
        // 保存"逻辑条目"（宏条目低半字节 = 本地宏槽+1），供 UI 回选；另建"线字节"用于下发。
        // 官方约定（DB t_key_macro_data + 官方 USB 抓包验证）：宏槽 = 被绑定物理键的下标 key_code，
        // 0x08 的 buf[5] 与 0x09 里 0x90 条目的低半字节都 = key_code+1。
        // 所以先把引用到的宏按"目标键下标"作为槽上传(0x08)，再把线字节条目改成官方格式，最后下发按键映射(0x09)。
        var logical = Keys.Select(k => k.Entry).ToList();
        var wire = logical.ToList();
        int uploaded = 0;
        for (int i = 0; i < logical.Count; i++)
        {
            var e = logical[i];
            if (e.Type != 0x90) continue;                 // 只处理"宏"分类的条目
            int refSlot = (e.V1 & 0x0F) - 1;              // 低半字节 = 本地宏槽+1
            var m = _macros.Macros.FirstOrDefault(x => x.Slot == refSlot);
            if (m is null) continue;
            var evs = m.BuildEvents();
            if (evs.Count == 0) continue;

            if (await _svc.ApplyMacroAsync(i, evs)) uploaded++;      // 宏槽 = 键下标
            wire[i] = KeyMapCodec.RebindMacroToKey(e, i);            // 仅改下发的线字节
        }

        // 快照保存"逻辑条目"（不要存重定位后的线字节，否则回到本页无法回选宏 → 显示成左键）
        _svc.Settings.Keys.Clear();
        _svc.Settings.Keys.AddRange(logical);

        bool ok = await _svc.ApplyKeyMapAsync(wire);
        Status = ok
            ? $"已下发按键映射（{logical.Count} 键{(uploaded > 0 ? $"，并上传 {uploaded} 个宏" : "")}）"
            : "下发失败";
    }

    /// <summary>当前键位里是否至少有一个键是"鼠标左键"(type=0x10, v1=0x01)。
    /// 没有左键会导致无法正常点击，应用前需强制提醒。</summary>
    public bool HasLeftClick =>
        Keys.Any(k => k.Entry is { Type: 0x10, V1: 0x01 });

    [RelayCommand]
    private void ResetDefault()
    {
        var caps = _svc.Capabilities;
        var macroOpts = BuildMacroOptions();
        for (int i = 0; i < Keys.Count; i++)
        {
            var def = DefaultFor(caps, i);
            // 用默认条目重建选择
            Keys[i] = new KeyEditItem(Keys[i].KeyName, def, macroOpts);
        }
        Status = "已恢复默认键位（未下发，点「应用」生效）";
    }
}
