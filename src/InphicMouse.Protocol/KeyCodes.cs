namespace InphicMouse.Protocol;

/// <summary>
/// 按键码映射，1:1 复刻 <c>ProtocolData::GetSysKeyCode</c>(@0x4551e0)。
/// 把 Windows VK / HID usage 中的修饰键归一到硬件修饰位掩码；其余码原样返回
/// （视作已是 HID usage）。0 → 0。
/// </summary>
public static class KeyCodes
{
    /// <summary>修饰键 → 硬件修饰位；非修饰键原样返回。</summary>
    public static byte GetSysKeyCode(int usage) => usage switch
    {
        0x00 => 0x00,
        0x5B or 0xE3 => 0x08,   // LGui
        0x5C or 0xE7 => 0x80,   // RGui
        0xA0 or 0xE1 => 0x02,   // LShift
        0xA1 or 0xE5 => 0x20,   // RShift
        0xA2 or 0xE0 => 0x01,   // LCtrl
        0xA3 or 0xE4 => 0x10,   // RCtrl
        0xA4 or 0xE2 => 0x04,   // LAlt
        0xA5 or 0xE6 => 0x40,   // RAlt
        _ => (byte)(usage & 0xFF),
    };

    /// <summary>usage 是否为独立修饰键（HID 0xE0..0xE7）。</summary>
    public static bool IsModifierUsage(int usage) => usage is >= 0xE0 and <= 0xE7;

    /// <summary>
    /// Windows 虚拟键码(VK) → HID 键盘 Usage，1:1 复刻 <c>ProtocolData::GetFlashKeyCode</c>(@0x454bb0)。
    /// 用于键盘快捷键捕获：把用户按下的主键转成硬件 usage。未覆盖的 VK 返回 0。
    /// </summary>
    public static byte VkToHidUsage(int vk)
    {
        // 字母 A..Z: VK 0x41..0x5A → 0x04..0x1D
        if (vk is >= 0x41 and <= 0x5A) return (byte)(vk - 0x41 + 0x04);
        // 主键盘数字 1..9: VK 0x31..0x39 → 0x1E..0x26；0: VK 0x30 → 0x27
        if (vk is >= 0x31 and <= 0x39) return (byte)(vk - 0x31 + 0x1E);
        if (vk == 0x30) return 0x27;
        // 功能键 F1..F12: VK 0x70..0x7B → 0x3A..0x45
        if (vk is >= 0x70 and <= 0x7B) return (byte)(vk - 0x70 + 0x3A);
        // 小键盘数字 0..9: VK 0x60..0x69 → 0x62..0x63.. (Keypad 1..0 = 0x59..0x62)
        if (vk is >= 0x61 and <= 0x69) return (byte)(vk - 0x61 + 0x59); // Numpad1..9
        if (vk == 0x60) return 0x62;                                    // Numpad0
        return vk switch
        {
            0x0D => 0x28, // Enter
            0x1B => 0x29, // Esc
            0x08 => 0x2A, // Backspace
            0x09 => 0x2B, // Tab
            0x20 => 0x2C, // Space
            0xBD => 0x2D, // - _
            0xBB => 0x2E, // = +
            0xDB => 0x2F, // [ {
            0xDD => 0x30, // ] }
            0xDC => 0x31, // \ |
            0xBA => 0x33, // ; :
            0xDE => 0x34, // ' "
            0xC0 => 0x35, // ` ~
            0xBC => 0x36, // , <
            0xBE => 0x37, // . >
            0xBF => 0x38, // / ?
            0x14 => 0x39, // CapsLock
            0x2C => 0x46, // PrintScreen
            0x91 => 0x47, // ScrollLock
            0x13 => 0x48, // Pause
            0x2D => 0x49, // Insert
            0x24 => 0x4A, // Home
            0x21 => 0x4B, // PageUp
            0x2E => 0x4C, // Delete
            0x23 => 0x4D, // End
            0x22 => 0x4E, // PageDown
            0x27 => 0x4F, // Right
            0x25 => 0x50, // Left
            0x28 => 0x51, // Down
            0x26 => 0x52, // Up
            _ => 0x00,
        };
    }
}
