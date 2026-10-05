namespace InphicMouse.Protocol.Codecs;

/// <summary>
/// 宏内容上传（0x08）。两个逆向函数：
/// <list type="bullet">
/// <item><c>SetMouseKeyMacroData</c>(@0x438b80) —— 分帧封装：
/// 头字 <c>0x01000800</c> → buf[1]=0x08,buf[2]=0x00,buf[3]=0x01；buf[5]=宏槽(键位下标)+1；
/// buf[6..] 为宏事件（每事件 4 字节）。≤6 事件单帧：buf[4]=事件数*4+1；
/// &gt;6 事件分块：每块 buf[3]=(块序&lt;&lt;4)|总块数，buf[4]=0x1B(整块)/余数(末块)。</item>
/// <item><c>GetMouseMacroData</c>(@0x41d410) —— 事件编码（本类核心，
/// 已用官方 USB 抓包 <c>2.pcapng</c> 逐字节校验）。</item>
/// </list>
///
/// <para><b>事件线格式 = [动作类型, 延时低, 延时高, 键值]（4 字节）</b>，延时为 16 位小端毫秒，
/// 且挂在"<b>前一个动作</b>"上（DB 里的独立延时记录在编码时并入前一动作）；动作之间最小 1ms。</para>
///
/// <para><b>动作类型字节</b>：键盘按下 0x30 / 键盘松开 0xB0（修饰键分别是 0x20 / 0xA0）；
/// 鼠标按下 0x10 / 鼠标松开 0x90。</para>
///
/// <para><b>键值</b>：键盘 = HID usage（<see cref="KeyCodes.VkToHidUsage"/>，A=65→0x04）；
/// 鼠标 = 按钮位（左1/右2/中4）；修饰键 = <see cref="KeyCodes.GetSysKeyCode"/> 的硬件修饰位。</para>
///
/// <para>对照抓包（宏"按下A，延时50ms，松开A"，槽 6）：
/// <c>00 08 00 01 09 06 30 32 00 04 B0 01 00 04 …… 21</c>。</para>
/// </summary>
public static class MacroCodec
{
    public const byte Cmd = 0x08;
    public const int MaxEventsPerFrame = 6;   // 单帧最多 6 个动作（@0x438b80: iVar3 < 7）
    public const int ChunkPayload = 0x1B;     // 分块时每块载荷 27 字节

    // 动作类型字节（wire）
    private const byte TKeyDown = 0x30;
    private const byte TKeyUp = 0xB0;
    private const byte TModDown = 0x20;       // 修饰键按下
    private const byte TModUp = 0xA0;         // 修饰键松开
    private const byte TMouseDown = 0x10;
    private const byte TMouseUp = 0x90;

    /// <summary>事件类型，对应官方 DB t_macrorecord.type。</summary>
    public enum EvType : byte { Delay = 1, KeyDown = 2, KeyUp = 3, MouseDown = 4, MouseUp = 5 }

    /// <summary>一个宏事件：type + value（见类注释）。1:1 对应官方 DB 的一条记录。</summary>
    public readonly record struct MacroEvent(EvType Type, int Value)
    {
        public static MacroEvent Delay(int ms) => new(EvType.Delay, ms);
        public static MacroEvent KeyDown(int vk) => new(EvType.KeyDown, vk);
        public static MacroEvent KeyUp(int vk) => new(EvType.KeyUp, vk);
        public static MacroEvent MouseDown(int buttonBit) => new(EvType.MouseDown, buttonBit);
        public static MacroEvent MouseUp(int buttonBit) => new(EvType.MouseUp, buttonBit);
    }

