using System.Linq;
using System.Threading.Tasks;
using InphicMouse.Core.Metadata;
using InphicMouse.Core.Models;
using InphicMouse.Protocol.Services;
using Xunit;

namespace InphicMouse.Protocol.Tests;

public class ColorUtilTests
{
    [Theory]
    [InlineData("#FF0000", 0x0000FF)] // R=FF → 低位
    [InlineData("#00FF00", 0x00FF00)]
    [InlineData("#0000FF", 0xFF0000)] // B=FF → 高位
    public void FromHex_LowByteIsRed(string hex, int expected) =>
        Assert.Equal(expected, ColorUtil.FromHex(hex));

    [Fact]
    public void RoundTrip_HexToIntToHex()
    {
        int c = ColorUtil.FromHex("#123456");
        Assert.Equal("#123456", ColorUtil.ToHex(c));
    }

    [Fact]
    public void FromHex_HandlesAlphaAndBadInput()
    {
        Assert.Equal(ColorUtil.FromHex("#00FF0000"), ColorUtil.FromHex("#FF0000"));
        Assert.Equal(0xFFFFFF, ColorUtil.FromHex("garbage"));
    }
}

public class SimulatedMouseServiceTests
{
    private static SimulatedMouseService Make() =>
        new(MetadataProvider.BuiltInCapabilities, "IN9");

    [Fact]
    public void Constructs_ConnectedSimulatedState()
    {
        var s = Make();
        Assert.True(s.Status.IsConnected);
        Assert.True(s.Status.IsSimulated);
        Assert.Equal(6, s.Settings.DpiValues.Count);
        Assert.Equal(85, s.Status.BatteryPercent);
        Assert.Equal("1.02", s.Status.FirmwareText);
    }

    [Fact]
    public async Task Apply_ReturnsTrue_AndRaisesOnRefresh()
    {
        var s = Make();
        Assert.True(await s.ApplyDpiAsync());
        Assert.True(await s.ApplyLightAsync());

        bool raised = false;
        s.StatusChanged += (_, _) => raised = true;
        await s.RefreshInfoAsync();
        Assert.True(raised);
    }

    [Fact]
    public async Task ResetDpi_RestoresDefaults()
    {
        var s = Make();
        s.Settings.DpiValues[0] = 12345;
        await s.ResetDpiAsync();
        Assert.Equal(800, s.Settings.DpiValues[0]);
    }
}

public class DeviceLocatorTests
{
    [Fact]
    public void Find_PrefersMi02Interface()
    {
        var model = MetadataProvider.BuiltInModel;
        var mode = model.Modes.First();
        var devices = new[]
        {
            new Hid.HidDeviceInfo { Path = @"\\?\hid#vid_248a&pid_8266&mi_00", VendorId = mode.Vid, ProductId = mode.Pid },
            new Hid.HidDeviceInfo { Path = @"\\?\hid#vid_248a&pid_8266&mi_02", VendorId = mode.Vid, ProductId = mode.Pid },
        };
        var found = DeviceLocator.Find(new[] { model }, devices);
        Assert.NotNull(found);
        Assert.Contains("mi_02", found!.Device.Path);
    }

    [Fact]
    public void Find_ReturnsNullWhenNoMatch()
    {
        var devices = new[] { new Hid.HidDeviceInfo { Path = "x", VendorId = 0x1234, ProductId = 0x5678 } };
        Assert.Null(DeviceLocator.Find(new[] { MetadataProvider.BuiltInModel }, devices));
    }
}
