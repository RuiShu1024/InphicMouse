using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InphicMouse.Protocol.Services;

namespace InphicMouse.App.ViewModels;

public sealed partial class ReportRateViewModel : ObservableObject
{
    private readonly IMouseService _svc;

    public ObservableCollection<string> Options { get; } = new();

    [ObservableProperty] private int _selectedIndex;
    [ObservableProperty] private string _status = "";

    // 实机确认(反馈3)：档位码 = 1000/Hz。code 1→1000, 2→500, 4→250, 8→125。
    private static readonly (int Hz, int Code)[] Table =
    {
        (1000, 1), (500, 2), (250, 4), (125, 8),
    };

    public ReportRateViewModel(IMouseService svc)
    {
        _svc = svc;
        int max = System.Math.Clamp(svc.Capabilities?.ReportRateMax ?? 4, 1, Table.Length);
        for (int i = 0; i < max; i++)
            Options.Add($"{Table[i].Hz} Hz");
        _selectedIndex = IndexOfCode(svc.Settings.ReportRateCode);
    }

    private int IndexOfCode(int code)
    {
        for (int i = 0; i < Options.Count; i++)
            if (Table[i].Code == code) return i;
        return 0;
    }

    [RelayCommand]
    private async Task Apply()
    {
        int code = Table[System.Math.Clamp(SelectedIndex, 0, Options.Count - 1)].Code;
        _svc.Settings.ReportRateCode = code;
        bool ok = await _svc.ApplyReportRateAsync();
        Status = ok ? $"已下发回报率 {Table[SelectedIndex].Hz} Hz" : "下发失败";
    }
}
