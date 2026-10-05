using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using InphicMouse.App.ViewModels;
using InphicMouse.Protocol;
using InphicMouse.Protocol.Codecs;

namespace InphicMouse.App.Views;

public sealed partial class MacroPage : Page
{
    public MacroViewModel Vm { get; } = App.GetService<MacroViewModel>();

    // "键盘事件"按钮按下后，等待捕获下一个按键插入（一次性）。
    private bool _awaitInsertKb;

    public MacroPage()
    {
        InitializeComponent();
        HookLists();
    }

    // ---------------- 录制捕获 ----------------
    private void Capture_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // 忽略纯修饰键
        if (IsModifier(e.Key)) { e.Handled = true; return; }
        int vk = (int)e.Key;   // 官方 DB value 存 Windows VK（如 A=65），直接用 VK
        if (vk != 0)
        {
            if (_awaitInsertKb)
            {
                _awaitInsertKb = false;
                InsertKbBtn.Content = "键盘事件";
                Vm.InsertKeyboard(vk);
            }
            else if (Vm.IsRecording)
            {
                Vm.Record(MacroCodec.EvType.KeyDown, vk);
                Vm.Record(MacroCodec.EvType.KeyUp, vk);
            }
        }
        e.Handled = true;
    }

    private void Capture_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Capture.Focus(FocusState.Programmatic);
        if (!Vm.IsRecording) return;
        var (bit, down) = Button(e);
        if (bit != 0 && down) Vm.Record(MacroCodec.EvType.MouseDown, bit);
    }

    private void Capture_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!Vm.IsRecording) return;
        var (bit, _) = Button(e);
        if (bit != 0) Vm.Record(MacroCodec.EvType.MouseUp, bit);
    }

    private void InsertKb_Click(object sender, RoutedEventArgs e)
    {
        _awaitInsertKb = true;
        InsertKbBtn.Content = "请按一个键…";
        Capture.Focus(FocusState.Programmatic);
    }

    private static bool IsModifier(VirtualKey k) =>
        k is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu
          or VirtualKey.LeftControl or VirtualKey.RightControl
          or VirtualKey.LeftShift or VirtualKey.RightShift
          or VirtualKey.LeftMenu or VirtualKey.RightMenu
          or VirtualKey.LeftWindows or VirtualKey.RightWindows;

    // ---------------- 双击：重命名宏 / 编辑事件 ----------------
    private async void MacroList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement src && src.DataContext is MacroDef m) Vm.Current = m;
        if (Vm.Current is null) return;
        var box = new TextBox { Text = Vm.Current.Name, SelectionStart = Vm.Current.Name.Length };
        var dlg = MakeDialog("重命名快捷指令", box);
        if (await dlg.ShowAsync() == ContentDialogResult.Primary) Vm.RenameCurrent(box.Text);
    }

    private async void EventList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Vm.Current is null) return;
        int idx = e.OriginalSource is FrameworkElement fe && fe.DataContext is MacroEventRow row
            ? row.Index
            : Vm.SelectedEventIndex;
        if (idx < 0 || idx >= Vm.Current.Events.Count) return;
        await EditEventAsync(idx);
    }

    private async Task EditEventAsync(int index)
    {
        if (Vm.Current is null || index < 0 || index >= Vm.Current.Events.Count) return;
        var ev = Vm.Current.Events[index].Event;

        switch (ev.Type)
        {
            case MacroCodec.EvType.Delay:
            {
                var nb = new NumberBox
                {
                    Value = ev.Value, Minimum = 0, Maximum = 65535, SmallChange = 10,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 220,
                };
                var dlg = MakeDialog("编辑时间延迟", nb);
                if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                {
                    int ms = double.IsFinite(nb.Value) ? (int)Math.Clamp(nb.Value, 0, 65535) : ev.Value;
                    Vm.ReplaceEvent(index, MacroCodec.MacroEvent.Delay(ms));
                }
                break;
            }

            case MacroCodec.EvType.MouseDown:
            case MacroCodec.EvType.MouseUp:
            {
                var cb = new ComboBox
                {
                    ItemsSource = new[] { "左键", "中键", "右键" }, Width = 180,
                    SelectedIndex = ev.Value switch { 1 => 0, 4 => 1, 2 => 2, _ => 0 },
                };
                var dlg = MakeDialog("编辑鼠标事件", cb);
                if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                {
                    int bit = cb.SelectedIndex switch { 0 => 0x01, 1 => 0x04, 2 => 0x02, _ => ev.Value };
                    Vm.ReplaceEvent(index, ev.Type == MacroCodec.EvType.MouseDown
                        ? MacroCodec.MacroEvent.MouseDown(bit)
                        : MacroCodec.MacroEvent.MouseUp(bit));
                }
                break;
            }

            default: // 键盘按下 / 松开
            {
                int vk = 0;
                var box = new TextBox
                {
                    Text = "点此后按一个新键…", IsReadOnly = true, TextAlignment = TextAlignment.Center, Width = 240,
                };
                box.Loaded += (_, _) => box.Focus(FocusState.Programmatic);
                box.PreviewKeyDown += (_, ke) =>
                {
                    if (IsModifier(ke.Key)) { ke.Handled = true; return; }
                    vk = (int)ke.Key;
                    box.Text = MacroEventRow.VkName(vk);
                    ke.Handled = true;
                };
                var dlg = MakeDialog(ev.Type == MacroCodec.EvType.KeyDown ? "编辑按键（按下）" : "编辑按键（松开）", box);
                if (await dlg.ShowAsync() == ContentDialogResult.Primary && vk != 0)
                    Vm.ReplaceEvent(index, ev.Type == MacroCodec.EvType.KeyDown
                        ? MacroCodec.MacroEvent.KeyDown(vk)
                        : MacroCodec.MacroEvent.KeyUp(vk));
                break;
            }
        }
    }

    private ContentDialog MakeDialog(string title, object content) => new()
    {
        XamlRoot = XamlRoot,
        Title = title,
        Content = content,
        PrimaryButtonText = "保存",
        CloseButtonText = "取消",
        DefaultButton = ContentDialogButton.Primary,
    };

    // ---------------- 导出 / 导入（单个宏）----------------
    private async void ExportMacros_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Current is null) { Vm.Status = "请先在左侧选中一个宏，再导出。"; return; }
        try
        {
            string safe = string.Concat(Vm.Current.Name.Split(System.IO.Path.GetInvalidFileNameChars()));
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.Desktop,
                SuggestedFileName = string.IsNullOrWhiteSpace(safe) ? "InphicMouse-macro" : safe,
            };
            picker.FileTypeChoices.Add("宏 JSON", new System.Collections.Generic.List<string> { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await FileIO.WriteTextAsync(file, Vm.ExportMacro());
            Vm.Status = $"已导出「{Vm.Current.Name}」到 {file.Path}";
        }
        catch (Exception ex)
        {
            Vm.Status = "导出失败：" + ex.Message;
        }
    }

    private async void ImportMacros_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            string json = await FileIO.ReadTextAsync(file);
            Vm.ImportMacros(json);   // 追加为新的宏（单个导出/导入工作流）
        }
        catch (Exception ex)
        {
            Vm.Status = "导入失败：" + ex.Message;
        }
    }

    // ---------------- 拖动排序 + 移动动画（FLIP）----------------
    private int _eventDragIndex = -1;
    private int _macroDragIndex = -1;
    private readonly System.Collections.Generic.Dictionary<object, double> _prevY = new();

    private void HookLists()
    {
        // handledEventsToo=true：即使项模板/列表项已把指针事件标记为 Handled，也要收到。
        EventList.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(EventList_PointerPressed), true);
        EventList.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(EventList_PointerReleased), true);
        EventList.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler(EventList_DoubleTapped), true);

        MacroList.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(MacroList_PointerPressed), true);
        MacroList.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(MacroList_PointerReleased), true);
        MacroList.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler(MacroList_DoubleTapped), true);
    }

    private void EventList_PointerPressed(object sender, PointerRoutedEventArgs e)
        => _eventDragIndex = IndexAtY(EventList, e.GetCurrentPoint(EventList).Position.Y);

    private void EventList_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_eventDragIndex >= 0)
        {
            int to = IndexAtY(EventList, e.GetCurrentPoint(EventList).Position.Y);
            if (to >= 0 && to != _eventDragIndex) MoveEventWithAnimation(_eventDragIndex, to);
        }
        _eventDragIndex = -1;
    }

    private void MacroList_PointerPressed(object sender, PointerRoutedEventArgs e)
        => _macroDragIndex = IndexAtY(MacroList, e.GetCurrentPoint(MacroList).Position.Y);

    private void MacroList_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_macroDragIndex >= 0)
        {
            int to = IndexAtY(MacroList, e.GetCurrentPoint(MacroList).Position.Y);
            if (to >= 0 && to != _macroDragIndex) MoveMacroWithAnimation(_macroDragIndex, to);
        }
        _macroDragIndex = -1;
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelectedEvent(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelectedEvent(1);

    private void MoveSelectedEvent(int delta)
    {
        if (Vm.Current is null) return;
        int i = Vm.SelectedEventIndex;
        if (i < 0 || i >= Vm.Current.Events.Count) return;
        int j = i + delta;
        if (j < 0 || j >= Vm.Current.Events.Count) return;
        MoveEventWithAnimation(i, j);
    }

    private void MoveEventWithAnimation(int from, int to)
    {
        CapturePositions(EventList);
        Vm.MoveEvent(from, to);
        PlayMoveAnimation(EventList);
    }

    private void MoveMacroWithAnimation(int from, int to)
    {
        CapturePositions(MacroList);
        Vm.MoveMacro(from, to);
        PlayMoveAnimation(MacroList);
    }

    /// <summary>记录各列表项当前 Y（相对列表），用于 FLIP 动画。</summary>
    private void CapturePositions(ListView list)
    {
        _prevY.Clear();
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.ContainerFromIndex(i) is not ListViewItem c) continue;
            // 注意：WinUI 的 ListViewItem.DataContext 为空，需用 Items[i]（数据对象）做键。
            _prevY[list.Items[i]] = c.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        }
    }

    /// <summary>FLIP：先把每项瞬间放回旧位置，再平滑滑到新位置（160ms，缓出）。</summary>
    private void PlayMoveAnimation(ListView list)
    {
        list.UpdateLayout();
        foreach (object item in _prevY.Keys)
        {
            int idx = list.Items.IndexOf(item);
            if (idx < 0) continue;
            if (list.ContainerFromIndex(idx) is not ListViewItem c) continue;

            double newY = c.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            double delta = _prevY[item] - newY;
            if (Math.Abs(delta) < 0.5) continue;

            // 动画作用在列表项容器上：连同选中高亮一起滑动。
            if (c.RenderTransform is not TranslateTransform tt)
            {
                tt = new TranslateTransform();
                c.RenderTransform = tt;
            }
            tt.Y = delta;

            var sb = new Storyboard();
            var da = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(160)),
                EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(da, tt);
            Storyboard.SetTargetProperty(da, "Y");
            sb.Children.Add(da);
            sb.Begin();
        }
        _prevY.Clear();
    }

    /// <summary>按 Y 坐标（相对列表）求所在行下标；落在列表下方则取末行。</summary>
    private static int IndexAtY(ListView list, double y)
    {
        int count = list.Items.Count;
        if (count == 0) return -1;
        for (int i = 0; i < count; i++)
        {
            if (list.ContainerFromIndex(i) is ListViewItem lvi)
            {
                double top = lvi.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                if (y < top + lvi.ActualHeight) return i;
            }
        }
        return count - 1;
    }

    private static (byte bit, bool down) Button(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(null).Properties;
        return p.PointerUpdateKind switch
        {
            Microsoft.UI.Input.PointerUpdateKind.LeftButtonPressed => (0x01, true),
            Microsoft.UI.Input.PointerUpdateKind.LeftButtonReleased => (0x01, false),
            Microsoft.UI.Input.PointerUpdateKind.RightButtonPressed => (0x02, true),
            Microsoft.UI.Input.PointerUpdateKind.RightButtonReleased => (0x02, false),
            Microsoft.UI.Input.PointerUpdateKind.MiddleButtonPressed => (0x04, true),
            Microsoft.UI.Input.PointerUpdateKind.MiddleButtonReleased => (0x04, false),
            _ => (0, false),
        };
    }
}