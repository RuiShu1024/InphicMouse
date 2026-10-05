using System.Collections.Generic;
using System.Threading.Tasks;
using InphicMouse.Core.Models;
using InphicMouse.Protocol.Codecs;

namespace InphicMouse.Protocol.Services;

/// <summary>
/// 鼠标设备的高层服务：连接、读回、下发各类设置。UI 只依赖此抽象，
/// 真机(<c>RealMouseService</c>)与无设备演示(<c>SimulatedMouseService</c>)可互换。
/// 所有下发方法均返回是否成功，并就地更新 <see cref="Settings"/> 快照。
/// </summary>
public interface IMouseService
{
    MouseStatus Status { get; }
    DeviceCapabilities? Capabilities { get; }
    MouseSettings Settings { get; }

    /// <summary>连接状态或设备信息(电量/固件)变化时触发。</summary>
    event System.EventHandler? StatusChanged;

    /// <summary>尝试连接（枚举/定位设备，读回当前设置）。返回是否连上真实设备。</summary>
    Task<bool> ConnectAsync();

    /// <summary>刷新设备信息（电量/充电/固件/在线），对应 0x10。</summary>
    Task RefreshInfoAsync();

    Task<bool> ApplyDpiAsync();
    Task<bool> ApplyDpiColorsAsync();
    Task<bool> ApplyReportRateAsync();
    Task<bool> ApplyLightAsync();
    Task<bool> ApplyKeyMapAsync();

    /// <summary>按给定条目下发按键映射(0x09)。用于"UI 保存逻辑条目、下发改写后的线字节"的场景。</summary>
    Task<bool> ApplyKeyMapAsync(System.Collections.Generic.IReadOnlyList<KeyMapCodec.KeyEntry> keys);
    Task<bool> ApplySleepAsync();
    Task<bool> ApplySensorAsync();

    /// <summary>上传宏内容到指定宏槽(0x08)。<paramref name="events"/> 为宏事件序列。</summary>
    Task<bool> ApplyMacroAsync(int macroSlot, System.Collections.Generic.IReadOnlyList<InphicMouse.Protocol.Codecs.MacroCodec.MacroEvent> events);

    Task<bool> ResetDpiAsync();
    Task<bool> ResetLightAsync();
    Task<bool> ResetReportRateAsync();

    /// <summary>发若干读命令(0x10/0x12/0x13/0x15…)并返回原始字节转储，用于实机校准协议。</summary>
    Task<string> RunDiagnosticsAsync();

    /// <summary>读当前灯光(0x15)，返回原始字节与候选模式 id，用于校准灯效↔id。</summary>
    Task<string> ReadLightModeAsync();

    /// <summary>读回宏内容(0x18)原始字节，用于校准宏事件 4 字节格式。<paramref name="macroSlot"/> 0-based。</summary>
    Task<string> ReadMacroAsync(int macroSlot);
}
