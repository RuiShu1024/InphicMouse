using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace InphicMouse.Core.Metadata;

internal static partial class XmlUtil
{
    /// <summary>容错加载：原厂 xml 的属性值可能含未转义的裸 '&'（如 hid_interface），先按标准解析，失败则修复后重试。</summary>
    public static XDocument LoadLenient(string path)
    {
        string text = File.ReadAllText(path);
        try
        {
            return XDocument.Parse(text);
        }
        catch (System.Xml.XmlException)
        {
            string repaired = BareAmpersand().Replace(text, "&amp;");
            return XDocument.Parse(repaired);
        }
    }

    public static int ParseInt(string? s) => int.TryParse(s, out int v) ? v : 0;

    [GeneratedRegex("&(?!(amp|lt|gt|quot|apos|#[0-9]+|#x[0-9a-fA-F]+);)")]
    private static partial Regex BareAmpersand();
}
