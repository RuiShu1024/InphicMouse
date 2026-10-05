using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InphicMouse.Core.Models;
using InphicMouse.Hid;
using InphicMouse.Protocol.Codecs;
using InphicMouse.Protocol.Comm;

namespace InphicMouse.Protocol.Services;

/// <summary>
/// 真实设备实现：定位鼠标 → 打开通信链路 → 读设备信息 → 用各 Codec 组帧下发。
/// 下发字节与原程序 1:1（见各 Codec）。读回完整设置(0x12-0x17)为后续在物理机验证的 TODO，
/// 现阶段连接后以型号默认值初始化设置快照。
/// </summary>
public sealed class RealMouseService : IMouseService
{
    private readonly IReadOnlyList<MouseModelInfo> _models;
    private readonly Func<MouseModelInfo, DeviceCapabilities> _capsResolver;
    private MouseCommLink? _link;
    private byte _icType = 0x25;
    private readonly object _io = new();   // 串行化所有 HID 读写，避免定时刷新与用户操作并发导致错读/崩溃
    private readonly System.Threading.Timer _monitor;   // 插拔检测(轮询接口路径，约 1s)
    private int _refreshCounter;
    private ushort _cachedFw;   // 无线固件读一次即缓存
    private int _appliedLightMode = -1;   // 上次下发/读回的灯光模式 id，用于判断是否需强制转换刷新颜色

    public MouseStatus Status { get; } = new();
    public DeviceCapabilities? Capabilities { get; private set; }
    public MouseSettings Settings { get; private set; } = MouseSettings.FromCapabilities(new DeviceCapabilities());

    public event EventHandler? StatusChanged;

    public RealMouseService(IReadOnlyList<MouseModelInfo> models,
                            Func<MouseModelInfo, DeviceCapabilities> capsResolver)
    {
        _models = models;
        _capsResolver = capsResolver;
        // 未连接时也用首个型号的默认能力，让各页面能正常渲染。
        if (models.Count > 0)
        {
            Capabilities = capsResolver(models[0]);
            Settings = MouseSettings.FromCapabilities(Capabilities);
            Status.ModelName = models[0].Name;
        }
        // 一次性重装的计时器：每次 Poll 完再重新计时，避免回调重叠。
        _monitor = new System.Threading.Timer(_ => { try { Poll(); } finally { _monitor!.Change(1000, System.Threading.Timeout.Infinite); } },
                                              null, 400, System.Threading.Timeout.Infinite);
    }

    public Task<bool> ConnectAsync() => Task.Run(() =>
    {
        bool ok; lock (_io) { ok = TryConnectLocked(); }
        Raise();
        return ok;
    });

    /// <summary>插拔轮询：设备出现则自动连接、消失则断开；已连接时刷新(约每秒)。</summary>
    private void Poll()
    {
        bool changed = false;
        lock (_io)
        {
            bool present = DeviceLocator.IsPresent(_models);
            if (present && !Status.IsConnected) changed = TryConnectLocked();
            else if (!present && Status.IsConnected) { DisconnectLocked(); changed = true; }
            else if (present && Status.IsConnected)
            {
                ReadInfo();                 // 0x10：电量/在线/断连(快)
                changed = true;
                if (Status.Info?.IsOnline ?? false)
                {
                    ReadDpiIndexLocked();   // 0x13 当前档：物理 DPI 键切换约 1s 内跟手
                    if (++_refreshCounter >= 5) { _refreshCounter = 0; ReadBackSettings(); }
                }
            }
        }
        if (changed) Raise();
    }

    /// <summary>轻量读当前 DPI 档(0x13)，只更新当前档索引，不覆盖用户可能在编辑的档位数值。</summary>
    private void ReadDpiIndexLocked()
    {
        if (_link is null || !_link.IsOpen) return;
        var dpi = _link.Query(ReadFrame(0x13), MouseCommLink.InfoRetries);
        if (dpi is not null && dpi.Length > 4)
        {
            int cur = dpi[4] >> 4, count = dpi[4] & 0x0F;
            if (count is > 0 and <= 6) Settings.CurrentDpiIndex = Math.Clamp(cur, 0, count - 1);
        }
    }

