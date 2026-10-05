using System;
using InphicMouse.Protocol;
using InphicMouse.Protocol.Codecs;
using Xunit;

namespace InphicMouse.Protocol.Tests;

public class KeyCodesTests
{
    [Theory]
    [InlineData(0x00, 0x00)]
    [InlineData(0xE0, 0x01)] // LCtrl
    [InlineData(0xA2, 0x01)]
    [InlineData(0xE1, 0x02)] // LShift
    [InlineData(0xA0, 0x02)]
    [InlineData(0xE2, 0x04)] // LAlt
    [InlineData(0xE3, 0x08)] // LGui
    [InlineData(0x5B, 0x08)]
    [InlineData(0xE4, 0x10)] // RCtrl
    [InlineData(0xE5, 0x20)] // RShift
    [InlineData(0xE6, 0x40)] // RAlt
    [InlineData(0xE7, 0x80)] // RGui
    [InlineData(0x5C, 0x80)]
    [InlineData(0x04, 0x04)] // 非修饰：原样（HID 'a'）
    public void GetSysKeyCode_MatchesReference(int usage, int expected)
    {
        Assert.Equal(expected, KeyCodes.GetSysKeyCode(usage));
    }

    [Theory]
    [InlineData(0xE0, true)]
    [InlineData(0xE7, true)]
    [InlineData(0xDF, false)]
    [InlineData(0x04, false)]
    public void IsModifierUsage(int usage, bool expected) =>
        Assert.Equal(expected, KeyCodes.IsModifierUsage(usage));
}

public class KeyMapCodecTests
{
    [Fact]
    public void DefaultPayload_MatchesReverseEngineeredTemplate()
    {
        byte[] buf = KeyMapCodec.EncodeDefault();
        Assert.Equal(0x09, buf[1]);
        Assert.Equal(0x0F, buf[4]);
        Assert.Equal(new byte[]
        {
            0x10, 0x01, 0x00,
            0x10, 0x02, 0x00,
            0x10, 0x04, 0x00,
            0x10, 0x08, 0x00,
            0x10, 0x10, 0x00,
        }, buf[5..20]);
        Assert.True(MouseReport.Verify(buf));
    }

    [Fact]
    public void Encode_KeyboardKeyWithModifier()
    {
        // Ctrl+C: usage 'c'=0x06(HID), 修饰掩码 = LCtrl 0x01
        var entry = KeyMapCodec.KeyEntry.Keyboard(usage: 0x06, modifierMask: 0x01);
        Assert.Equal(0x70, entry.Type);
        Assert.Equal(0x01, entry.V1);   // 掩码
        Assert.Equal(0x06, entry.V2);   // usage 原样
    }

    [Fact]
    public void Encode_StandaloneModifierKey_ShiftsToV1()
    {
        // 单独映射为 LCtrl（usage 0xE0），无额外掩码 → v1=修饰位, v2=0
        var entry = KeyMapCodec.KeyEntry.Keyboard(usage: 0xE0, modifierMask: 0);
        Assert.Equal(0x70, entry.Type);
        Assert.Equal(0x01, entry.V1);
        Assert.Equal(0x00, entry.V2);
    }

    [Fact]
    public void Encode_FiveEntries_ByteExact()
    {
        var keys = new[]
        {
            KeyMapCodec.KeyEntry.Mouse(0x01),
            KeyMapCodec.KeyEntry.Mouse(0x02),
            KeyMapCodec.KeyEntry.System(),
            KeyMapCodec.KeyEntry.Keyboard(0x06, 0x01),
            KeyMapCodec.KeyEntry.Mouse(0x10),
        };
        byte[] buf = KeyMapCodec.Encode(keys);
        Assert.Equal(new byte[] { 0x10, 0x01, 0x00 }, buf[5..8]);
        Assert.Equal(new byte[] { 0x60, 0x00, 0x00 }, buf[11..14]);
        Assert.Equal(new byte[] { 0x70, 0x01, 0x06 }, buf[14..17]);
        Assert.True(MouseReport.Verify(buf));
    }

    [Fact]
    public void Encode_RejectsWrongKeyCount()
    {
        Assert.Throws<ArgumentException>(() =>
            KeyMapCodec.Encode(System.Array.Empty<KeyMapCodec.KeyEntry>()));
        Assert.Throws<ArgumentException>(() =>
            KeyMapCodec.Encode(new KeyMapCodec.KeyEntry[10]));
    }

