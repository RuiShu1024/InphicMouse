namespace InphicMouse.Hid;

/// <summary>HID 收发抽象。原程序主通信走 Output/Input Report(hidapi hid_write/hid_read)，另保留 Feature 报文。</summary>
public interface IHidTransport : IDisposable
{
    HidDeviceInfo Info { get; }
    bool IsOpen { get; }

    /// <summary>发送 Output Report。report[0]=Report ID(无则0)。对应 hidapi hid_write。</summary>
    bool Write(byte[] report);

    /// <summary>读取 Input Report，带超时(ms)。返回读到的字节数，超时/失败返回 &lt;=0。对应 hid_read_timeout。</summary>
    int Read(byte[] buffer, int timeoutMs);

    bool SetFeature(byte[] report);
    bool GetFeature(byte[] report);
}
