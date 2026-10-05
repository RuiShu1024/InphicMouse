using Microsoft.UI.Xaml.Controls;
using InphicMouse.App.ViewModels;

namespace InphicMouse.App.Views;

public sealed partial class LightPage : Page
{
    public LightViewModel Vm { get; } = App.GetService<LightViewModel>();
    public LightPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => Vm.Detach();
    }
}
