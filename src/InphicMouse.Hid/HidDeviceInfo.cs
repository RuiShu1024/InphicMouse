namespace InphicMouse.Hid;

/// <summary>一个 HID 设备接口(collection)的枚举信息。</summary>
public sealed record HidDeviceInfo
{
    public required string Path { get; init; }
    public ushort VendorId { get; init; }
    public ushort ProductId { get; init; }
    public ushort UsagePage { get; init; }
    public ushort Usage { get; init; }
    public ushort FeatureReportByteLength { get; init; }
    public ushort InputReportByteLength { get; init; }
    public ushort OutputReportByteLength { get; init; }
    public string? ProductString { get; init; }

    /// <summary>设备接口路径通常含 "mi_02"，用于精确定位复合设备的指定接口。</summary>
    public bool MatchesInterface(int mi) =>
        Path.Contains($"mi_{mi:D2}", StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        $"VID_{VendorId:X4}&PID_{ProductId:X4} UP={UsagePage:X4} U={Usage:X4} feat={FeatureReportByteLength} \"{ProductString}\"";
}
