using System.Runtime.InteropServices;
using System.Threading;
using InphicMouse.Hid.Native;
using Microsoft.Win32.SafeHandles;

namespace InphicMouse.Hid;

/// <summary>
/// 打开的 HID 设备句柄。主通信走 Output/Input Report（对应 hidapi hid_write / hid_read_timeout），
/// 另保留 Feature 报文能力。为支持带超时的读取，句柄以 FILE_FLAG_OVERLAPPED 打开并使用重叠 I/O。
/// </summary>
public sealed class HidDeviceHandle : IHidTransport
{
    private readonly SafeFileHandle _handle;
    private readonly nint _readEvent;   // 重叠读事件（手动重置）
    private readonly nint _writeEvent;  // 重叠写事件（手动重置）
    private readonly bool _overlapped;  // 是否成功以读写+重叠方式打开
    private bool _disposed;

    public HidDeviceInfo Info { get; }
    public bool IsOpen => !_handle.IsInvalid && !_handle.IsClosed && !_disposed;

    private HidDeviceHandle(SafeFileHandle handle, HidDeviceInfo info, bool overlapped)
    {
        _handle = handle;
        Info = info;
        _overlapped = overlapped;
        if (overlapped)
        {
            _readEvent = NativeMethods.CreateEventW(0, true, false, null);
            _writeEvent = NativeMethods.CreateEventW(0, true, false, null);
        }
    }

    /// <summary>
    /// 打开设备。优先以 GENERIC_READ|GENERIC_WRITE + 重叠方式打开（支持 Output/Input Report）；
    /// 若被系统对鼠标 collection 的独占拒绝，则回退到 0 访问权限 + 共享读写（仅支持 Feature 报文）。
    /// </summary>
    public static HidDeviceHandle? Open(HidDeviceInfo info)
    {
        SafeFileHandle h = HidDeviceEnumerator.OpenReadWrite(info.Path);
        if (!h.IsInvalid)
            return new HidDeviceHandle(h, info, overlapped: true);
        h.Dispose();

        h = HidDeviceEnumerator.OpenShared(info.Path);
        if (h.IsInvalid) { h.Dispose(); return null; }
        return new HidDeviceHandle(h, info, overlapped: false);
    }

    /// <summary>发送 Output Report（buf[0]=Report ID）。对应 hidapi hid_write。</summary>
    public bool Write(byte[] report)
    {
        if (report is null || report.Length == 0 || !IsOpen) return false;
        if (!_overlapped)
            return NativeMethods.WriteFile(_handle, report, (uint)report.Length, out _, 0);

        var ov = new NativeOverlapped { EventHandle = _writeEvent };
        return WithOverlapped(ref ov, _writeEvent, ovPtr =>
        {
            if (NativeMethods.WriteFile(_handle, report, (uint)report.Length, out uint written, ovPtr))
                return (int)written;
            if (Marshal.GetLastWin32Error() != NativeMethods.ERROR_IO_PENDING)
                return -1;
            if (NativeMethods.WaitForSingleObject(_writeEvent, NativeMethods.INFINITE) != NativeMethods.WAIT_OBJECT_0)
                return -1;
            return NativeMethods.GetOverlappedResult(_handle, ovPtr, out uint done, false) ? (int)done : -1;
        }) > 0;
    }

    /// <summary>读取 Input Report，带超时(ms)。返回读到字节数，超时/失败返回 &lt;=0。对应 hid_read_timeout。</summary>
    public int Read(byte[] buffer, int timeoutMs)
    {
        if (buffer is null || buffer.Length == 0 || !IsOpen) return -1;
        if (!_overlapped)
            return NativeMethods.ReadFile(_handle, buffer, (uint)buffer.Length, out uint syncRead, 0) ? (int)syncRead : -1;

        var ov = new NativeOverlapped { EventHandle = _readEvent };
        return WithOverlapped(ref ov, _readEvent, ovPtr =>
        {
            if (NativeMethods.ReadFile(_handle, buffer, (uint)buffer.Length, out uint read, ovPtr))
                return (int)read;
            if (Marshal.GetLastWin32Error() != NativeMethods.ERROR_IO_PENDING)
                return -1;

            uint wait = NativeMethods.WaitForSingleObject(_readEvent, timeoutMs < 0 ? NativeMethods.INFINITE : (uint)timeoutMs);
            if (wait == NativeMethods.WAIT_TIMEOUT)
            {
                NativeMethods.CancelIoEx(_handle, ovPtr);   // 超时：取消挂起的读取
                NativeMethods.GetOverlappedResult(_handle, ovPtr, out _, true);
                return 0;
            }
            if (wait != NativeMethods.WAIT_OBJECT_0) return -1;
            return NativeMethods.GetOverlappedResult(_handle, ovPtr, out uint done, false) ? (int)done : -1;
        });
    }

    private static int WithOverlapped(ref NativeOverlapped ov, nint evt, Func<nint, int> io)
    {
        nint ovPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
        try
        {
            Marshal.StructureToPtr(ov, ovPtr, false);
            return io(ovPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(ovPtr);
        }
    }

    public bool SetFeature(byte[] report) =>
        IsOpen && NativeMethods.HidD_SetFeature(_handle, report, report.Length);

    public bool GetFeature(byte[] report) =>
        IsOpen && NativeMethods.HidD_GetFeature(_handle, report, report.Length);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_readEvent != 0) NativeMethods.CloseHandle(_readEvent);
        if (_writeEvent != 0) NativeMethods.CloseHandle(_writeEvent);
        _handle.Dispose();
    }
}
