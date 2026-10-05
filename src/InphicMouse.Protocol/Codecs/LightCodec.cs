namespace InphicMouse.Protocol.Codecs;

/// <summary>灯光模式下发（0x05）与复位（0x0E→0x15）。1:1 复刻 SetLightModeData / SetMouseResetLight。</summary>
public static class LightCodec
{
    public const byte CmdSet = 0x05;
    public const byte CmdResetStep1 = 0x0E;
    public const byte CmdResetStep2 = 0x15; // 读回当前灯光

    /// <summary>亮度/速度各 0..15，合并为 (亮度&lt;&lt;4)|速度。</summary>
    private static byte PackLevel(int brightness, int speed) =>
        (byte)(((brightness & 0x0F) << 4) | (speed & 0x0F));

    private static void WriteRgb(Span<byte> dst, int offset, int color)
    {
        dst[offset] = (byte)(color & 0xFF);
        dst[offset + 1] = (byte)((color >> 8) & 0xFF);
        dst[offset + 2] = (byte)((color >> 16) & 0xFF);
    }

    /// <summary>
    /// 组装灯光报文。<paramref name="modeId"/> 0..5；<paramref name="colors"/> 为该模式所需颜色
    /// （模式3 用 1 色，模式2 用 7 色，其余可为空）。颜色为 int（低位起 R,G,B）。
    /// </summary>
    public static byte[] Encode(int modeId, int brightness, int speed, IReadOnlyList<int>? colors = null)
    {
        byte level = PackLevel(brightness, speed);
        switch (modeId)
        {
            case 2: // 七彩波浪：7 组 RGB
            {
                if (colors is null || colors.Count < 7)
                    throw new ArgumentException("模式2(七彩波浪)需要 7 个颜色", nameof(colors));
                Span<byte> p = stackalloc byte[2 + 1 + 7 * 3]; // buf5,buf6, count, 7*RGB = 24
                p[0] = (byte)modeId;
                p[1] = level;
                p[2] = 0x07;
                for (int i = 0; i < 7; i++) WriteRgb(p, 3 + i * 3, colors[i]);
                return MouseReport.Build(CmdSet, Header(0x18), p);
            }
            case 3: // 常亮：单色 RGB
            {
                if (colors is null || colors.Count < 1)
                    throw new ArgumentException("模式3(常亮)需要 1 个颜色", nameof(colors));
                Span<byte> p = stackalloc byte[2 + 3];
                p[0] = (byte)modeId;
                p[1] = level;
                WriteRgb(p, 2, colors[0]);
                return MouseReport.Build(CmdSet, Header(0x05), p);
            }
            case 1:
            {
                Span<byte> p = stackalloc byte[3]; // buf5,buf6,buf7=0
                p[0] = (byte)modeId;
                p[1] = level;
                return MouseReport.Build(CmdSet, Header(0x03), p);
            }
            default: // 0/4/5：仅模式+亮度/速度
            {
                Span<byte> p = stackalloc byte[2];
                p[0] = (byte)modeId;
                p[1] = level;
                return MouseReport.Build(CmdSet, Header(0x02), p);
            }
        }
    }

    /// <summary>灯光复位第 1 帧（0x0E）。之后应 Sleep(100) 再发第 2 帧读回（0x15）。</summary>
    public static byte[] EncodeResetStep1() =>
        MouseReport.Build(CmdResetStep1, Header(0x01), stackalloc byte[] { 0x01 });

    public static byte[] EncodeResetStep2() =>
        MouseReport.Build(CmdResetStep2, Header(0x01), stackalloc byte[] { 0x00 });

    private static byte[] Header(byte len) => new byte[] { 0x00, 0x01, len };
}
