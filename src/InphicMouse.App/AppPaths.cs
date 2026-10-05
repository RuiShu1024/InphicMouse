using System;
using System.IO;

namespace InphicMouse.App;

/// <summary>
/// 数据文件位置解析：
/// 优先放在程序目录（便携版：数据跟着程序走）；
/// 若程序目录不可写（例如装到 Program Files），回退到 %APPDATA%\InphicMouse。
/// </summary>
public static class AppPaths
{
    private static string? _dir;

    public static string DataFile(string fileName) => Path.Combine(DataDir, fileName);

    public static string DataDir => _dir ??= ResolveDir();

    /// <summary>%APPDATA%\InphicMouse（诊断导出等辅助文件固定放这里，避免污染安装目录）</summary>
    public static string RoamingDir
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InphicMouse");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string ResolveDir()
    {
        string baseDir = AppContext.BaseDirectory;
        if (IsWritable(baseDir)) return baseDir;

        return RoamingDir;
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, ".write_probe_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }
}