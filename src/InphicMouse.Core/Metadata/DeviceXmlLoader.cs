using InphicMouse.Core.Models;

namespace InphicMouse.Core.Metadata;

/// <summary>解析原程序 device/*.xml，得到某型号的完整能力元数据(DPI/回报率/灯光/按键/固件等)。</summary>
public static class DeviceXmlLoader
{
    public static DeviceCapabilities Load(string deviceXmlPath)
    {
        var doc = XmlUtil.LoadLenient(deviceXmlPath);
        var root = doc.Root!;                       // <mouse>
        var info = root.Element("mouse_info");

        // DPI
        var dpiInfo = info?.Element("dpi_info");
        int dpiCount = XmlUtil.ParseInt(dpiInfo?.Attribute("dpi_count")?.Value);
        int defaultDpi = XmlUtil.ParseInt(dpiInfo?.Attribute("default_dpi")?.Value);
        var dpiStages = new List<DpiStage>();
        if (dpiInfo is not null)
        {
            for (int i = 1; i <= dpiCount; i++)
            {
                var el = dpiInfo.Element($"dpi_{i}");
                if (el is null) continue;
                dpiStages.Add(new DpiStage
                {
                    Index = i,
                    Value = XmlUtil.ParseInt(el.Attribute("value")?.Value),
                    ColorHex = el.Attribute("color")?.Value ?? "#FFFFFF",
                    IsDefault = i == defaultDpi,
                });
            }
        }

        // 灯光模式（数量不定，按 light_1.. 递增直到缺失）
        var lightInfo = info?.Element("light_info");
        int defaultLight = XmlUtil.ParseInt(lightInfo?.Attribute("default_light")?.Value);
        var lights = new List<LightModeInfo>();
        if (lightInfo is not null)
        {
            for (int i = 1; ; i++)
            {
                var el = lightInfo.Element($"light_{i}");
                if (el is null) break;
                lights.Add(new LightModeInfo
                {
                    Index = i,
                    Name = el.Attribute("name")?.Value ?? "",
                    Enabled = XmlUtil.ParseInt(el.Attribute("enable")?.Value) == 1,
                });
            }
        }

        // 按键
        var keys = new List<MouseKeyDef>();
        foreach (var k in root.Element("mouse_key")?.Elements("key") ?? [])
        {
            keys.Add(new MouseKeyDef
            {
                Name = XmlUtil.ParseInt(k.Attribute("name")?.Value),
                KeyValue = XmlUtil.ParseInt(k.Attribute("key_value")?.Value),
                Direction = XmlUtil.ParseInt(k.Attribute("direction")?.Value),
                Rect = k.Attribute("rect")?.Value ?? "",
                FuncType = XmlUtil.ParseInt(k.Attribute("func_type")?.Value),
                FuncValue = XmlUtil.ParseInt(k.Attribute("func_value")?.Value),
                FuncDesc = XmlUtil.ParseInt(k.Attribute("func_desc")?.Value),
            });
        }

        var rr = info?.Element("report_rate");
        var imgFile = root.Element("image_file");
        return new DeviceCapabilities
        {
            ModelAttr = Path.GetFileNameWithoutExtension(deviceXmlPath),
            HomeImage = imgFile?.Attribute("home_img")?.Value ?? "",
            DpiCount = dpiCount,
            DefaultDpiIndex = defaultDpi,
            DpiStages = dpiStages,
            ReportRateMax = XmlUtil.ParseInt(rr?.Attribute("report_max")?.Value),
            DefaultReportRate = XmlUtil.ParseInt(rr?.Attribute("default_value")?.Value),
            DefaultLightIndex = defaultLight,
            LightModes = lights,
            SleepLight = XmlUtil.ParseInt(info?.Element("sleep_light")?.Attribute("value")?.Value),
            MoveWakeup = XmlUtil.ParseInt(info?.Element("move_wakeup")?.Attribute("value")?.Value) == 1,
            MoveCloseLight = XmlUtil.ParseInt(info?.Element("move_closelight")?.Attribute("value")?.Value) == 1,
            Keys = keys,
            FirmwareUrl = root.Element("firmware")?.Attribute("url")?.Value,
        };
    }
}
