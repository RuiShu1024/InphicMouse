namespace InphicMouse.Core.Models;

/// <summary>设备连接模式，对应 config.xml 的 &lt;mode&gt;。</summary>
public sealed record DeviceMode
{
    public int Value { get; init; }            // 0 = USB, 1 = 2.4G
    public string Desc { get; init; } = "";
    public ushort Vid { get; init; }
    public ushort Pid { get; init; }
    public string DevId { get; init; } = "";
    public string HidInterface { get; init; } = "";
    public bool IsWireless => Value == 1;
}

/// <summary>鼠标型号定义，对应 config.xml 的 &lt;mouse&gt;。</summary>
public sealed record MouseModelInfo
{
    public string Name { get; init; } = "";
    public int DeviceType { get; init; }
    /// <summary>如 "mouse_in9(6827)"，对应 device 目录下的 xml 及 skins 中的设备图。</summary>
    public string DeviceAttr { get; init; } = "";
    public string Image { get; init; } = "";
    public IReadOnlyList<DeviceMode> Modes { get; init; } = [];
}