    /// <summary>
    /// 把一组事件编码成一个或多个 33 字节下发帧。<paramref name="macroSlot"/> 为宏槽，即被绑定物理键的
    /// 下标（官方 <c>key_code</c>；0-based，内部 +1 写入 buf[5]）。
    /// </summary>
    public static IReadOnlyList<byte[]> Encode(int macroSlot, IReadOnlyList<MacroEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        // 第一步：把 DB 记录序列压成 wire 动作流（延时并入前一个动作），复刻 GetMouseMacroData。
        var wire = new List<byte[]>();
        int pending = 0;
        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            if (ev.Type == EvType.Delay)
            {
                pending += Math.Clamp(ev.Value, 0, 0xFFFF);
                // 末尾延时直接补到最后一个动作（复刻 last-record 分支）
                if (i == events.Count - 1 && wire.Count > 0) SetDelay(wire[^1], pending);
                continue;
            }
            if (wire.Count > 0) SetDelay(wire[^1], Math.Max(pending, 1));  // 两动作之间最小 1ms
            pending = 0;
            wire.Add(BuildAction(ev));
        }

        // 第二步：帧体 = [宏槽+1] + 每个动作 4 字节，再按 SetMouseKeyMacroData 分帧。
        var body = new byte[1 + wire.Count * 4];
        body[0] = (byte)(macroSlot + 1);
        for (int i = 0; i < wire.Count; i++)
            wire[i].CopyTo(body, 1 + i * 4);

        var frames = new List<byte[]>();
        if (wire.Count <= MaxEventsPerFrame)
        {
            frames.Add(MouseReport.Build(Cmd, new byte[] { 0x00, 0x01, (byte)(wire.Count * 4 + 1) }, body));
            return frames;
        }

        int total = body.Length;
        int blocks = (total + ChunkPayload - 1) / ChunkPayload;
        for (int b = 0; b < blocks; b++)
        {
            int off = b * ChunkPayload;
            int len = Math.Min(ChunkPayload, total - off);
            byte header3 = (byte)((b << 4) | (blocks & 0x0F));
            var hdr = new byte[] { 0x00, header3, (byte)len };
            frames.Add(MouseReport.Build(Cmd, hdr, body.AsSpan(off, len)));
        }
        return frames;
    }

    /// <summary>一个动作（无延时）→ wire 4 字节。默认延时 1ms（后续延时记录会覆盖）。</summary>
    private static byte[] BuildAction(MacroEvent ev)
    {
        byte type, key;
        switch (ev.Type)
        {
            case EvType.KeyDown:
                if (IsModifierVk(ev.Value))
                {
                    type = TModDown;
                    key = KeyCodes.GetSysKeyCode(ev.Value);
                }
                else
                {
                    type = TKeyDown;
                    key = KeyCodes.VkToHidUsage(ev.Value);
                }
                break;

            case EvType.KeyUp:
                if (IsModifierVk(ev.Value))
                {
                    type = TModUp;
                    key = KeyCodes.GetSysKeyCode(ev.Value);
                }
                else if (ev.Value is 0x5B or 0x5D)  // 复刻官方：Win 键松开与按下同为 0x20
                {
                    type = TModDown;
                    key = KeyCodes.GetSysKeyCode(ev.Value);
                }
                else
                {
                    type = TKeyUp;
                    key = KeyCodes.VkToHidUsage(ev.Value);
                }
                break;

            case EvType.MouseDown:
                type = TMouseDown;
                key = (byte)ev.Value;
                break;

            case EvType.MouseUp:
                type = TMouseUp;
                key = (byte)ev.Value;
                break;

            default:
                type = 0x00;
                key = (byte)ev.Value;
                break;
        }

        return new byte[] { type, 0x01, 0x00, key };
    }

    private static void SetDelay(byte[] action, int ms)
    {
        action[1] = (byte)(ms & 0xFF);
        action[2] = (byte)((ms >> 8) & 0xFF);
    }

    /// <summary>修饰键 VK：0xA0..0xA7（复刻 GetMouseMacroData 的 <c>value-0xA0 &lt; 8</c>）。</summary>
    private static bool IsModifierVk(int vk) => vk is >= 0xA0 and <= 0xA7;
}