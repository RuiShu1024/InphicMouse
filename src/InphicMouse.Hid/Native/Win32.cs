namespace InphicMouse.Hid.Native;

internal static class Win32
{
    // SetupDiGetClassDevs flags
    public const int DIGCF_PRESENT = 0x02;
    public const int DIGCF_DEVICEINTERFACE = 0x10;

    // CreateFile
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x01;
    public const uint FILE_SHARE_WRITE = 0x02;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
}
