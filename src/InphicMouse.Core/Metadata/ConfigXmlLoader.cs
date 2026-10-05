using System.Globalization;
using InphicMouse.Core.Models;

namespace InphicMouse.Core.Metadata;

/// <summary>解析原程序 config.xml，得到型号目录（型号 + 各连接模式的 VID/PID/接口）。</summary>
public static class ConfigXmlLoader
{
    public static IReadOnlyList<MouseModelInfo> LoadModels(string configXmlPath)
    {
        var doc = XmlUtil.LoadLenient(configXmlPath);
        var models = new List<MouseModelInfo>();
        var deviceEl = doc.Root?.Element("device");
        if (deviceEl is null) return models;

        foreach (var m in deviceEl.Elements("mouse"))
        {
            var modes = m.Elements("mode").Select(mode => new DeviceMode
            {
                Value = XmlUtil.ParseInt(mode.Attribute("value")?.Value),
                Desc = mode.Attribute("desc")?.Value ?? "",
                Vid = ParseHex(mode.Attribute("vid")?.Value),
                Pid = ParseHex(mode.Attribute("pid")?.Value),
                DevId = mode.Attribute("dev_id")?.Value ?? "",
                HidInterface = mode.Attribute("hid_interface")?.Value ?? "",
            }).ToList();

            models.Add(new MouseModelInfo
            {
                Name = m.Attribute("name")?.Value ?? "",
                DeviceType = XmlUtil.ParseInt(m.Attribute("device_type")?.Value),
                DeviceAttr = m.Attribute("device_attr")?.Value ?? "",
                Image = m.Attribute("image")?.Value ?? "",
                Modes = modes,
            });
        }
        return models;
    }

    private static ushort ParseHex(string? s) =>
        ushort.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort v) ? v : (ushort)0;
}
