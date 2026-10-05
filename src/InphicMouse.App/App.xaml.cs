using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using InphicMouse.Core.Metadata;
using InphicMouse.Protocol.Services;
using InphicMouse.App.ViewModels;

namespace InphicMouse.App;

public partial class App : Application
{
    private Window? _window;

    /// <summary>主窗口（供主题切换使用）。</summary>
    public static Window? MainWindow { get; private set; }

    /// <summary>主窗口句柄（供 FileOpenPicker/FileSavePicker 在非打包应用里初始化用）。</summary>
    public static IntPtr MainWindowHandle { get; private set; }

    /// <summary>全局服务容器。</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = BuildServices();
        AppPrefs.Load();
        _window = new MainWindow();
        MainWindow = _window;
        MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        ApplyTheme();
        SetWindowIcon(_window);
        _window.Activate();
    }

    /// <summary>设置窗口/任务栏图标（非打包 WinUI 需显式指定，否则标题栏可能是默认图标）。</summary>
    private static void SetWindowIcon(Window window)
    {
        try
        {
            string ico = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "InphicMouse.ico");
            if (System.IO.File.Exists(ico)) window.AppWindow?.SetIcon(ico);
        }
        catch { /* 图标设置失败不影响运行 */ }
    }

    /// <summary>应用主题（跟随系统/亮色/暗色）：作用于窗口内容树 + 标题栏，即时生效。</summary>
    public static void ApplyTheme()
    {
        bool dark = AppPrefs.ThemeMode switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => Current.RequestedTheme == ApplicationTheme.Dark,
        };

        if (MainWindow?.Content is FrameworkElement root)
            root.RequestedTheme = AppPrefs.ThemeMode switch
            {
                AppThemeMode.Light => ElementTheme.Light,
                AppThemeMode.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

        ApplyTitleBar(MainWindow, dark);
    }

    /// <summary>让系统标题栏跟随应用主题（强制亮/暗时避免标题栏与内容不一致）。</summary>
    private static void ApplyTitleBar(Window? window, bool dark)
    {
        if (window is null) return;
        var tb = window.AppWindow?.TitleBar;
        if (tb is null) return;

        if (AppPrefs.ThemeMode == AppThemeMode.System)
        {
            // 跟随系统：交还给系统默认配色
            tb.BackgroundColor = null; tb.ForegroundColor = null;
            tb.ButtonBackgroundColor = null; tb.ButtonForegroundColor = null;
            tb.ButtonHoverBackgroundColor = null; tb.ButtonHoverForegroundColor = null;
            tb.ButtonPressedBackgroundColor = null; tb.ButtonPressedForegroundColor = null;
            tb.ButtonInactiveBackgroundColor = null; tb.ButtonInactiveForegroundColor = null;
            return;
        }

        Windows.UI.Color bg = dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
                                   : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        Windows.UI.Color fg = dark ? Microsoft.UI.Colors.White
                                   : Windows.UI.Color.FromArgb(255, 0, 0, 0);
        Windows.UI.Color hover = dark ? Windows.UI.Color.FromArgb(255, 58, 58, 58)
                                      : Windows.UI.Color.FromArgb(255, 229, 229, 229);
        Windows.UI.Color pressed = dark ? Windows.UI.Color.FromArgb(255, 80, 80, 80)
                                        : Windows.UI.Color.FromArgb(255, 214, 214, 214);
        var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);

        tb.BackgroundColor = bg; tb.ForegroundColor = fg;
        tb.ButtonBackgroundColor = transparent; tb.ButtonForegroundColor = fg;
        tb.ButtonHoverBackgroundColor = hover; tb.ButtonHoverForegroundColor = fg;
        tb.ButtonPressedBackgroundColor = pressed; tb.ButtonPressedForegroundColor = fg;
        tb.ButtonInactiveBackgroundColor = transparent; tb.ButtonInactiveForegroundColor = fg;
    }

    private static IServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();

        // 元数据来源（自带 Metadata\ 或原程序安装目录，兜底内置默认）
        var provider = new MetadataProvider();
        var models = provider.LoadModels();
        sc.AddSingleton(provider);

        // 始终用真实设备服务：内部有插拔监视器(约1s轮询)，插上接收器即自动连接、拔出即断开。
        // 未连接时用首个型号默认能力渲染界面。
        IMouseService svc = new RealMouseService(models, provider.ResolveCapabilities);
        sc.AddSingleton(svc);

        // 宏本地存储（单例，宏编辑页与按键页共享）
        sc.AddSingleton<MacroStore>();

        // ViewModels
        sc.AddSingleton<MainViewModel>();
        sc.AddTransient<InfoViewModel>();
        sc.AddTransient<DpiViewModel>();
        sc.AddTransient<ReportRateViewModel>();
        sc.AddTransient<LightViewModel>();
        sc.AddTransient<KeyMapViewModel>();
        sc.AddTransient<MacroViewModel>();
        sc.AddTransient<SettingsViewModel>();

        return sc.BuildServiceProvider();
    }

    public static T GetService<T>() where T : class =>
        Services.GetRequiredService<T>();
}
