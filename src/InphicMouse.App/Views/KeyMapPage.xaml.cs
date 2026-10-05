using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using InphicMouse.App.ViewModels;
using InphicMouse.Protocol;

namespace InphicMouse.App.Views;

public sealed partial class KeyMapPage : Page
{
    public KeyMapViewModel Vm { get; } = App.GetService<KeyMapViewModel>();
    public KeyMapPage() => InitializeComponent();

    /// <summary>键盘快捷键捕获：按下组合键时记录修饰位 + 主键 HID usage，写回对应 KeyEditItem。</summary>
    private void Capture_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not KeyEditItem item) return;

        var key = e.Key;
        // 纯修饰键单独按下时不作为主键，等待主键
        if (key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu
            or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            e.Handled = true;
            return;
        }

        byte mask = 0;
        if (IsDown(VirtualKey.LeftControl)) mask |= 0x01;
        if (IsDown(VirtualKey.RightControl)) mask |= 0x10;
        if (IsDown(VirtualKey.LeftShift)) mask |= 0x02;
        if (IsDown(VirtualKey.RightShift)) mask |= 0x20;
        if (IsDown(VirtualKey.LeftMenu)) mask |= 0x04;   // LAlt
        if (IsDown(VirtualKey.RightMenu)) mask |= 0x40;  // RAlt
        if (IsDown(VirtualKey.LeftWindows)) mask |= 0x08;
        if (IsDown(VirtualKey.RightWindows)) mask |= 0x80;
        // 泛修饰(未区分左右)兜底
        if (mask == 0 || (mask & 0x11) == 0)
            if (IsDown(VirtualKey.Control)) mask |= 0x01;
        if ((mask & 0x22) == 0)
            if (IsDown(VirtualKey.Shift)) mask |= 0x02;
        if ((mask & 0x44) == 0)
            if (IsDown(VirtualKey.Menu)) mask |= 0x04;

        byte usage = KeyCodes.VkToHidUsage((int)key);
        if (usage != 0)
            item.SetCapture(mask, usage);

        e.Handled = true;
    }

    private static bool IsDown(VirtualKey k) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(k) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

    /// <summary>应用前检查：若没有任何按键保留"鼠标左键"，强制弹窗提醒后果，确认后才下发。</summary>
    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!Vm.HasLeftClick)
        {
            var dlg = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "未设置鼠标左键",
                Content = "当前没有任何按键被设为「鼠标左键」。应用后将无法用这只鼠标进行正常的左键单击"
                        + "（选择文件、点击按钮、确认对话框都会失效），只能用其他设备或改回设置来恢复。\n\n确定仍要应用吗？",
                PrimaryButtonText = "仍要应用",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            var result = await dlg.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
        }
        await Vm.ApplyCommand.ExecuteAsync(null);
    }
}