    // 需在持有 _io 时调用（lock 可重入，内部 ReadInfo/ReadBackSettings 再次取锁无碍）。
    private bool TryConnectLocked()
    {
        if (_link is { IsOpen: true } && Status.IsConnected) return true;
        var located = DeviceLocator.Find(_models);
        if (located is null) return false;
        var handle = HidDeviceHandle.Open(located.Device);
        if (handle is null) return false;

        _link?.Dispose();
        _link = new MouseCommLink(handle);
        Capabilities = _capsResolver(located.Model);
        Settings = MouseSettings.FromCapabilities(Capabilities);
        Status.IsConnected = true;
        Status.IsSimulated = false;
        Status.ModelName = located.Model.Name;
        Status.Mode = located.Mode.IsWireless ? LinkMode.Wireless : LinkMode.Usb;

        ReadInfo();
        ReadBackSettings();
        return Status.IsConnected;
    }

    private void DisconnectLocked()
    {
        _link?.Dispose();
        _link = null;
        _cachedFw = 0;
        _appliedLightMode = -1;
        Status.IsConnected = false;
        Status.Info = null;
    }

    public Task RefreshInfoAsync() => Task.Run(() => { ReadInfo(); ReadBackSettings(); Raise(); });

    private void ReadInfo()
    {
        lock (_io)
        {
            if (_link is null || !_link.IsOpen) return;
            // Exchange 返回的响应已剥掉 report id，与逆向文档 dev_info 偏移一致。
            var r = _link.Exchange(DeviceInfoCodec.EncodeRequest(), MouseCommLink.InfoRetries);
            if (!r.WriteOk) { MarkDisconnected(); return; }   // 写失败=设备已拔出
            if (r.Response is null) return;
            var info = DeviceInfoCodec.Parse(r.Response);
            if (info is null) return;
            // 2.4G 模式下 0x10 不含固件版本(=0)，改从 0x20 无线固件读一次并缓存(避免每秒重读)。
            if (info.FirmwareVersion == 0)
            {
                if (_cachedFw == 0)
                {
                    var wf = _link.Query(ReadFrame(0x20), MouseCommLink.InfoRetries);
                    if (wf is not null && wf.Length > 5) _cachedFw = (ushort)(wf[5] << 8 | wf[4]);
                }
                if (_cachedFw != 0) info = info with { FirmwareVersion = _cachedFw };
            }
            Status.Info = info;
            _icType = info.IcType;
        }
    }

