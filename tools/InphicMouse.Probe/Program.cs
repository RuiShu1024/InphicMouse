using System.Globalization;
using InphicMouse.Hid;
using InphicMouse.Protocol;
using InphicMouse.Protocol.Codecs;

// Inphic Mouse HID 诊断探针
// 用法:
//   InphicMouse.Probe                 枚举所有 HID 设备并对匹配鼠标做读回诊断
//   InphicMouse.Probe --vid 248A --pid 8266   指定 VID/PID
//   InphicMouse.Probe --index 5        直接用枚举列表里的第 N 个设备
//   InphicMouse.Probe --light          交互式读灯光(0x15)校准:按提示在官方软件切换模式后回车

static class Probe
{
    static int _argVid = -1, _argPid = -1, _argIndex = -1;
    static bool _lightCalib;

    static void Main(string[] args)
    {
        ParseArgs(args);
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== Inphic Mouse HID 诊断探针 ===\n");

        var all = HidDeviceEnumerator.Enumerate();
        Console.WriteLine($"共枚举到 {all.Count} 个 HID 接口:\n");
        for (int i = 0; i < all.Count; i++)
        {
            var d = all[i];
            Console.WriteLine($"[{i,2}] VID_{d.VendorId:X4}&PID_{d.ProductId:X4}  UP={d.UsagePage:X4} U={d.Usage:X4}  " +
                              $"in={d.InputReportByteLength} out={d.OutputReportByteLength} feat={d.FeatureReportByteLength}");
            Console.WriteLine($"     \"{d.ProductString}\"  mi_02={d.MatchesInterface(2)}");
            Console.WriteLine($"     {d.Path}");
        }
        Console.WriteLine();

        HidDeviceInfo? target = PickTarget(all);
        if (target is null)
        {
            Console.WriteLine("未找到匹配设备。可用 --index N 指定上面列表里的某个接口，或 --vid/--pid 指定。");
            return;
        }

        Console.WriteLine($"\n>>> 目标: VID_{target.VendorId:X4}&PID_{target.ProductId:X4}  {target.Path}\n");
        using var h = HidDeviceHandle.Open(target);
        if (h is null) { Console.WriteLine("打开失败(句柄无效)。"); return; }
        Console.WriteLine($"已打开, IsOpen={h.IsOpen}\n");

        if (_lightCalib) { LightCalibration(h); return; }

        Diagnose(h, "设备信息 0x10", DeviceInfoCodec.EncodeRequest());
        Diagnose(h, "回报率读回 0x12", MouseReport.Build(0x12, new byte[] { 0x00, 0x01, 0x00 }, ReadOnlySpan<byte>.Empty));
        Diagnose(h, "DPI 读回 0x13", MouseReport.Build(0x13, new byte[] { 0x00, 0x01, 0x00 }, ReadOnlySpan<byte>.Empty));
        Diagnose(h, "灯光读回 0x15", MouseReport.Build(0x15, new byte[] { 0x00, 0x01, 0x00 }, ReadOnlySpan<byte>.Empty));
    }

    // 发送一帧, 多次读回, 打印原始字节与两种偏移下的解析
    static void Diagnose(IHidTransport t, string title, byte[] frame)
    {
        Console.WriteLine($"---- {title} ----");
        Console.WriteLine($"发送({frame.Length}): {Hex(frame)}");
        bool wrote = t.Write(frame);
        Console.WriteLine($"Write 返回: {wrote}");

        int readLen = t.Info.InputReportByteLength > 0 ? t.Info.InputReportByteLength : 33;
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            var buf = new byte[Math.Max(readLen, 33)];
            int n = t.Read(buf, 300);
            if (n <= 0) { Console.WriteLine($"  读#{attempt}: 无数据 (n={n})"); continue; }
            Console.WriteLine($"  读#{attempt}: n={n}  {Hex(buf, n)}");
            // 首字节可能是 report id(0). 打印 [1] 与 [2] 作为 ack 候选
            Console.WriteLine($"     buf[0]={buf[0]:X2} buf[1]={buf[1]:X2} buf[2]={buf[2]:X2}  (原程序判定 resp[1]==0 成功)");
            if (frame[1] == 0x10) ParseDevInfo(buf, n);
            break;
        }
        Console.WriteLine();
    }

    // 在两种偏移假设下解析设备信息, 帮助确认 report id 是否占位
    static void ParseDevInfo(byte[] buf, int n)
    {
        foreach (int off in new[] { 0, 1 })
        {
            if (n < off + 0x0F) continue;
            var slice = buf.AsSpan(off, n - off);
            var info = DeviceInfoCodec.Parse(slice);
            if (info is null) continue;
            Console.WriteLine($"     [偏移+{off}] 固件={info.FirmwareText} IC=0x{info.IcType:X2} " +
                              $"电量={info.BatteryPercent}% 充电={info.IsCharging} 在线={info.IsOnline} 回报档={info.ReportRateMaxIndex}");
        }
    }

    static void LightCalibration(IHidTransport t)
    {
        Console.WriteLine("灯光校准: 请在【官方软件】里依次切换每种灯效, 每切一次回到这里回车, 记录读回的模式 id。\n输入 q 退出。");
        while (true)
        {
            Console.Write("切好后回车(或 q 退出): ");
            var line = Console.ReadLine();
            if (line?.Trim().ToLower() == "q") break;
            t.Write(MouseReport.Build(0x15, new byte[] { 0x00, 0x01, 0x00 }, ReadOnlySpan<byte>.Empty));
            var buf = new byte[Math.Max((int)t.Info.InputReportByteLength, 33)];
            int n = t.Read(buf, 300);
            if (n <= 0) { Console.WriteLine("  读不到, 重试"); continue; }
            Console.WriteLine($"  原始: {Hex(buf, n)}");
            Console.WriteLine($"  候选模式 id: buf[4]={buf[4]} buf[5]={buf[5]}  (记下此时官方软件选的是哪种灯效)");
        }
    }

    static HidDeviceInfo? PickTarget(IReadOnlyList<HidDeviceInfo> all)
    {
        if (_argIndex >= 0 && _argIndex < all.Count) return all[_argIndex];
        IEnumerable<HidDeviceInfo> q = all;
        if (_argVid >= 0) q = q.Where(d => d.VendorId == _argVid);
        if (_argPid >= 0) q = q.Where(d => d.ProductId == _argPid);
        if (_argVid < 0 && _argPid < 0)
            q = q.Where(d => d.VendorId is 0x248A or 0x249A);
        var list = q.ToList();
        if (list.Count == 0) return null;
        return list.FirstOrDefault(d => d.MatchesInterface(2)) ?? list[0];
    }

    static void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--vid" when i + 1 < args.Length: _argVid = ParseHex(args[++i]); break;
                case "--pid" when i + 1 < args.Length: _argPid = ParseHex(args[++i]); break;
                case "--index" when i + 1 < args.Length: int.TryParse(args[++i], out _argIndex); break;
                case "--light": _lightCalib = true; break;
            }
        }
    }

    static int ParseHex(string s) =>
        int.TryParse(s.Replace("0x", "").Replace("0X", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v) ? v : -1;

    static string Hex(ReadOnlySpan<byte> b, int len = -1)
    {
        if (len < 0) len = b.Length;
        var sb = new System.Text.StringBuilder(len * 3);
        for (int i = 0; i < len; i++) sb.Append(b[i].ToString("X2")).Append(' ');
        return sb.ToString().TrimEnd();
    }
}
