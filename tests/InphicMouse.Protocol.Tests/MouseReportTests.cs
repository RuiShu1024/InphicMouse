using InphicMouse.Protocol;
using Xunit;

namespace InphicMouse.Protocol.Tests;

public class MouseReportTests
{
    [Fact]
    public void Build_SetsFrameLayoutAndChecksum()
    {
        byte[] buf = MouseReport.Build(0x03,
            header: new byte[] { 0x00, 0x01, 0x25 },
            payload: new byte[] { 0x02, 0x08, 0x00, 0x08, 0x00 });

        Assert.Equal(MouseReport.Length, buf.Length); // 33
        Assert.Equal(0x00, buf[0]);                    // Report ID
        Assert.Equal(0x03, buf[1]);                    // 命令码
        Assert.Equal(0x00, buf[2]);
        Assert.Equal(0x01, buf[3]);
        Assert.Equal(0x25, buf[4]);
        Assert.Equal(0x02, buf[5]);                    // 载荷起点
        // 校验和 = sum(buf[5..31]) & 0xFF = 2+8+0+8+0 = 18
        Assert.Equal(18, buf[MouseReport.ChecksumIndex]);
        Assert.True(MouseReport.Verify(buf));
    }

    [Fact]
    public void Build_HeaderNotCountedInChecksum()
    {
        // 头部(buf[1..4]) 不参与校验：改命令码/头部不应改变校验和
        byte[] a = MouseReport.Build(0x03, new byte[] { 0x00, 0x01, 0x25 }, new byte[] { 0x10 });
        byte[] b = MouseReport.Build(0x05, new byte[] { 0x00, 0x01, 0x02 }, new byte[] { 0x10 });
        Assert.Equal(a[MouseReport.ChecksumIndex], b[MouseReport.ChecksumIndex]);
        Assert.Equal(0x10, a[MouseReport.ChecksumIndex]);
    }

    [Fact]
    public void Build_ChecksumWrapsAt8Bits()
    {
        // 载荷两字节 0xFF,0x02 → 0x101 → &0xFF = 0x01
        byte[] buf = MouseReport.Build(0x03, new byte[] { 0x00, 0x01, 0x02 }, new byte[] { 0xFF, 0x02 });
        Assert.Equal(0x01, buf[MouseReport.ChecksumIndex]);
    }

    [Fact]
    public void Build_RejectsOversizedHeaderAndPayload()
    {
        Assert.Throws<System.ArgumentException>(() =>
            MouseReport.Build(0x03, new byte[4], System.ReadOnlySpan<byte>.Empty));
        Assert.Throws<System.ArgumentException>(() =>
            MouseReport.Build(0x03, new byte[] { 0, 1, 0 }, new byte[28]));
    }
}
