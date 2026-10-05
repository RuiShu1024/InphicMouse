using System.Runtime.InteropServices;
using System.Text;
using InphicMouse.Hid.Native;
using Microsoft.Win32.SafeHandles;

namespace InphicMouse.Hid;

/// <summary>基于 SetupAPI + hid.dll 枚举系统中所有 HID 设备接口，与原程序 DriverComm::EnumerateDevice 一致。</summary>
public static class HidDeviceEnumerator
{
    private static readonly nint InvalidHandle = -1;

    public static IReadOnlyList<HidDeviceInfo> Enumerate(Func<HidDeviceInfo, bool>? filter = null)
    {
        var result = new List<HidDeviceInfo>();
        NativeMethods.HidD_GetHidGuid(out var hidGuid);
        nint devInfo = NativeMethods.SetupDiGetClassDevs(ref hidGuid, null, 0,
            Win32.DIGCF_PRESENT | Win32.DIGCF_DEVICEINTERFACE);
        if (devInfo == InvalidHandle || devInfo == 0) return result;

        try
        {
            var ifaceData = new SP_DEVICE_INTERFACE_DATA
            {
                cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
            };
            int index = 0;
            while (NativeMethods.SetupDiEnumDeviceInterfaces(devInfo, 0, ref hidGuid, index, ref ifaceData))
            {
                index++;
                string? path = GetDevicePath(devInfo, ref ifaceData);
                if (path is null) continue;
                var info = Probe(path);
                if (info is null) continue;
                if (filter is null || filter(info)) result.Add(info);
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(devInfo);
        }
        return result;
    }

    /// <summary>只取设备接口路径（不 CreateFile/不 Probe），用于快速的存在性轮询检测插拔。</summary>
    public static IReadOnlyList<string> EnumerateInterfacePaths()
    {
        var paths = new List<string>();
        NativeMethods.HidD_GetHidGuid(out var hidGuid);
        nint devInfo = NativeMethods.SetupDiGetClassDevs(ref hidGuid, null, 0,
            Win32.DIGCF_PRESENT | Win32.DIGCF_DEVICEINTERFACE);
        if (devInfo == InvalidHandle || devInfo == 0) return paths;
        try
        {
            var ifaceData = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            int index = 0;
            while (NativeMethods.SetupDiEnumDeviceInterfaces(devInfo, 0, ref hidGuid, index, ref ifaceData))
            {
                index++;
                string? path = GetDevicePath(devInfo, ref ifaceData);
                if (path is not null) paths.Add(path);
            }
        }
        finally { NativeMethods.SetupDiDestroyDeviceInfoList(devInfo); }
        return paths;
    }

    private static string? GetDevicePath(nint devInfo, ref SP_DEVICE_INTERFACE_DATA ifaceData)
    {
        NativeMethods.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifaceData, 0, 0, out int required, 0);
        if (required <= 0) return null;
        nint buffer = Marshal.AllocHGlobal(required);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: x64=8, x86=6；DevicePath 起始于 offset 4。
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifaceData, buffer, required, out _, 0))
                return null;
            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static HidDeviceInfo? Probe(string path)
    {
        SafeFileHandle handle = OpenShared(path);
        if (handle.IsInvalid) { handle.Dispose(); return null; }
        try
        {
            var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            if (!NativeMethods.HidD_GetAttributes(handle, ref attr)) return null;

            ushort usage = 0, usagePage = 0, feat = 0, inp = 0, outp = 0;
            if (NativeMethods.HidD_GetPreparsedData(handle, out nint pp) && pp != 0)
            {
                try
                {
                    var caps = new HIDP_CAPS { Reserved = new ushort[17] };
                    if (NativeMethods.HidP_GetCaps(pp, ref caps) == NativeMethods.HIDP_STATUS_SUCCESS)
                    {
                        usage = caps.Usage; usagePage = caps.UsagePage;
                        feat = caps.FeatureReportByteLength;
                        inp = caps.InputReportByteLength; outp = caps.OutputReportByteLength;
                    }
                }
                finally { NativeMethods.HidD_FreePreparsedData(pp); }
            }

            return new HidDeviceInfo
            {
                Path = path,
                VendorId = attr.VendorID,
                ProductId = attr.ProductID,
                UsagePage = usagePage,
                Usage = usage,
                FeatureReportByteLength = feat,
                InputReportByteLength = inp,
                OutputReportByteLength = outp,
                ProductString = GetProductString(handle),
            };
        }
        finally
        {
            handle.Dispose();
        }
    }

    private static string? GetProductString(SafeFileHandle h)
    {
        var buf = new byte[256];
        if (NativeMethods.HidD_GetProductString(h, buf, buf.Length))
        {
            string s = Encoding.Unicode.GetString(buf).TrimEnd('\0');
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        return null;
    }

    /// <summary>以 0 访问权限 + 共享读写打开设备句柄，可执行 Feature 报文而不与系统对鼠标 collection 的独占冲突。</summary>
    internal static SafeFileHandle OpenShared(string path) =>
        NativeMethods.CreateFile(path, 0,
            Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE, 0, Win32.OPEN_EXISTING, 0, 0);

    /// <summary>以读写权限 + 共享读写 + 重叠 I/O 打开，支持 Output/Input Report（hidapi 的默认方式）。</summary>
    internal static SafeFileHandle OpenReadWrite(string path) =>
        NativeMethods.CreateFile(path, Win32.GENERIC_READ | Win32.GENERIC_WRITE,
            Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE, 0, Win32.OPEN_EXISTING,
            Win32.FILE_FLAG_OVERLAPPED, 0);
}
