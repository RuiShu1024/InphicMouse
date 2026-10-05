namespace InphicMouse.Protocol.Codecs;

/// <summary>
/// 按键映射下发（0x09），1:1 复刻 <c>SetMouseKeyMacroData</c>(@0x438090)。
/// 载荷为 5 个物理键，每键 3 字节 <c>[功能类型, v1, v2]</c>（header len = 0x0F = 15）。
/// 功能类型（高半字节）：0x10 鼠标键 / 0x20 滚轮·特殊 / 0x30 连发 / 0x40 DPI /
/// 0x60 系统·配置 / 0x70 键盘键 / 0x80 多媒体 / 0x90 宏。
/// </summary>
public static class KeyMapCodec
{
    public const byte Cmd = 0x09;
    public const int KeyCount = 5;
    public const int EntrySize = 3;

    /// <summary>出厂默认键位（逆向自 @0x438090 的初始模板）：5 键均为鼠标键，值为各自按钮位。</summary>
    public static readonly byte[] DefaultPayload =
    {
        0x10, 0x01, 0x00, // 左键
        0x10, 0x02, 0x00, // 右键
        0x10, 0x04, 0x00, // 中键
        0x10, 0x08, 0x00, // 侧键1（后退）
        0x10, 0x10, 0x00, // 侧键2（前进）
    };

    /// <summary>单个物理键的功能条目（3 字节）。</summary>
    public readonly record struct KeyEntry(byte Type, byte V1, byte V2)
    {
        public void CopyTo(Span<byte> dst)
        {
            dst[0] = Type; dst[1] = V1; dst[2] = V2;
        }

        /// <summary>鼠标键：类型 0x10，v1 = 按钮位（左0x01/右0x02/中0x04/侧1 0x08/侧2 0x10）。</summary>
        public static KeyEntry Mouse(byte buttonBit) => new(0x10, buttonBit, 0x00);

        /// <summary>滚轮/特殊鼠标功能：类型 0x20（复刻 macro_type==1 的 value7/8：0x20,v1）。</summary>
        public static KeyEntry Wheel(byte v1) => new(0x20, v1, 0x00);

        /// <summary>连发(Fire)：类型 0x30，v1/v2 复刻 macro_type==7(0x30,macro_value,macro_value2)。</summary>
        public static KeyEntry Fire(byte v1, byte v2) => new(0x30, v1, v2);

        /// <summary>DPI 功能：类型 0x40（复刻 macro_type==5）。v1：2=DPI+ / 3=DPI- / 1=DPI循环。</summary>
        public static KeyEntry Dpi(byte v1) => new(0x40, v1, 0x00);

        /// <summary>系统/配置功能：类型 0x60（复刻 macro_type==6/9）。v1=0 视为关闭/无功能。</summary>
        public static KeyEntry SystemFunc(byte v1 = 0x00) => new(0x60, v1, 0x00);

        /// <summary>
        /// 多媒体/消费者控制：类型 0x80（复刻 macro_type==3/8）。<paramref name="usage"/> 为 HID
        /// Consumer Usage：单字节(如 0xCD 播放/暂停) v2=0；双字节(如 0x183/0x192) 小端拆入 v1/v2。
        /// </summary>
        public static KeyEntry Media(int usage) =>
            new(0x80, (byte)(usage & 0xFF), (byte)((usage >> 8) & 0xFF));

        /// <summary>系统/配置键：类型 0x60（对应 macro_type==9）。</summary>
        public static KeyEntry System() => new(0x60, 0x00, 0x00);

        /// <summary>原始 3 字节条目（用于目录中直接给定确切字节）。</summary>
        public static KeyEntry Raw(byte type, byte v1, byte v2) => new(type, v1, v2);

        /// <summary>
        /// 宏键：类型 0x90（复刻 @0x438090 case4 + 官方 USB 抓包）。v1 低半字节 = 宏槽(键下标)+1，
        /// 高半字节为循环标志（来自官方 MacroInfo.macro_type / play_times）：
        /// 按住循环 → |0x10；直到任意键按下 → |0x40；指定次数 → |0x20 且 v2=次数；
        /// 单次 → 原值(v1 不带标志)。
        /// 官方抓包示例：单次/指定 1 次为 <c>90 26 01</c>（槽 6 = key_code 5）。
        /// </summary>
        public static KeyEntry Macro(int macroSlot, bool repeatUntilRelease = false,
                                     byte repeatCount = 0, bool untilAnyKey = false)
        {
            byte v1 = (byte)(macroSlot + 1);
            if (repeatCount > 0) return new KeyEntry(0x90, (byte)(v1 | 0x20), repeatCount);
            if (repeatUntilRelease) return new KeyEntry(0x90, (byte)(v1 | 0x10), 0x00);
            if (untilAnyKey) return new KeyEntry(0x90, (byte)(v1 | 0x40), 0x00);
            return new KeyEntry(0x90, v1, 0x00);
        }

        /// <summary>
        /// 键盘键：类型 0x70（对应 macro_type==2）。<paramref name="usage"/> 为 VK/HID usage，
        /// <paramref name="modifierMask"/> 为修饰键掩码（macro_value2）。
        /// 复刻分支：一般 v1=掩码、v2=GetSysKeyCode(usage)；当掩码为 0 且 usage 本身是
        /// 独立修饰键(0xE0..0xE7) 时 v1=该修饰位、v2=0。
        /// </summary>
        public static KeyEntry Keyboard(int usage, byte modifierMask)
        {
            byte sys = KeyCodes.GetSysKeyCode(usage);
            if (modifierMask == 0 && KeyCodes.IsModifierUsage(usage))
                return new KeyEntry(0x70, sys, 0x00);
            return new KeyEntry(0x70, modifierMask, sys);
        }

        /// <summary>
        /// 由"捕获的键盘组合"生成条目：<paramref name="modifierMask"/> 为硬件修饰位掩码
        /// (LCtrl0x01/LShift0x02/LAlt0x04/LGui0x08/RCtrl0x10/RShift0x20/RAlt0x40/RGui0x80 的按位或)，
        /// <paramref name="mainHidUsage"/> 为主键 HID usage(由 <see cref="KeyCodes.VkToHidUsage"/> 得)。
        /// 复刻 @0x438090 case2：type=0x70, v1=修饰掩码, v2=主键 usage。
        /// </summary>
        public static KeyEntry KeyboardCombo(byte modifierMask, byte mainHidUsage) =>
            new(0x70, modifierMask, mainHidUsage);
    }

