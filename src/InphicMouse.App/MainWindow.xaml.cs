using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using InphicMouse.App.ViewModels;
using InphicMouse.App.Views;

namespace InphicMouse.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel Vm { get; } = App.GetService<MainViewModel>();

    public MainWindow()
    {
        InitializeComponent();
        ContentFrame.Navigate(typeof(InfoPage));
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        var page = (item.Tag as string) switch
        {
            "info" => typeof(InfoPage),
            "dpi" => typeof(DpiPage),
            "rate" => typeof(ReportRatePage),
            "light" => typeof(LightPage),
            "keys" => typeof(KeyMapPage),
            "macro" => typeof(MacroPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(InfoPage),
        };
        ContentFrame.Navigate(page);
    }
}
