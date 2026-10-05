using System;
using System.IO;
using System.Text.Json;

namespace InphicMouse.App;

/// <summary>主题模式。</summary>
public enum AppThemeMode { System = 0, Light = 1, Dark = 2 }

/// <summary>应用级偏好（主题），存于程序目录 settings.json。</summary>
public static class AppPrefs
{
    private static readonly string FilePath = AppPaths.DataFile("settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>当前主题模式（默认跟随系统）。</summary>
    public static AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;

    internal sealed class Dto
    {
        public int ThemeMode { get; set; }
        // 兼容旧版本只有 LightTheme 的配置
        public bool? LightTheme { get; set; }
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var d = JsonSerializer.Deserialize<Dto>(File.ReadAllText(FilePath));
            if (d is null) return;
            ThemeMode = d.ThemeMode is >= 0 and <= 2 ? (AppThemeMode)d.ThemeMode : AppThemeMode.System;
            // 旧配置迁移：只有 LightTheme=true 时视为亮色
            if (d.ThemeMode == 0 && d.LightTheme == true) ThemeMode = AppThemeMode.Light;
        }
        catch { /* 忽略损坏文件 */ }
    }

    public static void Save()
    {
        try
        {
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(new Dto { ThemeMode = (int)ThemeMode }, Options));
        }
        catch { /* 忽略写入失败 */ }
    }
}