    /// <summary>
    /// 把一条宏条目重新定位到指定物理键。官方约定：**宏槽 = 键下标 key_code**，
    /// 故 v1 低半字节改写为 keyIndex+1（0-based 键下标），高半字节的循环标志与 v2 保留。
    /// 例：KeyEntry.Macro(0, repeatCount:1) 的 0x21 绑到 6 号键(index 5) → 0x26。
    /// </summary>
    public static KeyEntry RebindMacroToKey(KeyEntry entry, int keyIndex)
    {
        if (entry.Type != 0x90) return entry;
        return new KeyEntry(0x90, (byte)((keyIndex + 1) | (entry.V1 & 0xF0)), entry.V2);
    }

    /// <summary>
    /// 判断本地保存的宏条目与目录里的宏条目是否指向同一个宏。
    /// 精确相等即同一宏；否则退化为比较"类型 + 循环标志 + 次数"——
    /// 因为低半字节可能是"键下标+1"（下发时按目标键重定位过的旧数据），不一定等于宏槽+1。
    /// </summary>
    public static bool MacroEntryMatches(KeyEntry bound, KeyEntry reference)
    {
        if (bound.Type != 0x90 || reference.Type != 0x90) return false;
        if (bound == reference) return true;
        return (bound.V1 & 0xF0) == (reference.V1 & 0xF0) && bound.V2 == reference.V2;
    }

    /// <summary>用功能条目组装 0x09 报文。键数随型号而定（3311 为 6 键，含 DPI 键）；
    /// header 载荷长度 = 键数×3。</summary>
    public static byte[] Encode(IReadOnlyList<KeyEntry> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count is < 1 or > 9)
            throw new ArgumentException("键数须为 1..9", nameof(keys));

        int len = keys.Count * EntrySize;
        Span<byte> payload = stackalloc byte[len];
        for (int i = 0; i < keys.Count; i++)
            keys[i].CopyTo(payload.Slice(i * EntrySize, EntrySize));

        return MouseReport.Build(Cmd, stackalloc byte[] { 0x00, 0x01, (byte)len }, payload);
    }

    /// <summary>下发出厂默认键位。</summary>
    public static byte[] EncodeDefault() =>
        MouseReport.Build(Cmd, stackalloc byte[] { 0x00, 0x01, 0x0F }, DefaultPayload);
}
