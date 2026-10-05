using Windows.UI;

namespace InphicMouse.App.ViewModels;

/// <summary>#RRGGBB 十六进制 ⇄ Windows.UI.Color 转换（供 ColorPicker 使用）。</summary>
public static class ColorHelpers
{
    public static Color FromHex(string? hex)
    {
        byte r = 0xFF, g = 0xFF, b = 0xFF;
        if (!string.IsNullOrWhiteSpace(hex))
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 8) hex = hex[2..];
            if (hex.Length == 6
                && byte.TryParse(hex.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte pr)
                && byte.TryParse(hex.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte pg)
                && byte.TryParse(hex.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte pb))
            {
                r = pr; g = pg; b = pb;
            }
        }
        return Color.FromArgb(0xFF, r, g, b);
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
