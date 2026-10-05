using Microsoft.UI.Xaml.Controls;
using InphicMouse.App.ViewModels;

namespace InphicMouse.App.Views;

public sealed partial class InfoPage : Page
{
    public InfoViewModel Vm { get; } = App.GetService<InfoViewModel>();
    public InfoPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => Vm.Detach();
    }
}
