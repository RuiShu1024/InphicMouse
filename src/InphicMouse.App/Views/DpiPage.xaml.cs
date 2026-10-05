using Microsoft.UI.Xaml.Controls;
using InphicMouse.App.ViewModels;

namespace InphicMouse.App.Views;

public sealed partial class DpiPage : Page
{
    public DpiViewModel Vm { get; } = App.GetService<DpiViewModel>();
    public DpiPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => Vm.Detach();
    }
}
