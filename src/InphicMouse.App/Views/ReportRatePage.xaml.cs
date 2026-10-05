using Microsoft.UI.Xaml.Controls;
using InphicMouse.App.ViewModels;

namespace InphicMouse.App.Views;

public sealed partial class ReportRatePage : Page
{
    public ReportRateViewModel Vm { get; } = App.GetService<ReportRateViewModel>();
    public ReportRatePage() => InitializeComponent();
}
