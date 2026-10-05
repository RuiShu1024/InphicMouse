namespace InphicMouse.Protocol.Codecs;

/// <summary>设备信息读回（0x10）解析：电量/充电/固件/IC/在线。对应 GetDeviceInfo + GetMouseData 的 dev_info 解析。</summary>
public static class DeviceInfoCodec
{
    public const byte Cmd = 0x10;

    /// <summary>组装 0x10 读请求（载荷全 0）。</summary>
    public static byte[] EncodeRequest() =>
        MouseReport.Build(Cmd, stackalloc byte[] { 0x00, 0x01, 0x00 }, ReadOnlySpan<byte>.Empty);

    /// <summary>解析 dev_info 响应缓冲区（需至少 0x0F 字节）。</summary>
    public static DeviceInfo? Parse(ReadOnlySpan<byte> devInfo)
    {
        if (devInfo.Length < 0x0F) return null;
        return new DeviceInfo
        {
            FirmwareVersion = (ushort)(devInfo[9] << 8 | devInfo[8]),
            IcType = devInfo[0x0A],
            ReportRateMaxIndex = devInfo[0x0B] switch { 0x10 => 5, 0x20 => 6, 0x40 => 7, _ => 4 },
            IsCharging = devInfo[0x0C] != 0,
            BatteryPercent = devInfo[0x0D],
            IsOnline = devInfo[0x0E] != 0,
        };
    }
}

/// <summary>从 dev_info 解析出的设备状态。</summary>
public sealed record DeviceInfo
{
    public ushort FirmwareVersion { get; init; }
    public byte IcType { get; init; }
    public int ReportRateMaxIndex { get; init; }
    public bool IsCharging { get; init; }
    public byte BatteryPercent { get; init; }
    public bool IsOnline { get; init; }

    /// <summary>固件版本按 16 进制数字显示（实机核对：0x0142 → "1.42"，官方也显示 v1.42）。</summary>
    public string FirmwareText => $"{FirmwareVersion >> 8:X}.{FirmwareVersion & 0xFF:X2}";
}
