namespace InphicMouse.Protocol.Codecs;

/// <summary>
/// 快捷指令目录：把官方软件"按键功能编辑器"的分类与选项，按逆向 <c>SetMouseKeyMacroData</c>(@0x438090)
/// 的确切字节 1:1 复刻为可枚举的目录。纯数据，无 UI 依赖。
/// 每个 <see cref="KeyOption"/> 直接携带下发用的 3 字节 <see cref="KeyMapCodec.KeyEntry"/>。
/// 键盘快捷键为动态捕获(见 App 侧)，不在此固定目录内。
/// </summary>
public static class KeyFunctionCatalog
{
    /// <summary>一个可选功能项（显示名 + 对应 3 字节条目）。</summary>
    public sealed record KeyOption(string Name, KeyMapCodec.KeyEntry Entry);

    /// <summary>一个功能分类（名称 + 其下选项）。</summary>
    public sealed record KeyCategory(string Name, IReadOnlyList<KeyOption> Options);

    // ---- 鼠标功能（type 0x10；连发/滚轮用 0x30/0x20）----
    public static readonly KeyCategory Mouse = new("鼠标功能", new[]
    {
        new KeyOption("左键",        KeyMapCodec.KeyEntry.Mouse(0x01)),
        new KeyOption("右键",        KeyMapCodec.KeyEntry.Mouse(0x02)),
        new KeyOption("中键",        KeyMapCodec.KeyEntry.Mouse(0x04)),
        new KeyOption("后退（侧键1）", KeyMapCodec.KeyEntry.Mouse(0x08)),
        new KeyOption("前进（侧键2）", KeyMapCodec.KeyEntry.Mouse(0x10)),
        new KeyOption("滚轮上",      KeyMapCodec.KeyEntry.Wheel(0x01)),   // 复刻 value7 → 0x20,0x01
        new KeyOption("滚轮下",      KeyMapCodec.KeyEntry.Wheel(0xFF)),   // 复刻 value8 → 0x20,0xFF
    });

    // ---- DPI 功能（type 0x40；v1：2=+ / 3=- / 1=循环）----
    public static readonly KeyCategory Dpi = new("DPI", new[]
    {
        new KeyOption("DPI 循环",  KeyMapCodec.KeyEntry.Dpi(0x01)),
        new KeyOption("DPI +",     KeyMapCodec.KeyEntry.Dpi(0x02)),
        new KeyOption("DPI -",     KeyMapCodec.KeyEntry.Dpi(0x03)),
    });

    // ---- 多媒体（type 0x80，HID Consumer Usage；复刻 @0x438090 case 3）----
    public static readonly KeyCategory Media = new("多媒体", new[]
    {
        new KeyOption("播放/暂停", KeyMapCodec.KeyEntry.Media(0xCD)),
        new KeyOption("停止",      KeyMapCodec.KeyEntry.Media(0xB7)),
        new KeyOption("下一曲",    KeyMapCodec.KeyEntry.Media(0xB5)),
        new KeyOption("上一曲",    KeyMapCodec.KeyEntry.Media(0xB6)),
        new KeyOption("音量 +",    KeyMapCodec.KeyEntry.Media(0xE9)),
        new KeyOption("音量 -",    KeyMapCodec.KeyEntry.Media(0xEA)),
        new KeyOption("静音",      KeyMapCodec.KeyEntry.Media(0xE2)),
        new KeyOption("媒体播放器", KeyMapCodec.KeyEntry.Media(0x183)),
    });

    // ---- 关闭/无功能（type 0x60,0,0；复刻 case 9 的系统/空）----
    public static readonly KeyCategory Disable = new("关闭按键", new[]
    {
        new KeyOption("关闭此键", KeyMapCodec.KeyEntry.SystemFunc(0x00)),
    });

    // ---- 宏（type 0x90；绑定到宏槽。此处提供"宏1"单次/按住循环两种常用绑定）----
    public static readonly KeyCategory Macro = new("宏", new[]
    {
        new KeyOption("宏1（单次）",     KeyMapCodec.KeyEntry.Macro(0)),
        new KeyOption("宏1（按住循环）", KeyMapCodec.KeyEntry.Macro(0, repeatUntilRelease: true)),
    });

    /// <summary>键盘快捷键分类名（选项由 App 侧动态捕获，不在此）。</summary>
    public const string KeyboardCategoryName = "键盘快捷键";

    /// <summary>固定目录分类（不含"键盘快捷键"动态分类）。</summary>
    public static readonly IReadOnlyList<KeyCategory> FixedCategories = new[]
    {
        Mouse, Dpi, Media, Macro, Disable,
    };
}