    [Fact]
    public void Encode_SixKeys_HeaderLengthMatches()
    {
        var keys = new[]
        {
            KeyMapCodec.KeyEntry.Mouse(0x01), KeyMapCodec.KeyEntry.Mouse(0x02),
            KeyMapCodec.KeyEntry.Mouse(0x04), KeyMapCodec.KeyEntry.Mouse(0x08),
            KeyMapCodec.KeyEntry.Mouse(0x10), new KeyMapCodec.KeyEntry(0x40, 0x01, 0x00),
        };
        byte[] buf = KeyMapCodec.Encode(keys);
        Assert.Equal(0x12, buf[4]); // 6*3 = 18
        Assert.Equal(new byte[] { 0x40, 0x01, 0x00 }, buf[20..23]);
        Assert.True(MouseReport.Verify(buf));
    }
}

public class MacroBindingTests
{
    // 官方抓包 2.pcapng：宏绑到 key_code=5，官方下发条目 90 26 01（v1 = (5+1)|0x20, v2 = 1）。
    [Fact]
    public void MacroEntry_PlayOnce_MatchesOfficialCapture()
    {
        var entry = KeyMapCodec.KeyEntry.Macro(5, repeatCount: 1);
        Assert.Equal(0x90, entry.Type);
        Assert.Equal(0x26, entry.V1);
        Assert.Equal(0x01, entry.V2);
    }

    [Fact]
    public void SixKeyFrame_CarriesMacroOnLastKey()
    {
        var keys = new[]
        {
            KeyMapCodec.KeyEntry.Mouse(0x01), KeyMapCodec.KeyEntry.Mouse(0x02),
            KeyMapCodec.KeyEntry.Mouse(0x04), KeyMapCodec.KeyEntry.Mouse(0x08),
            KeyMapCodec.KeyEntry.Mouse(0x10), KeyMapCodec.KeyEntry.Macro(5, repeatCount: 1),
        };
        byte[] buf = KeyMapCodec.Encode(keys);
        Assert.Equal(new byte[] { 0x90, 0x26, 0x01 }, buf[20..23]);
        Assert.True(MouseReport.Verify(buf));
    }

    [Fact]
    public void MacroEntry_LoopModes_SetExpectedFlags()
    {
        Assert.Equal(0x10, KeyMapCodec.KeyEntry.Macro(0, repeatUntilRelease: true).V1 & 0xF0);
        Assert.Equal(0x40, KeyMapCodec.KeyEntry.Macro(0, untilAnyKey: true).V1 & 0xF0);
        Assert.Equal(0x20, KeyMapCodec.KeyEntry.Macro(0, repeatCount: 3).V1 & 0xF0);
        Assert.Equal(0x00, KeyMapCodec.KeyEntry.Macro(0).V1 & 0xF0);
    }

    // Apply 流程：宏条目按"目标键下标"重定位（官方约定 宏槽 = key_code）。
    [Fact]
    public void RebindMacroToKey_MatchesOfficialCapture()
    {
        var bound = KeyMapCodec.KeyEntry.Macro(0, repeatCount: 1);   // 本地宏槽 0 → 0x21
        Assert.Equal(0x21, bound.V1);

        var wire = KeyMapCodec.RebindMacroToKey(bound, 5);          // 绑到 6 号键(index 5)
        Assert.Equal(0x90, wire.Type);
        Assert.Equal(0x26, wire.V1);
        Assert.Equal(0x01, wire.V2);
    }

    [Fact]
    public void RebindMacroToKey_KeepsLoopFlags()
    {
        var held = KeyMapCodec.RebindMacroToKey(KeyMapCodec.KeyEntry.Macro(0, repeatUntilRelease: true), 2);
        Assert.Equal(0x13, held.V1);   // (2+1) | 0x10
        var any = KeyMapCodec.RebindMacroToKey(KeyMapCodec.KeyEntry.Macro(0, untilAnyKey: true), 2);
        Assert.Equal(0x43, any.V1);    // (2+1) | 0x40
    }

    // 回归：宏绑定被"重定位到目标键"后再次进入页面，仍必须能回选到该宏（而不是掉回"鼠标功能/左键"）。
    [Fact]
    public void MacroEntryMatches_LogicalAndRebound()
    {
        var logical = KeyMapCodec.KeyEntry.Macro(0, repeatCount: 1);   // 逻辑条目 0x21（宏槽 0）
        var rebound = KeyMapCodec.RebindMacroToKey(logical, 5);       // 下发线字节 0x26（键下标 5）

        Assert.True(KeyMapCodec.MacroEntryMatches(logical, logical));
        Assert.True(KeyMapCodec.MacroEntryMatches(rebound, logical)); // 旧数据（重定位过）也能回选
        Assert.False(KeyMapCodec.MacroEntryMatches(KeyMapCodec.KeyEntry.Mouse(0x01), logical));
        Assert.False(KeyMapCodec.MacroEntryMatches(logical, KeyMapCodec.KeyEntry.Mouse(0x01)));
    }
}