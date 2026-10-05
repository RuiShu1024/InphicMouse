namespace InphicMouse.Protocol.Codecs;

/// <summary>
/// 其余设置类下发：回报率(0x02/复位0x0D→0x12)、DPI 各档颜色(0x04)、
/// 传感器抬升(0x06)、休眠/移动唤醒(0x07)。均 1:1 复刻对应 SetMouse* 函数。
/// </summary>
public static class DeviceSettingsCodec
{
    private static byte[] Header(byte len) => new byte[] { 0x00, 0x01, len };

    // ---- 回报率 0x02 ----
    public const byte CmdReportRate = 0x02;
    public const byte CmdReportResetStep1 = 0x0D;
    public const byte CmdReportResetStep2 = 0x12;

    /// <summary><paramref name="rateCode"/> 为回报率档位码（非 Hz，码↔Hz 表待实机确认）。</summary>
    public static byte[] EncodeReportRate(int rateCode) =>
        MouseReport.Build(CmdReportRate, Header(0x01), stackalloc byte[] { (byte)rateCode });

    public static byte[] EncodeReportRateResetStep1() =>
        MouseReport.Build(CmdReportResetStep1, Header(0x01), stackalloc byte[] { 0x01 });

    public static byte[] EncodeReportRateResetStep2() =>
        MouseReport.Build(CmdReportResetStep2, Header(0x01), stackalloc byte[] { 0x00 });

    // ---- DPI 各档颜色 0x04 ----
    public const byte CmdDpiColor = 0x04;

    /// <summary>6 档 RGB 颜色，每档 3 字节（低位起 R,G,B）。不足 6 档补 0。</summary>
    public static byte[] EncodeDpiColors(IReadOnlyList<int> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        Span<byte> p = stackalloc byte[18]; // 6*3
        int n = Math.Min(6, colors.Count);
        for (int i = 0; i < n; i++)
        {
            int c = colors[i];
            p[i * 3] = (byte)(c & 0xFF);
            p[i * 3 + 1] = (byte)((c >> 8) & 0xFF);
            p[i * 3 + 2] = (byte)((c >> 16) & 0xFF);
        }
        return MouseReport.Build(CmdDpiColor, Header(0x12), p);
    }

    // ---- 传感器/抬升高度 0x06 ----
    public const byte CmdSensorLift = 0x06;

    /// <summary>抬升高度 + sensor_flag（bit3→buf[6], bit2→buf[7]）。</summary>
    public static byte[] EncodeSensorLift(int liftoffHeight, int sensorFlag)
    {
        Span<byte> p = stackalloc byte[3];
        p[0] = (byte)liftoffHeight;
        p[1] = (byte)((sensorFlag & 0x08) != 0 ? 1 : 0);
        p[2] = (byte)((sensorFlag & 0x04) != 0 ? 1 : 0);
        return MouseReport.Build(CmdSensorLift, Header(0x05), p);
    }

    // ---- 休眠/移动唤醒 0x07 ----
    public const byte CmdSleep = 0x07;

    public static byte[] EncodeSleep(int sleepLight, int moveWakeup, int moveCloseLight, int buttonRespondTime)
    {
        Span<byte> p = stackalloc byte[4];
        p[0] = (byte)sleepLight;
        p[1] = (byte)moveWakeup;
        p[2] = (byte)moveCloseLight;
        p[3] = (byte)buttonRespondTime;
        return MouseReport.Build(CmdSleep, Header(0x04), p);
    }
}
