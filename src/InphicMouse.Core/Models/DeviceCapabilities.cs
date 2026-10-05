namespace InphicMouse.Core.Models;

/// <summary>一个 DPI 档位，对应 device xml 的 &lt;dpi_n value color&gt;。</summary>
public sealed record DpiStage
{
    public int Index { get; init; }        // 1-based
    public int Value { get; init; }        // DPI 值
    public string ColorHex { get; init; } = "#FFFFFF";
    public bool IsDefault { get; init; }
}

/// <summary>灯光模式，对应 device xml 的 &lt;light_n name enable&gt;。</summary>
public sealed record LightModeInfo
{
    public int Index { get; init; }        // 1-based
    public string Name { get; init; } = "";
    public bool Enabled { get; init; }
}

/// <summary>鼠标按键定义，对应 device xml 的 &lt;key&gt;。</summary>
public sealed record MouseKeyDef
{
    public int Name { get; init; }         // 语言表 ID
    public int KeyValue { get; init; }     // 物理键索引
    public int Direction { get; init; }
    public string Rect { get; init; } = "";
    public int FuncType { get; init; }
    public int FuncValue { get; init; }
    public int FuncDesc { get; init; }     // 语言表 ID
}

/// <summary>某个型号的完整能力元数据，由 device xml 解析而来，用于驱动 UI 动态生成。</summary>
public sealed class DeviceCapabilities
{
    public string ModelAttr { get; init; } = "";
    public string HomeImage { get; init; } = "";

    public int DpiCount { get; init; }
    public int DefaultDpiIndex { get; init; }
    public List<DpiStage> DpiStages { get; init; } = [];

    public int ReportRateMax { get; init; }
    public int DefaultReportRate { get; init; }

    public int DefaultLightIndex { get; init; }
    public List<LightModeInfo> LightModes { get; init; } = [];

    public int SleepLight { get; init; }
    public bool MoveWakeup { get; init; }
    public bool MoveCloseLight { get; init; }

    public List<MouseKeyDef> Keys { get; init; } = [];

    public string? FirmwareUrl { get; init; }
}
