using System.Linq;
using InphicMouse.Protocol;
using InphicMouse.Protocol.Codecs;
using Xunit;

namespace InphicMouse.Protocol.Tests;

public class DpiCodecTests
{
    [Fact]
    public void EncodeStages_TwoStages_ByteExact()
    {
        // ic 0x25: 800→8, 1600→16；当前档 0，共 2 档
        byte[] buf = DpiCodec.EncodeStages(0x25, new[] { 800, 1600 }, currentIndex: 0);

        Assert.Equal(0x03, buf[1]);
        Assert.Equal(0x00, buf[2]);
        Assert.Equal(0x01, buf[3]);
        Assert.Equal(0x25, buf[4]);
        Assert.Equal(0x02, buf[5]);            // (0<<4)|2
        // 档1: reg 8 → lo,hi,lo,hi (X/Y 相同)
        Assert.Equal(new byte[] { 8, 0, 8, 0 }, buf[6..10]);
        // 档2: reg 16
        Assert.Equal(new byte[] { 16, 0, 16, 0 }, buf[10..14]);
        Assert.True(MouseReport.Verify(buf));
        Assert.Equal(50, buf[MouseReport.ChecksumIndex]); // 2+8+8+16+16
    }

    [Fact]
    public void EncodeStages_CurrentIndexInHighNibble()
    {
        byte[] buf = DpiCodec.EncodeStages(0x25, new[] { 800, 1600, 2400 }, currentIndex: 2);
        Assert.Equal((2 << 4) | 3, buf[5]);
    }

    [Fact]
    public void EncodeStages_RejectsBadStageCount()
    {
        Assert.Throws<System.ArgumentException>(() => DpiCodec.EncodeStages(0x25, new int[0], 0));
        Assert.Throws<System.ArgumentException>(() => DpiCodec.EncodeStages(0x25, new int[7], 0));
    }

    [Fact]
    public void EncodeReset_MatchesSpec()
    {
        byte[] buf = DpiCodec.EncodeReset();
        Assert.Equal(0x0C, buf[1]);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x01 }, buf[2..5]);
        Assert.Equal(0x01, buf[5]);
    }
}

public class LightCodecTests
{
    [Fact]
    public void Encode_Mode3_SolidColor_ByteExact()
    {
        // 常亮：单色 RGB。color 0x0000FF → R=0xFF,G=0,B=0；亮度5/速度3
        byte[] buf = LightCodec.Encode(modeId: 3, brightness: 5, speed: 3, colors: new[] { 0x0000FF });
        Assert.Equal(0x05, buf[1]);
        Assert.Equal(0x05, buf[4]);            // 载荷长度
        Assert.Equal(3, buf[5]);               // 模式
        Assert.Equal(0x53, buf[6]);            // (5<<4)|3
        Assert.Equal(new byte[] { 0xFF, 0x00, 0x00 }, buf[7..10]);
        Assert.True(MouseReport.Verify(buf));
    }

    [Fact]
    public void Encode_Mode2_SevenColors_ByteExact()
    {
        var colors = new[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
        byte[] buf = LightCodec.Encode(modeId: 2, brightness: 15, speed: 0, colors: colors);
        Assert.Equal(0x05, buf[1]);
        Assert.Equal(0x18, buf[4]);            // 24
        Assert.Equal(2, buf[5]);
        Assert.Equal(0xF0, buf[6]);            // (15<<4)|0
        Assert.Equal(0x07, buf[7]);            // 色数
        // 每档 RGB：低位起 R,G,B。color 0x01 → 1,0,0
        Assert.Equal(new byte[] { 1, 0, 0 }, buf[8..11]);
        Assert.True(MouseReport.Verify(buf));
    }

    [Fact]
    public void Encode_SimpleModes_TwoBytePayload()
    {
        foreach (int mode in new[] { 0, 1, 4, 5 })
        {
            byte[] buf = LightCodec.Encode(mode, brightness: 8, speed: 2);
            Assert.Equal(0x05, buf[1]);
            Assert.Equal(mode == 1 ? 0x03 : 0x02, buf[4]);
            Assert.Equal((byte)mode, buf[5]);
            Assert.Equal(0x82, buf[6]);        // (8<<4)|2
        }
    }

    [Fact]
    public void Encode_Mode2_RequiresSevenColors()
    {
        Assert.Throws<System.ArgumentException>(() =>
            LightCodec.Encode(2, 0, 0, new[] { 1, 2, 3 }));
    }
}

public class DeviceSettingsCodecTests
{
    [Fact]
    public void EncodeDpiColors_SixStages_ByteExact()
    {
        // color 值 0x030201 → R=0x01,G=0x02,B=0x03
        var colors = Enumerable.Range(0, 6).Select(i => 0x030201 + i).ToArray();
        byte[] buf = DeviceSettingsCodec.EncodeDpiColors(colors);
        Assert.Equal(0x04, buf[1]);
        Assert.Equal(0x12, buf[4]);            // 18 = 6*3
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, buf[5..8]);
        Assert.True(MouseReport.Verify(buf));
    }

