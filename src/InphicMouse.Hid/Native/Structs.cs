using System.Runtime.InteropServices;

namespace InphicMouse.Hid.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct SP_DEVICE_INTERFACE_DATA
{
    public int cbSize;
    public Guid InterfaceClassGuid;
    public int Flags;
    public nint Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HIDD_ATTRIBUTES
{
    public int Size;
    public ushort VendorID;
    public ushort ProductID;
    public ushort VersionNumber;
}

// 完整映射原生 HIDP_CAPS，尺寸必须一致，否则 HidP_GetCaps 会破坏栈。
[StructLayout(LayoutKind.Sequential)]
internal struct HIDP_CAPS
{
    public ushort Usage;
    public ushort UsagePage;
    public ushort InputReportByteLength;
    public ushort OutputReportByteLength;
    public ushort FeatureReportByteLength;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
    public ushort[] Reserved;

    public ushort NumberLinkCollectionNodes;
    public ushort NumberInputButtonCaps;
    public ushort NumberInputValueCaps;
    public ushort NumberInputDataIndices;
    public ushort NumberOutputButtonCaps;
    public ushort NumberOutputValueCaps;
    public ushort NumberOutputDataIndices;
    public ushort NumberFeatureButtonCaps;
    public ushort NumberFeatureValueCaps;
    public ushort NumberFeatureDataIndices;
}
