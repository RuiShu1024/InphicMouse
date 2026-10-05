using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using InphicMouse.Protocol.Codecs;

namespace InphicMouse.App.ViewModels;

/// <summary>循环模式（对应官方"循环设置" play_type）。</summary>
public enum MacroLoopMode { UntilRelease, UntilAnyKey, Count }

/// <summary>一条宏事件行：1:1 对应官方 DB t_macrorecord（type + value）。</summary>
public sealed partial class MacroEventRow : ObservableObject
{
    [ObservableProperty] private int _index;

    private MacroCodec.MacroEvent _event;
    /// <summary>事件（可双击编辑后替换）。</summary>
    public MacroCodec.MacroEvent Event
    {
        get => _event;
        set
        {
            if (_event == value) return;
            _event = value;
            OnPropertyChanged(nameof(Event));
            OnPropertyChanged(nameof(IsDelay));
            OnPropertyChanged(nameof(Kind));
            OnPropertyChanged(nameof(Value));
        }
    }

    public MacroEventRow(int index, MacroCodec.MacroEvent ev)
    {
        _index = index; _event = ev;
    }

    public bool IsDelay => Event.Type == MacroCodec.EvType.Delay;

    /// <summary>属性列：键盘/鼠标/时间。</summary>
    public string Kind => Event.Type switch
    {
        MacroCodec.EvType.Delay => "时间",
        MacroCodec.EvType.KeyDown or MacroCodec.EvType.KeyUp => "键盘",
        _ => "鼠标",
    };

    /// <summary>值列：延时 "N ms"；按键 "↓/↑ 名称"（对照官方 desc）。</summary>
    public string Value => Event.Type switch
    {
        MacroCodec.EvType.Delay => $"{Event.Value} ms",
        MacroCodec.EvType.KeyDown => $"↓ {VkName(Event.Value)}",
        MacroCodec.EvType.KeyUp => $"↑ {VkName(Event.Value)}",
        MacroCodec.EvType.MouseDown => $"↓ {MouseName(Event.Value)}",
        MacroCodec.EvType.MouseUp => $"↑ {MouseName(Event.Value)}",
        _ => "",
    };

    private static string MouseName(int bit) => bit switch { 1 => "左键", 2 => "右键", 4 => "中键", _ => $"0x{bit:X2}" };

    /// <summary>列表行的可读文本（同时供 UI 自动化/无障碍读取）。</summary>
    public override string ToString() => $"{Index} {Kind} {Value}";

    /// <summary>Windows VK → 显示名（官方 value 存的是 VK，如 A=65）。</summary>
    public static string VkName(int vk)
    {
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();          // A-Z
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();          // 0-9
        if (vk is >= 0x70 and <= 0x7B) return "F" + (vk - 0x70 + 1);          // F1-F12
        return vk switch
        {
            0x0D => "Enter", 0x1B => "Esc", 0x08 => "Backspace", 0x09 => "Tab", 0x20 => "Space",
            0x25 => "←", 0x26 => "↑", 0x27 => "→", 0x28 => "↓",
            0x11 => "Ctrl", 0x10 => "Shift", 0x12 => "Alt",
            _ => $"VK{vk}",
        };
    }
}

/// <summary>一个本地保存的宏定义。</summary>
public sealed partial class MacroDef : ObservableObject
{
    [ObservableProperty] private string _name = "自定义1";
    public int Slot { get; set; }
    public ObservableCollection<MacroEventRow> Events { get; } = new();

    // 默认"指定次数=1"（单次播放）：对应官方 MacroInfo.play_type=0/play_times=1，
    // 抓包验证的键位条目为 [0x90, (键下标+1)|0x20, 1]（如 90 26 01）。
    public MacroLoopMode LoopMode { get; set; } = MacroLoopMode.Count;
    public int LoopCount { get; set; } = 1;
    public bool UseRecordedDelay { get; set; } = true;
    public int UniformDelayMs { get; set; } = 50;

    /// <summary>事件直接 1:1 下发（延时也是独立事件，与官方 DB t_macrorecord 一致）。</summary>
    public List<MacroCodec.MacroEvent> BuildEvents() =>
        Events.Select(r => r.Event).ToList();

