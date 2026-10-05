using InphicMouse.Core.Models;
using InphicMouse.Protocol.Codecs;

namespace InphicMouse.Protocol.Services;

/// <summary>连接模式。</summary>
public enum LinkMode { Usb, Wireless }

/// <summary>设备当前的可写设置快照（读回或用户修改后就地更新）。UI 双向绑定的数据来源。</summary>
public sealed class MouseSettings
{
    /// <summary>各 DPI 档当前值（按档序）。</summary>
    public List<int> DpiValues { get; } = new();

    /// <summary>各 DPI 档颜色（int，低位起 R,G,B）。</summary>
    public List<int> DpiColors { get; } = new();

    /// <summary>当前选中 DPI 档（0-based）。</summary>
    public int CurrentDpiIndex { get; set; }

    /// <summary>回报率档位码（非 Hz）。</summary>
    public int ReportRateCode { get; set; }

    public int LightMode { get; set; }
    public int LightBrightness { get; set; } = 4;
    public int LightSpeed { get; set; } = 4;

    /// <summary>灯光颜色（常亮 1 色 / 七彩波浪 7 色）。</summary>
    public List<int> LightColors { get; } = new();

    /// <summary>5 个物理键的功能条目。</summary>
    public List<KeyMapCodec.KeyEntry> Keys { get; } = new();

    public int SleepLight { get; set; }
    public bool MoveWakeup { get; set; }
    public bool MoveCloseLight { get; set; }
    public int ButtonRespondTime { get; set; }

    public int LiftoffHeight { get; set; }
    public int SensorFlag { get; set; }

    /// <summary>用型号能力元数据初始化一份默认设置。</summary>
    public static MouseSettings FromCapabilities(DeviceCapabilities caps)
    {
        var s = new MouseSettings
        {
            CurrentDpiIndex = Math.Max(0, caps.DefaultDpiIndex - 1),
            ReportRateCode = caps.DefaultReportRate,
            LightMode = Math.Max(0, caps.DefaultLightIndex - 1),
            SleepLight = caps.SleepLight,
            MoveWakeup = caps.MoveWakeup,
            MoveCloseLight = caps.MoveCloseLight,
        };
        foreach (var st in caps.DpiStages)
        {
            s.DpiValues.Add(st.Value);
            s.DpiColors.Add(ColorUtil.FromHex(st.ColorHex));
        }
        // 默认键位模板（左/右/中/侧1/侧2）
        s.Keys.AddRange(new[]
        {
            KeyMapCodec.KeyEntry.Mouse(0x01),
            KeyMapCodec.KeyEntry.Mouse(0x02),
            KeyMapCodec.KeyEntry.Mouse(0x04),
            KeyMapCodec.KeyEntry.Mouse(0x08),
            KeyMapCodec.KeyEntry.Mouse(0x10),
        });
        // 常亮默认色 + 七彩波浪 7 色占位
        s.LightColors.Add(0x00FFFFFF);
        return s;
    }
}

/// <summary>设备连接与信息状态。</summary>
public sealed class MouseStatus
{
    public bool IsConnected { get; set; }
    public bool IsSimulated { get; set; }
    public LinkMode Mode { get; set; }
    public string ModelName { get; set; } = "";
    public DeviceInfo? Info { get; set; }

    public string ModeText => Mode == LinkMode.Wireless ? "2.4G 无线" : "USB 有线";
    public int BatteryPercent => Info?.BatteryPercent ?? 0;
    public bool IsCharging => Info?.IsCharging ?? false;
    public string FirmwareText => Info?.FirmwareText ?? "—";

    /// <summary>接收器在但无线鼠标是否在线(唤醒)。已读到设备信息且 online=0 视为休眠/无设备。</summary>
    public bool IsOnline => IsConnected && (Info?.IsOnline ?? true);
}

/// <summary>颜色工具：#RRGGBB ⇄ int(低位起 R,G,B，与报文一致)。</summary>
public static class ColorUtil
{
    public static int FromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return 0xFFFFFF;
        hex = hex.TrimStart('#');
        if (hex.Length == 8) hex = hex[2..]; // 丢弃 alpha
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
            return 0xFFFFFF;
        int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        return r | (g << 8) | (b << 16); // 低位起 R,G,B
    }

    public static (byte R, byte G, byte B) ToRgb(int color) =>
        ((byte)(color & 0xFF), (byte)((color >> 8) & 0xFF), (byte)((color >> 16) & 0xFF));

    public static string ToHex(int color)
    {
        var (r, g, b) = ToRgb(color);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