    [Fact]
    public void EncodeSensorLift_FlagBitsSplit()
    {
        // sensor_flag bit3→buf6, bit2→buf7
        byte[] buf = DeviceSettingsCodec.EncodeSensorLift(liftoffHeight: 2, sensorFlag: 0x08);
        Assert.Equal(0x06, buf[1]);
        Assert.Equal(2, buf[5]);
        Assert.Equal(1, buf[6]);
        Assert.Equal(0, buf[7]);

        byte[] buf2 = DeviceSettingsCodec.EncodeSensorLift(0, 0x04);
        Assert.Equal(0, buf2[6]);
        Assert.Equal(1, buf2[7]);
    }

    [Fact]
    public void EncodeSleep_FieldOrder()
    {
        byte[] buf = DeviceSettingsCodec.EncodeSleep(sleepLight: 1, moveWakeup: 2, moveCloseLight: 3, buttonRespondTime: 4);
        Assert.Equal(0x07, buf[1]);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buf[5..9]);
    }

    [Fact]
    public void EncodeReportRate_SingleByteCode()
    {
        byte[] buf = DeviceSettingsCodec.EncodeReportRate(2);
        Assert.Equal(0x02, buf[1]);
        Assert.Equal(0x01, buf[4]);
        Assert.Equal(2, buf[5]);
    }
}

public class DeviceInfoCodecTests
{
    [Fact]
    public void Parse_ExtractsAllFields()
    {
        var dev = new byte[0x10];
        dev[8] = 0x02; dev[9] = 0x01;   // 固件 0x0102
        dev[0x0A] = 0x25;               // ic_type
        dev[0x0B] = 0x20;               // → 回报率档 6
        dev[0x0C] = 1;                  // 充电中
        dev[0x0D] = 77;                 // 电量
        dev[0x0E] = 1;                  // 在线

        var info = DeviceInfoCodec.Parse(dev);
        Assert.NotNull(info);
        Assert.Equal(0x0102, info!.FirmwareVersion);
        Assert.Equal("1.02", info.FirmwareText);
        Assert.Equal(0x25, info.IcType);
        Assert.Equal(6, info.ReportRateMaxIndex);
        Assert.True(info.IsCharging);
        Assert.Equal(77, info.BatteryPercent);
        Assert.True(info.IsOnline);
    }

    [Fact]
    public void Parse_ShortBufferReturnsNull()
    {
        Assert.Null(DeviceInfoCodec.Parse(new byte[5]));
    }

    [Fact]
    public void EncodeRequest_IsCmd0x10()
    {
        byte[] buf = DeviceInfoCodec.EncodeRequest();
        Assert.Equal(0x10, buf[1]);
        Assert.Equal(MouseReport.Length, buf.Length);
    }
}

