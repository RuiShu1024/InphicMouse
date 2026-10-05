using Microsoft.UI.Xaml.Controls;
using InphicMouse.App.ViewModels;

namespace InphicMouse.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel Vm { get; } = App.GetService<SettingsViewModel>();
    public SettingsPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => Vm.Detach();
    }
}
