using InphicMouse.Protocol;
using Xunit;

namespace InphicMouse.Protocol.Tests;

public class DpiConversionTests
{
    // 复刻 GetMouseDPIValue，逐 ic_type 校验分段换算。
    [Theory]
    // 0x11: 三段
    [InlineData(0x11, 5000, 100)]     // <10001 → 5000/50
    [InlineData(0x11, 12000, 220)]    // 10001..12999 → (12000-10000)/100+200
    [InlineData(0x11, 14000, 222)]    // >12999 → (14000-13000)/1000+221
    // 0x05: 两段
    [InlineData(0x05, 6400, 128)]     // <6401 → 6400/50
    [InlineData(0x05, 8000, 144)]     // ≥6401 → (8000-6400)/100+128
    // 0x25: 两段（6827 机型）
    [InlineData(0x25, 800, 8)]        // ≤5000 → 800/100
    [InlineData(0x25, 6000, 52)]      // >5000 → (6000-5000)/500+50
    // 0x26: 两段（下段 -1）
    [InlineData(0x26, 100, 0)]        // <5001 → 100/100-1
    [InlineData(0x26, 6000, 52)]
    // 0x31
    [InlineData(0x31, 12000, 120)]    // ≤12000 → 12000/100
    [InlineData(0x31, 14000, 122)]    // >12000 → (14000-12000)/1000+120
    // 直除 /100 组
    [InlineData(0x04, 800, 8)]
    [InlineData(0x35, 1600, 16)]
    // 默认: dpi/50-1
    [InlineData(0x99, 800, 15)]
    public void ToRegister_MatchesReference(int icType, int dpi, int expectedReg)
    {
        Assert.Equal(expectedReg, DpiConversion.ToRegister(icType, dpi));
    }

    [Fact]
    public void Clamp_BoundsToLegalRange()
    {
        Assert.Equal(DpiConversion.DpiMin, DpiConversion.Clamp(10));
        Assert.Equal(DpiConversion.DpiMax, DpiConversion.Clamp(999999));
        Assert.Equal(1600, DpiConversion.Clamp(1600));
    }

    // 关键往返：对可逆档点，寄存器值经反算应回到原 DPI。
    [Theory]
    [InlineData(0x11, 800)]
    [InlineData(0x11, 1600)]
    [InlineData(0x11, 6400)]
    [InlineData(0x11, 12000)]
    [InlineData(0x25, 800)]
    [InlineData(0x25, 6000)]
    [InlineData(0x05, 6400)]
    [InlineData(0x05, 8000)]
    [InlineData(0x31, 12000)]
    [InlineData(0x04, 4800)]
    public void RoundTrip_RegisterBackToDpi(int icType, int dpi)
    {
        ushort reg = DpiConversion.ToRegister(icType, dpi);
        Assert.Equal(dpi, DpiConversion.FromRegister(icType, reg));
    }

    // 实机(IN9 3311, ic 0x11)读回的出厂寄存器 → DPI（复刻 GetDPIValueByMouseDPI）。
    [Theory]
    [InlineData(15, 750)]
    [InlineData(31, 1550)]
    [InlineData(239, 12000)]
    public void FromRegister_Ic11_MatchesDevice(int reg, int expected)
    {
        Assert.Equal(expected, DpiConversion.FromRegister(0x11, reg));
    }
}
