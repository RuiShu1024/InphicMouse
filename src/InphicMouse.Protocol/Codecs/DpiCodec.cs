namespace InphicMouse.Protocol.Codecs;

/// <summary>DPI 档位下发（0x03）与复位（0x0C）。1:1 复刻 SetMouseDPIData / SetMouseResetDPI。</summary>
public static class DpiCodec
{
    public const byte CmdSet = 0x03;
    public const byte CmdReset = 0x0C;

    /// <summary>
    /// 组装 DPI 档位报文。<paramref name="stageValues"/> 为各档 DPI 值（1..6 档，按档序），
    /// <paramref name="currentIndex"/> 为当前档 0-based，<paramref name="icType"/> 决定寄存器换算。
    /// </summary>
    public static byte[] EncodeStages(int icType, IReadOnlyList<int> stageValues, int currentIndex)
    {
        ArgumentNullException.ThrowIfNull(stageValues);
        if (stageValues.Count is < 1 or > 6)
            throw new ArgumentException("DPI 档数须为 1..6", nameof(stageValues));

        int count = stageValues.Count;
        Span<byte> payload = stackalloc byte[1 + count * 4];
        payload[0] = (byte)(((currentIndex & 0x0F) << 4) | (count & 0x0F));
        for (int i = 0; i < count; i++)
        {
            ushort reg = DpiConversion.ToRegister(icType, stageValues[i]);
            byte lo = (byte)(reg & 0xFF), hi = (byte)(reg >> 8);
            int o = 1 + i * 4;
            payload[o] = lo; payload[o + 1] = hi;    // X
            payload[o + 2] = lo; payload[o + 3] = hi; // Y（与 X 相同）
        }
        // DPI 命令的 buf[4] 为原程序固定值 0x25（含义待实机确认，按原样复刻）。
        return MouseReport.Build(CmdSet, stackalloc byte[] { 0x00, 0x01, 0x25 }, payload);
    }

    /// <summary>DPI 恢复默认（0x0C）。</summary>
    public static byte[] EncodeReset() =>
        MouseReport.Build(CmdReset, stackalloc byte[] { 0x00, 0x01, 0x01 }, stackalloc byte[] { 0x01 });
}
