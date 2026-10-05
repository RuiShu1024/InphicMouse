using InphicMouse.Hid;

namespace InphicMouse.Protocol.Comm;

/// <summary>一次发送的结果：写是否成功、是否收到 ack、以及 ack 响应缓冲。</summary>
public readonly record struct SendResult(bool WriteOk, bool AckOk, byte[]? Response)
{
    /// <summary>写成功即视为"已下发到鼠标"（Output Report 已送达）。</summary>
    public bool Sent => WriteOk;
}

/// <summary>
/// 通信链路，1:1 复刻 <c>DriverComm::SetDeviceData</c>(@0x4528e0) / <c>GetDeviceInfo</c>(@0x452790)
/// 的写+ack+读回语义：发送 33 字节 Output Report → 循环读 Input Report，响应 <c>resp[1]==0</c> 视为 ack，
/// 最多重试 50 次。所有报文经 <see cref="MouseReport"/> 组装（含校验和）。
/// 注意：原程序经 hidapi 读取会剥掉 report id 前缀；本实现用原始 ReadFile，首字节可能是 report id(0)，
/// 故 ack/解析同时兼容 +0 与 +1 偏移（详见 <see cref="FindAck"/>）。
/// </summary>
public sealed class MouseCommLink : IDisposable
{
    // 原程序 ack 上限 50/49 次×100ms＝设备离线(休眠)时每次读要等 ~5s，导致 UI 卡死。
    // 实机确认在线时 ack 立即返回(第一读即中)，故降到 12 次(~1.2s 上限)：在线依旧秒回，离线快速失败。
    public const int SendRetries = 12;
    public const int InfoRetries = 12;
    private const int ReadTimeoutMs = 100;

    private readonly IHidTransport _transport;

    public MouseCommLink(IHidTransport transport) => _transport = transport;

    public bool IsOpen => _transport.IsOpen;

    private int ReadLen => _transport.Info.InputReportByteLength > 0
        ? _transport.Info.InputReportByteLength
        : 33;

    /// <summary>发送一帧并尝试收 ack。返回写/ack 状态与响应（已剥掉首字节 report id，与原程序 hidapi 空间一致）。
    /// 即使未匹配到 ack，也把最后一次读到的响应放入 Response，供上层尽力解析。</summary>
    public SendResult Exchange(byte[] report, int maxRetries = SendRetries)
    {
        if (!_transport.Write(report)) return new SendResult(false, false, null);
        var buf = new byte[Math.Max(ReadLen, 34)];
        byte[]? last = null;
        for (int i = 0; i < maxRetries; i++)
        {
            Array.Clear(buf);
            int n = _transport.Read(buf, ReadTimeoutMs);
            if (n <= 1) continue;
            // Windows 原始 ReadFile 的首字节是 report id(0)，原程序经 hidapi 已剥掉；
            // 这里同样剥掉，令后续解析与逆向文档(dev_info/命令回读)偏移一致。
            var resp = new byte[n - 1];
            Array.Copy(buf, 1, resp, 0, n - 1);
            last = resp;
            if (resp.Length > 1 && resp[1] == 0)   // 原程序判定 resp[1]==0 为 ack
                return new SendResult(true, true, resp);
        }
        return new SendResult(true, false, last); // 写成功但未收到 ack（读通道可能受限）
    }

    /// <summary>仅关心是否已下发（写成功）。UI 的"应用"用此判定，避免读通道问题造成误报失败。</summary>
    public bool Send(byte[] report, int maxRetries = SendRetries) =>
        Exchange(report, maxRetries).Sent;

    /// <summary>发送读命令并返回收到 ack 的响应缓冲（未收到返回 null）。</summary>
    public byte[]? Query(byte[] request, int maxRetries = SendRetries)
    {
        var r = Exchange(request, maxRetries);
        return r.AckOk ? r.Response : null;
    }

    public void Dispose() => _transport.Dispose();
}
