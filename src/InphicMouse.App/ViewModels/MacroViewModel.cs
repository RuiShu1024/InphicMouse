using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InphicMouse.Protocol.Codecs;

namespace InphicMouse.App.ViewModels;

/// <summary>
/// 快捷指令（宏）编辑器，对照官方"快捷指令管理"：左=宏列表，中=事件列表+录制/插入，右=延时/循环设置。
/// 宏仅保存在**本地**(MacroStore→macros.json)；真正下发到鼠标在「鼠标按键」页选择该宏并「应用」时发生。
/// </summary>
public sealed partial class MacroViewModel : ObservableObject
{
    private readonly MacroStore _store;

    public ObservableCollection<MacroDef> Macros => _store.Macros;

    [ObservableProperty] private MacroDef? _current;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _status = "在左侧「新建」一个宏，选中后录制或手动插入事件；编辑完点「保存」。";
    [ObservableProperty] private int _selectedEventIndex = -1;

    private long _lastTicks;

    public MacroViewModel(MacroStore store)
    {
        _store = store;
        // 经属性赋值以触发 OnCurrentChanged（订阅事件集合变更 → 拖动/移动后重编号）。
        if (_store.Macros.Count > 0) Current = _store.Macros[0];
    }

    public ObservableCollection<MacroEventRow>? Events => Current?.Events;

    private ObservableCollection<MacroEventRow>? _subscribedEvents;

    partial void OnCurrentChanged(MacroDef? value)
    {
        IsRecording = false;
        // 事件列表拖动/增删后自动重编号并保存
        if (_subscribedEvents is not null) _subscribedEvents.CollectionChanged -= Events_Changed;
        _subscribedEvents = value?.Events;
        if (_subscribedEvents is not null) _subscribedEvents.CollectionChanged += Events_Changed;
        OnPropertyChanged(nameof(Events));
        OnPropertyChanged(nameof(UseRecordedDelay));
        OnPropertyChanged(nameof(UseUniformDelay));
        OnPropertyChanged(nameof(UniformDelayMs));
        OnPropertyChanged(nameof(IsUntilRelease));
        OnPropertyChanged(nameof(IsUntilAnyKey));
        OnPropertyChanged(nameof(IsCount));
        OnPropertyChanged(nameof(LoopCount));
        OnPropertyChanged(nameof(HasCurrent));
    }

    public bool HasCurrent => Current is not null;

    // ---- 宏列表操作 ----
    [RelayCommand]
    private void NewMacro()
    {
        Current = _store.Create();
        _store.Save();
        Status = "已新建宏，可开始录制或插入事件。";
    }

    [RelayCommand]
    private void DeleteMacro()
    {
        if (Current is null) return;
        _store.Remove(Current);
        Current = _store.Macros.Count > 0 ? _store.Macros[0] : null;
        Status = "已删除宏。";
    }

    [RelayCommand]
    private void Save()
    {
        _store.Save();
        Status = Current is null ? "无宏可保存。" : $"已保存「{Current.Name}」（{Current.Events.Count} 事件）到本地。到「鼠标按键」选此宏并应用才会写入鼠标。";
    }

    /// <summary>双击宏名重命名。</summary>
    public void RenameCurrent(string? name)
    {
        if (Current is null) return;
        name = (name ?? "").Trim();
        if (name.Length == 0) return;
        Current.Name = name;
        _store.Save();
        Status = $"已重命名为「{name}」。";
    }

    /// <summary>导出**当前选中的单个宏**为 JSON 文本（用户可自行编辑后再导入）。</summary>
    public string ExportMacro() => Current is null ? "" : _store.ExportText(Current);

    /// <summary>导入宏 JSON 文本（追加为新的宏，并选中导入的最后一个）。</summary>
    public void ImportMacros(string json)
    {
        _store.ImportText(json, replace: false);
        if (_store.Macros.Count > 0) Current = _store.Macros[^1];
        Status = $"已导入为新的宏，当前共 {_store.Macros.Count} 个宏。";
    }

