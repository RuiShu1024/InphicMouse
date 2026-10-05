using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace InphicMouse.Hid.Native;

internal static class NativeMethods
{
    // ---------------- hid.dll ----------------
    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_FreePreparsedData(nint preparsedData);

    // 返回 NTSTATUS，成功为 HIDP_STATUS_SUCCESS (0x00110000)
    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(nint preparsedData, ref HIDP_CAPS caps);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetProductString(SafeFileHandle handle, byte[] buffer, int bufferLength);

    // ---------------- setupapi.dll ----------------
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, nint hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, nint deviceInfoData,
        ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(nint deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, nint detailData, int detailDataSize,
        out int requiredSize, nint deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern int SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    // ---------------- kernel32.dll ----------------
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool WriteFile(SafeFileHandle handle, byte[] buffer, uint toWrite, out uint written, nint overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool ReadFile(SafeFileHandle handle, byte[] buffer, uint toRead, out uint read, nint overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool CancelIoEx(SafeFileHandle handle, nint overlapped);

    // 重叠 I/O 用于给 ReadFile 加超时（对应 hidapi hid_read_timeout）。
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint CreateEventW(nint attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool GetOverlappedResult(SafeFileHandle handle, nint overlapped,
        out uint bytesTransferred, [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool CloseHandle(nint handle);

    public const int HIDP_STATUS_SUCCESS = 0x00110000;
    public const int ERROR_IO_PENDING = 997;
    public const uint WAIT_OBJECT_0 = 0x00000000;
    public const uint WAIT_TIMEOUT = 0x00000102;
    public const uint INFINITE = 0xFFFFFFFF;
}