public class MacroCodecTests
{
    // 官方 USB 抓包(2.pcapng)：宏"按下A，延时50ms，松开A"、绑定到 6 号键(key_code=5) 时官方下发：
    // 00 08 00 01 09 | 06 30 32 00 04 B0 01 00 04 | 00... 21
    [Fact]
    public void Encode_KeyA_Delay50_KeyAUp_MatchesOfficialCapture()
    {
        var frames = MacroCodec.Encode(5, new[]
        {
            MacroCodec.MacroEvent.KeyDown(0x41),   // VK 'A'
            MacroCodec.MacroEvent.Delay(50),
            MacroCodec.MacroEvent.KeyUp(0x41),
        });

        byte[] buf = Assert.Single(frames);
        var expected = new byte[33];
        expected[1] = 0x08; expected[3] = 0x01; expected[4] = 0x09; expected[5] = 0x06;
        expected[6] = 0x30; expected[7] = 0x32; expected[8] = 0x00; expected[9] = 0x04;   // A down + 50ms
        expected[10] = 0xB0; expected[11] = 0x01; expected[12] = 0x00; expected[13] = 0x04; // A up (min 1ms)
        expected[32] = 0x21;
        Assert.Equal(expected, buf);
        Assert.True(MouseReport.Verify(buf));
    }

    [Fact]
    public void Encode_MouseClick_DelayMergedOntoPreviousAction()
    {
        var frames = MacroCodec.Encode(0, new[]
        {
            MacroCodec.MacroEvent.MouseDown(1),
            MacroCodec.MacroEvent.Delay(50),
            MacroCodec.MacroEvent.MouseUp(1),
        });

        byte[] buf = Assert.Single(frames);
        Assert.Equal(0x09, buf[4]);
        Assert.Equal(0x01, buf[5]);                                       // 槽 0 → +1 = 1
        Assert.Equal(new byte[] { 0x10, 0x32, 0x00, 0x01 }, buf[6..10]);  // 左键按下 + 50ms
        Assert.Equal(new byte[] { 0x90, 0x01, 0x00, 0x01 }, buf[10..14]); // 左键松开
        Assert.True(MouseReport.Verify(buf));
    }

    [Fact]
    public void Encode_ModifierKey_UsesModifierOpcodes()
    {
        var frames = MacroCodec.Encode(0, new[]
        {
            MacroCodec.MacroEvent.KeyDown(0xA2),   // LCtrl
            MacroCodec.MacroEvent.KeyUp(0xA2),
        });
        byte[] buf = Assert.Single(frames);
        Assert.Equal(new byte[] { 0x20, 0x01, 0x00, 0x01 }, buf[6..10]);   // 修饰位 0x01
        Assert.Equal(new byte[] { 0xA0, 0x01, 0x00, 0x01 }, buf[10..14]);
    }

    [Fact]
    public void Encode_TenRecords_CollapsesDelaysIntoSixActions()
    {
        // 官方 DB macro_id=2 的 10 条记录：A↓ 50 A↑ 1000 LB↓ 50 LB↑ MB↓ 50 MB↑
        var frames = MacroCodec.Encode(5, new[]
        {
            MacroCodec.MacroEvent.KeyDown(0x41), MacroCodec.MacroEvent.Delay(50),
            MacroCodec.MacroEvent.KeyUp(0x41), MacroCodec.MacroEvent.Delay(1000),
            MacroCodec.MacroEvent.MouseDown(1), MacroCodec.MacroEvent.Delay(50),
            MacroCodec.MacroEvent.MouseUp(1),
            MacroCodec.MacroEvent.MouseDown(4), MacroCodec.MacroEvent.Delay(50),
            MacroCodec.MacroEvent.MouseUp(4),
        });

        byte[] buf = Assert.Single(frames);
        Assert.Equal(0x06, buf[5]);
        Assert.Equal(0x19, buf[4]);                                        // 6 动作*4 + 1
        Assert.Equal(new byte[] { 0x30, 0x32, 0x00, 0x04 }, buf[6..10]);
        Assert.Equal(new byte[] { 0xB0, 0xE8, 0x03, 0x04 }, buf[10..14]);  // 1000ms
        Assert.Equal(new byte[] { 0x10, 0x32, 0x00, 0x01 }, buf[14..18]);
        Assert.Equal(new byte[] { 0x90, 0x01, 0x00, 0x01 }, buf[18..22]);
        Assert.Equal(new byte[] { 0x10, 0x32, 0x00, 0x04 }, buf[22..26]);
        Assert.Equal(new byte[] { 0x90, 0x01, 0x00, 0x04 }, buf[26..30]);
        Assert.True(MouseReport.Verify(buf));
    }
}