    // ---- 录制 ----
    [RelayCommand]
    private void ToggleRecord()
    {
        if (Current is null) { Status = "请先新建或选中一个宏。"; return; }
        IsRecording = !IsRecording;
        if (IsRecording) { _lastTicks = System.DateTime.UtcNow.Ticks; Status = "录制中…在捕获区操作键盘/鼠标，再次点击停止。"; }
        else { _store.Save(); Status = $"已录制 {Current.Events.Count} 个事件（已保存）。"; }
    }

    /// <summary>录制时由页面调用：按键=按下/松开，鼠标同理；两事件间按延时设置插一条延时事件。</summary>
    public void Record(MacroCodec.EvType type, int value)
    {
        if (!IsRecording || Current is null) return;
        long now = System.DateTime.UtcNow.Ticks;
        if (Current.Events.Count > 0)
        {
            int delay = Current.UseRecordedDelay
                ? (int)System.Math.Clamp((now - _lastTicks) / System.TimeSpan.TicksPerMillisecond, 0L, 65535L)
                : System.Math.Clamp(Current.UniformDelayMs, 0, 65535);
            AddEvent(MacroCodec.MacroEvent.Delay(delay));
        }
        _lastTicks = now;
        AddEvent(new MacroCodec.MacroEvent(type, value));
        Status = $"录制中… {Current.Events.Count} 个事件";
    }

    private void AddEvent(MacroCodec.MacroEvent ev)
    {
        Current!.Events.Add(new MacroEventRow(Current.Events.Count, ev));
        Reindex();
    }

    // ---- 手动插入 ----
    [RelayCommand] private void InsertLeft() => InsertMouse(0x01);
    [RelayCommand] private void InsertRight() => InsertMouse(0x02);
    [RelayCommand] private void InsertMiddle() => InsertMouse(0x04);

    /// <summary>插入键盘事件：由页面捕获一个按键(VK)后调用（按下+松开一对）。</summary>
    public void InsertKeyboard(int vk)
    {
        if (Current is null || vk == 0) return;
        int at = InsertPos();
        Current.Events.Insert(at, new MacroEventRow(at, MacroCodec.MacroEvent.KeyDown(vk)));
        Current.Events.Insert(at + 1, new MacroEventRow(at + 1, MacroCodec.MacroEvent.KeyUp(vk)));
        Reindex(); _store.Save();
    }

    [RelayCommand]
    private void InsertDelay()
    {
        if (Current is null) return;
        int at = InsertPos();
        Current.Events.Insert(at, new MacroEventRow(at,
            MacroCodec.MacroEvent.Delay(System.Math.Clamp(Current.UniformDelayMs, 0, 65535))));
        Reindex(); _store.Save();
    }

    private void InsertMouse(int bit)
    {
        if (Current is null) return;
        int at = InsertPos();
        Current.Events.Insert(at, new MacroEventRow(at, MacroCodec.MacroEvent.MouseDown(bit)));
        Current.Events.Insert(at + 1, new MacroEventRow(at + 1, MacroCodec.MacroEvent.MouseUp(bit)));
        Reindex(); _store.Save();
    }

