using System;
using System.Threading.Tasks;
using InphicMouse.Core.Models;
using InphicMouse.Protocol.Codecs;

namespace InphicMouse.Protocol.Services;

/// <summary>
/// 无真实设备时的演示实现：在内存中维护一份状态，所有下发即时"成功"并回写快照。
/// 用于开发机(无鼠标虚拟机)跑通界面与自动化验证。UI 会显示"演示模式"标识。
/// </summary>
public sealed class SimulatedMouseService : IMouseService
{
    public MouseStatus Status { get; }
    public DeviceCapabilities? Capabilities { get; }
    public MouseSettings Settings { get; }

    public event EventHandler? StatusChanged;

    public SimulatedMouseService(DeviceCapabilities caps, string modelName)
    {
        Capabilities = caps;
        Settings = MouseSettings.FromCapabilities(caps);
        Status = new MouseStatus
        {
            IsConnected = true,
            IsSimulated = true,
            Mode = LinkMode.Wireless,
            ModelName = modelName,
            Info = new DeviceInfo
            {
                FirmwareVersion = 0x0102,
                IcType = 0x25,
                ReportRateMaxIndex = Math.Max(1, caps.ReportRateMax),
                IsCharging = false,
                BatteryPercent = 85,
                IsOnline = true,
            },
        };
    }

    public Task<bool> ConnectAsync()
    {
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(false); // 非真实设备
    }

    public Task RefreshInfoAsync()
    {
        // 演示：电量缓慢波动一点，制造"活着"的感觉
        if (Status.Info is { } info)
        {
            int p = info.BatteryPercent;
            p = p <= 5 ? 100 : p - 1;
            Status.Info = info with { BatteryPercent = (byte)p };
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        return Task.CompletedTask;
    }

    public Task<bool> ApplyDpiAsync() => Ok();
    public Task<bool> ApplyDpiColorsAsync() => Ok();
    public Task<bool> ApplyReportRateAsync() => Ok();
    public Task<bool> ApplyLightAsync() => Ok();
    public Task<bool> ApplyKeyMapAsync() => Ok();
    public Task<bool> ApplyKeyMapAsync(System.Collections.Generic.IReadOnlyList<KeyMapCodec.KeyEntry> keys) => Ok();
    public Task<bool> ApplyMacroAsync(int macroSlot, System.Collections.Generic.IReadOnlyList<InphicMouse.Protocol.Codecs.MacroCodec.MacroEvent> events) => Ok();
    public Task<bool> ApplySleepAsync() => Ok();
    public Task<bool> ApplySensorAsync() => Ok();

    public Task<bool> ResetDpiAsync()
    {
        if (Capabilities is { } c)
        {
            Settings.DpiValues.Clear();
            foreach (var st in c.DpiStages) Settings.DpiValues.Add(st.Value);
            Settings.CurrentDpiIndex = Math.Max(0, c.DefaultDpiIndex - 1);
        }
        return Ok();
    }

    public Task<bool> ResetLightAsync()
    {
        if (Capabilities is { } c) Settings.LightMode = Math.Max(0, c.DefaultLightIndex - 1);
        return Ok();
    }

    public Task<bool> ResetReportRateAsync()
    {
        if (Capabilities is { } c) Settings.ReportRateCode = c.DefaultReportRate;
        return Ok();
    }

    private static Task<bool> Ok() => Task.FromResult(true);

    public Task<string> RunDiagnosticsAsync() =>
        Task.FromResult("演示模式：未连接真实鼠标，无原始字节。请在插着鼠标的物理机上运行。");

    public Task<string> ReadLightModeAsync() =>
        Task.FromResult("演示模式：无法读取真实灯光模式。");

    public Task<string> ReadMacroAsync(int macroSlot) =>
        Task.FromResult("演示模式：无法读取真实宏内容。请在插着鼠标的物理机上运行。");
}
