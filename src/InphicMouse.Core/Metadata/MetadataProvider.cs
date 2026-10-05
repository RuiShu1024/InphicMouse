using System.Collections.Generic;
using System.IO;
using System.Linq;
using InphicMouse.Core.Models;

namespace InphicMouse.Core.Metadata;

/// <summary>
/// 型号元数据来源解析：优先应用自带 <c>Metadata\</c> 目录，其次原程序安装目录
/// <c>C:\Program Files (x86)\Inphic Mouse</c>；都没有则用内置默认(6827)。
/// </summary>
public sealed class MetadataProvider
{
    public static readonly string[] DefaultSearchDirs =
    {
        Path.Combine(System.AppContext.BaseDirectory, "Metadata"),
        @"C:\Program Files (x86)\Inphic Mouse",
    };

    private readonly string? _root;

    /// <summary>找到的元数据根目录（含 config.xml），无则 null。</summary>
    public string? Root => _root;
    public bool HasMetadata => _root is not null;

    public MetadataProvider(IEnumerable<string>? searchDirs = null)
    {
        foreach (var dir in searchDirs ?? DefaultSearchDirs)
        {
            if (!string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "config.xml")))
            {
                _root = dir;
                break;
            }
        }
    }

    public IReadOnlyList<MouseModelInfo> LoadModels()
    {
        if (_root is null) return new[] { BuiltInModel };
        try
        {
            var models = ConfigXmlLoader.LoadModels(Path.Combine(_root, "config.xml"));
            return models.Count > 0 ? models : new[] { BuiltInModel };
        }
        catch { return new[] { BuiltInModel }; }
    }

    public DeviceCapabilities ResolveCapabilities(MouseModelInfo model)
    {
        if (_root is not null)
        {
            string path = Path.Combine(_root, "device", model.DeviceAttr + ".xml");
            if (File.Exists(path))
            {
                try { return DeviceXmlLoader.Load(path); }
                catch { /* 落到内置默认 */ }
            }
        }
        return BuiltInCapabilities;
    }

    // ---- 内置默认（无安装/无自带元数据时的兜底，近似 IN9 6827） ----

    public static readonly MouseModelInfo BuiltInModel = new()
    {
        Name = "IN9 (内置默认)",
        DeviceType = 104,
        DeviceAttr = "mouse_in9(6827)",
        Image = "",
        Modes = new List<DeviceMode>
        {
            new() { Value = 0, Desc = "USB", Vid = 0x248A, Pid = 0x8266, HidInterface = "MI_02" },
            new() { Value = 1, Desc = "2.4G", Vid = 0x249A, Pid = 0x8266, HidInterface = "MI_02" },
        },
    };

    public static readonly DeviceCapabilities BuiltInCapabilities = new()
    {
        ModelAttr = "mouse_in9(6827)",
        DpiCount = 6,
        DefaultDpiIndex = 3,
        DpiStages = new List<DpiStage>
        {
            new() { Index = 1, Value = 800,   ColorHex = "#FF0000" },
            new() { Index = 2, Value = 1600,  ColorHex = "#00FF00", IsDefault = true },
            new() { Index = 3, Value = 3200,  ColorHex = "#0000FF" },
            new() { Index = 4, Value = 6400,  ColorHex = "#FFFF00" },
            new() { Index = 5, Value = 12000, ColorHex = "#00FFFF" },
            new() { Index = 6, Value = 26000, ColorHex = "#FF00FF" },
        },
        ReportRateMax = 4,
        DefaultReportRate = 2,
        DefaultLightIndex = 1,
        LightModes = new List<LightModeInfo>
        {
            new() { Index = 1, Name = "流光", Enabled = true },
            new() { Index = 2, Name = "呼吸", Enabled = true },
            new() { Index = 3, Name = "七彩波浪", Enabled = true },
            new() { Index = 4, Name = "常亮", Enabled = true },
            new() { Index = 5, Name = "霓虹", Enabled = true },
            new() { Index = 6, Name = "关闭", Enabled = true },
        },
        SleepLight = 5,
        MoveWakeup = true,
        Keys = new List<MouseKeyDef>(),
        FirmwareUrl = null,
    };
}
