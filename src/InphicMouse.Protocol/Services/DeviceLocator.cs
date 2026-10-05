using System.Collections.Generic;
using System.Linq;
using InphicMouse.Core.Models;
using InphicMouse.Hid;

namespace InphicMouse.Protocol.Services;

/// <summary>按 config.xml 的型号/模式(VID/PID + MI_02)在系统 HID 设备中定位鼠标，并打开通信句柄。</summary>
public static class DeviceLocator
{
    public sealed record Located(HidDeviceInfo Device, MouseModelInfo Model, DeviceMode Mode);

    /// <summary>在已枚举设备里匹配型号目录。优先 MI_02 接口。</summary>
    public static Located? Find(IReadOnlyList<MouseModelInfo> models, IReadOnlyList<HidDeviceInfo>? devices = null)
    {
        devices ??= HidDeviceEnumerator.Enumerate();

        foreach (var model in models)
        foreach (var mode in model.Modes)
        {
            var matches = devices.Where(d => d.VendorId == mode.Vid && d.ProductId == mode.Pid).ToList();
            if (matches.Count == 0) continue;

            // 复合设备：优先取 MI_02 接口；否则取首个。
            var dev = matches.FirstOrDefault(d => d.MatchesInterface(2)) ?? matches[0];
            return new Located(dev, model, mode);
        }
        return null;
    }

    /// <summary>轻量存在性检测：仅扫描接口路径字符串(不打开设备)，判断目标鼠标是否插着。用于插拔轮询。</summary>
    public static bool IsPresent(IReadOnlyList<MouseModelInfo> models, IReadOnlyList<string>? paths = null)
    {
        paths ??= HidDeviceEnumerator.EnumerateInterfacePaths();
        foreach (var model in models)
        foreach (var mode in model.Modes)
        {
            string vid = $"vid_{mode.Vid:x4}";
            foreach (var p in paths)
            {
                string lp = p.ToLowerInvariant();
                if (lp.Contains(vid) && lp.Contains($"pid_{mode.Pid:x4}")) return true;
            }
        }
        return false;
    }
}