    /// <summary>读回设备当前 DPI(0x13)/回报率(0x12)/灯光(0x15)/传感器(0x16)/休眠(0x17) 到设置快照，
    /// 让 UI 反映设备真实状态（如物理 DPI 键切换后的当前档）。响应已剥 report id：[3]=len，[4]=payload0。</summary>
    private void ReadBackSettings()
    {
        lock (_io)
        {
            if (_link is null || !_link.IsOpen) return;

            // 0x13 DPI：payload[0]=(当前档<<4)|档数，之后每档 4 字节(lo,hi,lo,hi)
            var dpi = _link.Query(ReadFrame(0x13), MouseCommLink.InfoRetries);
            if (dpi is not null && dpi.Length > 5)
            {
                int cur = dpi[4] >> 4, count = dpi[4] & 0x0F;
                if (count is > 0 and <= 6)
                {
                    Settings.DpiValues.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        int o = 5 + i * 4;
                        if (o + 1 >= dpi.Length) break;
                        int reg = dpi[o] | (dpi[o + 1] << 8);
                        Settings.DpiValues.Add(DpiConversion.FromRegister(_icType, reg));
                    }
                    Settings.CurrentDpiIndex = Math.Clamp(cur, 0, Math.Max(0, Settings.DpiValues.Count - 1));
                }
            }

            // 0x12 回报率：payload[0]=档位码
            var rr = _link.Query(ReadFrame(0x12), MouseCommLink.InfoRetries);
            if (rr is not null && rr.Length > 4) Settings.ReportRateCode = rr[4];

            // 0x15 灯光：payload[0]=模式 id；payload[1]=(亮度<<4)|速度（高半字节亮度、低半字节速度）。
            // 原程序 GetMouseData@0x15：local_70[5]&0x0F=速度, local_70[5]>>4=亮度。
            // 之前只读了模式 id、漏读亮度/速度，导致 Settings.LightBrightness/Speed 恒为初始默认，
            // 应用灯光时可能带着亮度 0 下发 → 灯不亮，表现为"亮度/速度/颜色都无效"。
            var lt = _link.Query(ReadFrame(0x15), MouseCommLink.InfoRetries);
            if (lt is not null && lt.Length > 4)
            {
                Settings.LightMode = lt[4];
                _appliedLightMode = lt[4];
                if (lt.Length > 5)
                {
                    Settings.LightBrightness = lt[5] >> 4;
                    Settings.LightSpeed = lt[5] & 0x0F;
                }
            }

            // 0x16 传感器：payload[0]=抬升高度, [1]/[2]=sensor_flag 的 bit3/bit2
            var sn = _link.Query(ReadFrame(0x16), MouseCommLink.InfoRetries);
            if (sn is not null && sn.Length > 6)
            {
                Settings.LiftoffHeight = sn[4];
                Settings.SensorFlag = (sn[5] != 0 ? 0x08 : 0) | (sn[6] != 0 ? 0x04 : 0);
            }

            // 0x17 休眠：payload[0]=休眠超时, [1]=移动唤醒, [2]=移动关灯, [3]=按键响应
            var sl = _link.Query(ReadFrame(0x17), MouseCommLink.InfoRetries);
            if (sl is not null && sl.Length > 7)
            {
                Settings.SleepLight = sl[4];
                Settings.MoveWakeup = sl[5] != 0;
                Settings.MoveCloseLight = sl[6] != 0;
                Settings.ButtonRespondTime = sl[7];
            }
        }
    }

    private void MarkDisconnected()
    {
        Status.IsConnected = false;
        Status.Info = null;
    }

    private static byte[] ReadFrame(byte cmd) =>
        MouseReport.Build(cmd, new byte[] { 0x00, 0x01, 0x00 }, ReadOnlySpan<byte>.Empty);

    private Task<bool> SendAsync(byte[] report) => Task.Run(() =>
    {
        lock (_io)
        {
            if (_link is null) return false;
            var r = _link.Exchange(report);
            if (!r.WriteOk) { MarkDisconnected(); Raise(); return false; }
            return r.WriteOk;
        }
    });

    public Task<bool> ApplyDpiAsync() =>
        SendAsync(DpiCodec.EncodeStages(_icType, Settings.DpiValues, Settings.CurrentDpiIndex));

    public Task<bool> ApplyDpiColorsAsync() =>
        SendAsync(DeviceSettingsCodec.EncodeDpiColors(Settings.DpiColors));

    public Task<bool> ApplyReportRateAsync() =>
        SendAsync(DeviceSettingsCodec.EncodeReportRate(Settings.ReportRateCode));

    public async Task<bool> ApplyLightAsync()
    {
        int mode = Settings.LightMode;
        var colors = NormalizeLightColors(mode, Settings.LightColors);
        // 1:1 复刻原程序 SetLightModeData(@0x438d70)：**只发一帧**灯光报文(0x05)，不做任何额外切换。
        // 原程序改颜色/亮度/速度即走此单帧路径且在官方软件里即时生效(帧字节与我方 LightCodec 完全一致)。
        // 过去的"先关灯再切回"toggle 属非忠实的 workaround，反而引入中间"关闭"帧与时序竞态，
        // 导致亮度/速度/颜色都像没生效——现移除，严格对齐原厂行为。
        bool ok = await SendAsync(LightCodec.Encode(mode, Settings.LightBrightness, Settings.LightSpeed, colors));
        _appliedLightMode = mode;
        return ok;
    }

    public Task<bool> ApplyKeyMapAsync() =>
        SendAsync(KeyMapCodec.Encode(Settings.Keys));

    public Task<bool> ApplyKeyMapAsync(IReadOnlyList<KeyMapCodec.KeyEntry> keys) =>
        SendAsync(KeyMapCodec.Encode(keys));

    /// <summary>上传宏内容(0x08)：可能分多帧，逐帧串行发送，全部写成功才算成功。</summary>
    public Task<bool> ApplyMacroAsync(int macroSlot, IReadOnlyList<MacroCodec.MacroEvent> events) => Task.Run(() =>
    {
        lock (_io)
        {
            if (_link is null) return false;
            bool all = true;
            foreach (var frame in MacroCodec.Encode(macroSlot, events))
            {
                var r = _link.Exchange(frame);
                if (!r.WriteOk) { MarkDisconnected(); Raise(); return false; }
                all &= r.WriteOk;
            }
            return all;
        }
    });

    public Task<bool> ApplySleepAsync() =>
        SendAsync(DeviceSettingsCodec.EncodeSleep(Settings.SleepLight,
            Settings.MoveWakeup ? 1 : 0, Settings.MoveCloseLight ? 1 : 0, Settings.ButtonRespondTime));

    public Task<bool> ApplySensorAsync() =>
        SendAsync(DeviceSettingsCodec.EncodeSensorLift(Settings.LiftoffHeight, Settings.SensorFlag));

    public Task<bool> ResetDpiAsync() => SendAsync(DpiCodec.EncodeReset());

    public async Task<bool> ResetLightAsync()
    {
        bool a = await SendAsync(LightCodec.EncodeResetStep1());
        await Task.Delay(100);
        bool b = await SendAsync(LightCodec.EncodeResetStep2());
        return a && b;
    }

    public async Task<bool> ResetReportRateAsync()
    {
        bool a = await SendAsync(DeviceSettingsCodec.EncodeReportRateResetStep1());
        await Task.Delay(100);
        bool b = await SendAsync(DeviceSettingsCodec.EncodeReportRateResetStep2());
        return a && b;
    }

    /// <summary>灯光颜色列表按模式补齐：常亮需 1 色、七彩波浪需 7 色。</summary>
    private static IReadOnlyList<int> NormalizeLightColors(int mode, List<int> colors)
    {
        int need = mode switch { 2 => 7, 3 => 1, _ => 0 };
        if (need == 0) return colors;
        var list = colors.Take(need).ToList();
        while (list.Count < need) list.Add(0x00FFFFFF);
        return list;
    }

    private void Raise() => StatusChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>实机诊断：对全部读命令(0x10..0x20)逐条 dump 原始字节，并就地解码已知字段，便于对照官方软件校准。</summary>
    public Task<string> RunDiagnosticsAsync() => Task.Run(() =>
    {
      lock (_io)
      {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Inphic Mouse 实机诊断 ===");
        sb.AppendLine($"时间  : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"型号  : {Status.ModelName}    连接: {(!Status.IsConnected ? "未连接" : Status.IsOnline ? "已连接" : "无设备(休眠)")}    模式: {Status.ModeText}");
        if (Status.Info is { } li)
            sb.AppendLine($"固件  : {li.FirmwareText}    IC: 0x{li.IcType:X2}    电量: {li.BatteryPercent}%{(li.IsCharging ? "(充电中)" : "")}    在线: {li.IsOnline}");

        if (_link is null || !_link.IsOpen)
        {
            sb.AppendLine().AppendLine("未连接真实设备（演示模式无原始字节）。");
            return sb.ToString();
        }

        sb.AppendLine();
        sb.AppendLine("格式：发送 = 33 字节 hex（含校验和）；回读 = 已剥掉 report id 的原始字节。");
        foreach (var (name, cmd) in new[]
        {
            ("0x10 设备信息", (byte)0x10), ("0x11 握手", (byte)0x11), ("0x12 回报率", (byte)0x12),
            ("0x13 DPI 档位", (byte)0x13), ("0x14 DPI 颜色", (byte)0x14), ("0x15 灯光", (byte)0x15),
            ("0x16 传感器", (byte)0x16), ("0x17 休眠", (byte)0x17), ("0x20 无线固件", (byte)0x20),
        })
        {
            var frame = MouseReport.Build(cmd, new byte[] { 0x00, 0x01, 0x00 }, ReadOnlySpan<byte>.Empty);
            var r = _link.Exchange(frame, MouseCommLink.InfoRetries);
            sb.AppendLine();
            sb.AppendLine($"---- {name} ----");
            sb.AppendLine($"发送  : {Hex(frame)}");
            sb.AppendLine($"写OK  : {r.WriteOk}    ack: {r.AckOk}");
            sb.AppendLine($"回读  : {(r.Response is null ? "(无响应)" : Hex(r.Response))}");
            foreach (string line in DecodeRead(cmd, r.Response))
                sb.AppendLine("  解码  : " + line);
        }
        return sb.ToString();
      }
    });

    /// <summary>把回读帧就地解码成人类可读行（未知/无响应返回空）。</summary>
    private IEnumerable<string> DecodeRead(byte cmd, byte[]? resp)
    {
        if (resp is null || resp.Length < 5) yield break;
        switch (cmd)
        {
            case 0x10:
                var info = DeviceInfoCodec.Parse(resp);
                if (info is not null)
                    yield return $"固件={info.FirmwareText}  IC=0x{info.IcType:X2}  电量={info.BatteryPercent}%  充电={info.IsCharging}  在线={info.IsOnline}";
                break;

            case 0x12:
                yield return $"回报率档位码 = {resp[4]}（1↔1000Hz / 2↔500 / 4↔250 / 8↔125）";
                break;

            case 0x13:
            {
                int cur = resp[4] >> 4, cnt = resp[4] & 0x0F;
                yield return $"当前档 = {cur + 1}    档数 = {cnt}";
                var parts = new System.Collections.Generic.List<string>();
                for (int k = 0; k < cnt; k++)
                {
                    int o = 5 + k * 4;
                    if (o + 1 >= resp.Length) break;
                    int reg = resp[o] | (resp[o + 1] << 8);
                    parts.Add($"DPI{k + 1}={DpiConversion.FromRegister(_icType, reg)}(reg {reg})");
                }
                if (parts.Count > 0) yield return string.Join("  ", parts);
                break;
            }

            case 0x14:
            {
                var parts = new System.Collections.Generic.List<string>();
                for (int k = 0; k < 6; k++)
                {
                    int o = 4 + k * 3;
                    if (o + 2 >= resp.Length) break;
                    parts.Add($"#{resp[o + 2]:X2}{resp[o + 1]:X2}{resp[o]:X2}");
                }
                if (parts.Count > 0) yield return "各档颜色(低位起R,G,B) = " + string.Join(" ", parts);
                break;
            }

            case 0x15:
                yield return $"灯光模式 id = {resp[4]}";
                if (resp.Length > 5)
                    yield return $"亮度 = {resp[5] >> 4}    速度 = {resp[5] & 0x0F}";
                break;

            case 0x16:
                yield return $"抬升高度 = {resp[4]}    sensor_flag: bit3={resp[5]} bit2={resp[6]}";
                break;

            case 0x17:
                yield return $"休眠原始值 = {resp[4]}（≈{resp[4] / 6} 分钟）    移动唤醒 = {resp[5]}    移动关灯 = {resp[6]}    按键响应 = {resp[7]}";
                break;

            case 0x20:
                if (resp.Length > 7) yield return $"无线固件 = 0x{resp[7]:X2}{resp[6]:X2}";
                break;
        }
    }
    public Task<string> ReadLightModeAsync() => Task.Run(() =>
    {
      lock (_io)
      {
        if (_link is null || !_link.IsOpen) return "未连接真实设备。";
        var frame = MouseReport.Build(0x15, new byte[] { 0x00, 0x01, 0x00 }, ReadOnlySpan<byte>.Empty);
        var r = _link.Exchange(frame, MouseCommLink.InfoRetries);
        if (r.Response is null) return $"读不到灯光响应（写OK={r.WriteOk}）。";
        // 响应已剥 report id：[0]=cmd [1]=00 [2]=01 [3]=len [4]=模式id
        int id = r.Response.Length > 4 ? r.Response[4] : -1;
        return $"回读: {Hex(r.Response)}\n当前灯光模式 id = {id}\n（记下此刻官方软件里选的是哪种灯效）";
      }
    });

    /// <summary>读回宏内容：0x18 读不到，故**扫描多个候选读命令**(0x18/0x08/0x19/0x1A/0x11)并多种载荷，
    /// dump 所有有响应的原始字节，供校准宏读命令与事件格式。</summary>
    public Task<string> ReadMacroAsync(int macroSlot) => Task.Run(() =>
    {
      lock (_io)
      {
        if (_link is null || !_link.IsOpen) return "未连接真实设备。";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== 扫描宏读命令（宏槽 {macroSlot}）===");
        byte sel = (byte)(macroSlot + 1);

        // 候选：命令码 + 头部 len + 载荷首字节(选槽)。多组组合都试。
        var probes = new (byte cmd, byte hlen, byte[] payload, string desc)[]
        {
            (0x18, 0x01, new byte[]{ sel }, "0x18 len01 slot+1"),
            (0x18, 0x00, System.Array.Empty<byte>(), "0x18 len00 空"),
            (0x18, 0x01, new byte[]{ (byte)macroSlot }, "0x18 len01 slot"),
            (0x08, 0x01, new byte[]{ sel }, "0x08 len01 slot+1"),
            (0x19, 0x01, new byte[]{ sel }, "0x19 len01 slot+1"),
            (0x1A, 0x01, new byte[]{ sel }, "0x1A len01 slot+1"),
            (0x11, 0x01, new byte[]{ sel }, "0x11 len01 slot+1"),
        };

        foreach (var (cmd, hlen, payload, desc) in probes)
        {
            var frame = MouseReport.Build(cmd, new byte[] { 0x00, 0x01, hlen }, payload);
            var r = _link.Exchange(frame, MouseCommLink.InfoRetries);
            sb.AppendLine($"\n[{desc}] 写OK={r.WriteOk} ack={r.AckOk}");
            sb.AppendLine(r.Response is null ? "  (无响应)" : "  " + Hex(r.Response));
        }
        sb.AppendLine("\n（先用官方软件给某键录一个已知宏并应用，再点此。把本文整段发给开发者。）");
        return sb.ToString();
      }
    });

    private static string Hex(ReadOnlySpan<byte> b)
    {
        var sb = new System.Text.StringBuilder(b.Length * 3);
        foreach (var x in b) sb.Append(x.ToString("X2")).Append(' ');
        return sb.ToString().TrimEnd();
    }
}