    private int InsertPos() =>
        Current is not null && SelectedEventIndex >= 0 && SelectedEventIndex < Current.Events.Count
            ? SelectedEventIndex + 1 : (Current?.Events.Count ?? 0);

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Current is null) return;
        if (SelectedEventIndex >= 0 && SelectedEventIndex < Current.Events.Count)
        {
            Current.Events.RemoveAt(SelectedEventIndex);
            Reindex(); _store.Save();
        }
    }

    [RelayCommand]
    private void Clear()
    {
        if (Current is null) return;
        Current.Events.Clear();
        _store.Save();
        Status = "已清空当前宏事件。";
    }

    private void Reindex()
    {
        if (Current is null) return;
        for (int i = 0; i < Current.Events.Count; i++) Current.Events[i].Index = i;
    }

    private void Events_Changed(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        Reindex();
        _store.Save();
    }

    /// <summary>移动快捷指令在列表中的顺序（不影响宏槽与按键绑定）。</summary>
    public void MoveMacro(int from, int to)
    {
        int n = _store.Macros.Count;
        if (n == 0 || from < 0 || from >= n) return;
        to = System.Math.Clamp(to, 0, n - 1);
        if (to == from) return;
        _store.Macros.Move(from, to);
        Current = _store.Macros[to];
        _store.Save();
    }

    // ---- 事件排序（▲▼按钮；列表本身也支持拖动重排）----
    [RelayCommand] private void MoveEventUp() => MoveSelected(-1);
    [RelayCommand] private void MoveEventDown() => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        if (Current is null) return;
        int i = SelectedEventIndex;
        if (i < 0 || i >= Current.Events.Count) return;
        MoveEvent(i, i + delta);
    }

    /// <summary>把第 <paramref name="from"/> 条事件移动到 <paramref name="to"/> 位置（0-based）。
    /// 事件集合变更后由 <see cref="Events_Changed"/> 自动重编号并保存。</summary>
    public void MoveEvent(int from, int to)
    {
        if (Current is null) return;
        int n = Current.Events.Count;
        if (n == 0 || from < 0 || from >= n) return;
        to = System.Math.Clamp(to, 0, n - 1);
        if (to == from) return;
        Current.Events.Move(from, to);
        SelectedEventIndex = to;
    }

    /// <summary>双击编辑：替换第 <paramref name="index"/> 条事件的值（延时毫秒 / 键值 / 鼠标按钮）。</summary>
    public void ReplaceEvent(int index, MacroCodec.MacroEvent ev)
    {
        if (Current is null || index < 0 || index >= Current.Events.Count) return;
        Current.Events[index].Event = ev;
        _store.Save();
        Status = $"已修改事件 {index}：{Current.Events[index].Kind} {Current.Events[index].Value}";
    }

    // ---- 延时 / 循环设置（代理到 Current，并即时保存）----
    public bool UseRecordedDelay
    {
        get => Current?.UseRecordedDelay ?? true;
        set { if (Current is null) return; Current.UseRecordedDelay = value; _store.Save(); OnPropertyChanged(); OnPropertyChanged(nameof(UseUniformDelay)); }
    }
    public bool UseUniformDelay
    {
        get => !(Current?.UseRecordedDelay ?? true);
        set { if (Current is null) return; Current.UseRecordedDelay = !value; _store.Save(); OnPropertyChanged(); OnPropertyChanged(nameof(UseRecordedDelay)); }
    }
    public int UniformDelayMs
    {
        get => Current?.UniformDelayMs ?? 50;
        set { if (Current is null) return; Current.UniformDelayMs = value; _store.Save(); OnPropertyChanged(); }
    }
    public int LoopCount
    {
        get => Current?.LoopCount ?? 1;
        set { if (Current is null) return; Current.LoopCount = value; _store.Save(); OnPropertyChanged(); }
    }
    public bool IsUntilRelease
    {
        get => Current?.LoopMode == MacroLoopMode.UntilRelease;
        set { if (Current is not null && value) { Current.LoopMode = MacroLoopMode.UntilRelease; _store.Save(); RaiseLoop(); } }
    }
    public bool IsUntilAnyKey
    {
        get => Current?.LoopMode == MacroLoopMode.UntilAnyKey;
        set { if (Current is not null && value) { Current.LoopMode = MacroLoopMode.UntilAnyKey; _store.Save(); RaiseLoop(); } }
    }
    public bool IsCount
    {
        get => Current?.LoopMode == MacroLoopMode.Count;
        set { if (Current is not null && value) { Current.LoopMode = MacroLoopMode.Count; _store.Save(); RaiseLoop(); } }
    }
    private void RaiseLoop()
    {
        OnPropertyChanged(nameof(IsUntilRelease));
        OnPropertyChanged(nameof(IsUntilAnyKey));
        OnPropertyChanged(nameof(IsCount));
    }
}