    /// <summary>绑定到按键时的 KeyEntry（按循环设置选 0x90 的位）。</summary>
    public KeyMapCodec.KeyEntry BindEntry() => LoopMode switch
    {
        MacroLoopMode.Count => KeyMapCodec.KeyEntry.Macro(Slot, repeatCount: (byte)System.Math.Clamp(LoopCount, 1, 255)),
        MacroLoopMode.UntilRelease => KeyMapCodec.KeyEntry.Macro(Slot, repeatUntilRelease: true),
        MacroLoopMode.UntilAnyKey => KeyMapCodec.KeyEntry.Macro(Slot, untilAnyKey: true),
        _ => KeyMapCodec.KeyEntry.Macro(Slot),
    };
}

// ---- 本地持久化 DTO ----
internal sealed class MacroEventDto { public int Type { get; set; } public int Value { get; set; } }
internal sealed class MacroDto
{
    public string Name { get; set; } = "";
    public int Slot { get; set; }
    public int LoopMode { get; set; }
    public int LoopCount { get; set; }
    public bool UseRecordedDelay { get; set; }
    public int UniformDelayMs { get; set; }
    public List<MacroEventDto> Events { get; set; } = new();
}

/// <summary>宏的本地存储（JSON 文件，程序目录），全局单例。多宏，可被按键页引用。也支持导出/导入。</summary>
public sealed class MacroStore
{
    private static readonly string FilePath = AppPaths.DataFile("macros.json");

    // UnsafeRelaxedJsonEscaping：导出/保存的中文宏名与人可读（不写成 \uXXXX），便于用户手工编辑。
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public ObservableCollection<MacroDef> Macros { get; } = new();

    public MacroStore() => Load();

    /// <summary>新建一个宏，自动命名与分配槽位。</summary>
    public MacroDef Create()
    {
        int slot = Macros.Count == 0 ? 0 : Macros.Max(m => m.Slot) + 1;
        var m = new MacroDef { Name = $"自定义{Macros.Count + 1}", Slot = slot };
        Macros.Add(m);
        return m;
    }

    public void Remove(MacroDef m) { Macros.Remove(m); Save(); }

    public void Load()
    {
        Macros.Clear();
        try
        {
            if (!File.Exists(FilePath)) return;
            ImportText(File.ReadAllText(FilePath), replace: true, persist: false);
        }
        catch { /* 忽略损坏文件 */ }
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, ExportText(), new UTF8Encoding(false)); }
        catch { /* 忽略写入失败 */ }
    }

    /// <summary>导出全部宏为 JSON 文本（与 macros.json 同格式，便于用户自行编辑后再导入）。</summary>
    public string ExportText() => JsonSerializer.Serialize(BuildDtos(), JsonOptions);

    /// <summary>只导出单个宏（仍为数组格式，便于直接导入）。</summary>
    public string ExportText(MacroDef m) => JsonSerializer.Serialize(new List<MacroDto> { ToDto(m) }, JsonOptions);

    /// <summary>从 JSON 文本导入宏。<paramref name="replace"/> 为 true 时先清空现有宏；
    /// 导入的宏会重新分配槽位以避免冲突。</summary>
    public void ImportText(string json, bool replace, bool persist = true)
    {
        var dtos = JsonSerializer.Deserialize<List<MacroDto>>(json)
                   ?? throw new InvalidDataException("宏文件格式不正确。");
        if (replace) Macros.Clear();

        int next = Macros.Count == 0 ? 0 : Macros.Max(m => m.Slot) + 1;
        foreach (var d in dtos)
        {
            var m = FromDto(d);
            m.Slot = next++;
            Macros.Add(m);
        }
        if (persist) Save();
    }

    private List<MacroDto> BuildDtos() => Macros.Select(ToDto).ToList();

    private static MacroDto ToDto(MacroDef m) => new()
    {
        Name = m.Name, Slot = m.Slot, LoopMode = (int)m.LoopMode, LoopCount = m.LoopCount,
        UseRecordedDelay = m.UseRecordedDelay, UniformDelayMs = m.UniformDelayMs,
        Events = m.Events.Select(r => new MacroEventDto
        {
            Type = (int)r.Event.Type, Value = r.Event.Value,
        }).ToList(),
    };

    private static MacroDef FromDto(MacroDto d)
    {
        var m = new MacroDef
        {
            Name = d.Name, Slot = d.Slot, LoopMode = (MacroLoopMode)d.LoopMode,
            LoopCount = d.LoopCount, UseRecordedDelay = d.UseRecordedDelay, UniformDelayMs = d.UniformDelayMs,
        };
        foreach (var e in d.Events)
            m.Events.Add(new MacroEventRow(m.Events.Count,
                new MacroCodec.MacroEvent((MacroCodec.EvType)e.Type, e.Value)));
        return m;
    }
}