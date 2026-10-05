namespace InphicMouse.Protocol;

/// <summary>
/// DPI 值 ⇄ 硬件寄存器值换算，1:1 复刻 <c>GetMouseDPIValue</c>(@0x41ee80) 与其逆
/// <c>GetDPIValueByMouseDPI</c>(@0x41efe0)。换算依传感器 <c>ic_type</c>（来自设备信息 dev_info[0x0A]）。
/// 所有除法均为向下取整整数除法，与原程序一致。
/// </summary>
public static class DpiConversion
{
    public const int DpiMin = 50;
    public const int DpiMax = 26000;

    /// <summary>把 DPI 值裁剪到 [50, 26000]（原程序在换算前做的裁剪）。</summary>
    public static int Clamp(int dpi) => dpi < DpiMin ? DpiMin : (dpi > DpiMax ? DpiMax : dpi);

    /// <summary>DPI 值 → 16 位寄存器值。<paramref name="dpi"/> 会先被裁剪。</summary>
    public static ushort ToRegister(int icType, int dpi)
    {
        int d = Clamp(dpi);
        int r = icType switch
        {
            0x11 => d > 12999 ? (d - 13000) / 1000 + 221
                  : d < 10001 ? d / 50
                  : (d - 10000) / 100 + 200,
            0x05 => d < 6401 ? d / 50 : (d - 6400) / 100 + 128,
            0x25 => d > 5000 ? (d - 5000) / 500 + 50 : d / 100,
            0x26 => d < 5001 ? d / 100 - 1 : (d - 5000) / 500 + 50,
            0x31 => d > 12000 ? (d - 12000) / 1000 + 120 : d / 100,
            0x04 or 0x10 or 0x12 or 0x35 => d / 100,
            _ => d / 50 - 1,
        };
        return (ushort)(r < 0 ? 0 : r);
    }

    /// <summary>16 位寄存器值 → DPI 值（读回时用），复刻 GetDPIValueByMouseDPI(@0x41efe0)。</summary>
    public static int FromRegister(int icType, int reg)
    {
        return icType switch
        {
            0x11 => reg > 0xDC ? (reg + 1) * 50        // >220: (reg+1)*50
                  : reg > 199 ? (reg - 100) * 100      // 200..220
                  : reg * 50,                          // ≤199
            0x05 => reg > 0x80 ? (reg - 0x40) * 100 : reg * 50,
            0x25 => reg > 0x31 ? (reg - 0x28) * 500 : reg * 100,
            0x26 => reg > 0x31 ? (reg - 0x27) * 500 : reg * 100,
            0x31 => reg > 0x78 ? (reg - 0x6D) * 1000 : reg * 100,
            0x04 or 0x10 or 0x12 or 0x35 => reg * 100,
            _ => (reg + 1) * 50,
        };
    }
}
