namespace InphicMouse.Protocol;

/// <summary>
/// 原程序下发报文的统一框架：固定 33 字节（0x21）。
/// 布局（见 docs/hid-protocol.md，逆向自 DriverComm::SetDeviceData）：
/// <code>
///  offset 0    | 1        | 2 3 4      | 5 .......... 31        | 32
///         报告ID| 命令码    | 头部(3字节) | 载荷(27字节,参与校验)   | 校验和
/// </code>
/// 校验和 = (sum(buf[5..31])) &amp; 0xFF。头部(buf[1..4])不参与校验。
/// </summary>
public static class MouseReport
{
    public const int Length = 0x21;          // 33
    public const int PayloadStart = 5;
    public const int PayloadEnd = 31;        // 含
    public const int ChecksumIndex = 32;

    /// <summary>
    /// 组装一帧下发报文。<paramref name="command"/> 写入 buf[1]；<paramref name="header"/> 写入 buf[2..4]
    /// （最多 3 字节，不足补 0）；<paramref name="payload"/> 从 buf[5] 起写入（最多 27 字节）。
    /// 自动计算并写入 buf[32] 校验和。buf[0] 为报告 ID，默认 0。
    /// </summary>
    public static byte[] Build(byte command, ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload, byte reportId = 0)
    {
        if (header.Length > 3)
            throw new ArgumentException("header 最多 3 字节", nameof(header));
        if (payload.Length > PayloadEnd - PayloadStart + 1)
            throw new ArgumentException("payload 最多 27 字节", nameof(payload));

        var buf = new byte[Length];
        buf[0] = reportId;
        buf[1] = command;
        for (int i = 0; i < header.Length; i++) buf[2 + i] = header[i];
        for (int i = 0; i < payload.Length; i++) buf[PayloadStart + i] = payload[i];
        buf[ChecksumIndex] = Checksum(buf);
        return buf;
    }

    /// <summary>计算 buf[5..31] 的字节和低 8 位。</summary>
    public static byte Checksum(ReadOnlySpan<byte> buf)
    {
        int sum = 0;
        for (int i = PayloadStart; i <= PayloadEnd && i < buf.Length; i++) sum += buf[i];
        return (byte)(sum & 0xFF);
    }

    /// <summary>校验一帧报文的 buf[32] 是否与重算的校验和一致。</summary>
    public static bool Verify(ReadOnlySpan<byte> buf) =>
        buf.Length >= Length && buf[ChecksumIndex] == Checksum(buf);
}
