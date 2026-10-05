# Inphic IN9 鼠标 HID 协议（逆向还原）

> 来源：Ghidra 反编译 `Inphic Mouse.exe`（含完整 PDB 符号）。核心类 `DriverComm`（通信）、`ProtocolData`（编码）、`KeyboardDB`（状态）。
> 反编译产物：`C:\Users\Rui_Shu\InphicRE\decomp_core.c` / `decomp_callers.c` / `decomp_dpi.c`。

## 传输层（已确认）

底层为 **hidapi**（结构体 `hid_device_`），使用 **Output/Input Report**（非 Feature Report）：
- 发送：`hid_write(dev, buf, 33)` → Output Report
- 读回：`hid_read_timeout(dev, buf, 0x1e/30, timeout)` → Input Report

`DriverComm::SetDeviceData(uchar* buf, uint len=0x21, bool)`（@0x4528e0）：
1. 计算校验和写入 `buf[0x20]`，`hid_write` 发送 33 字节；
2. 循环 `hid_read_timeout` 读响应，**响应 `resp[1] == 0` 视为成功**，最多重试 **50 (0x32)** 次；
3. 返回是否成功；成功时把响应就地 memcpy 回同一缓冲区（读命令即在此解析）。

`DriverComm::GetDeviceInfo`（@0x452790）：命令 **0x10**，重试上限 0x31，`read>0 且 resp[1]==0` 为成功。

## 报文布局（下发，固定 33 字节 = 0x21）

```
偏移  0   | 1     | 2  3  4        | 5 .............. 31        | 32
     报告ID| 命令码 | 头部(0x00,0x01,len) | 载荷(27B,参与校验)     | 校验和
```
- `buf[0]`：Report ID（Output Report 首字节，恒 0）。
- `buf[1]`：命令码。
- `buf[2]=0x00`、`buf[3]=0x01`：固定头。
- `buf[4]`：**载荷字节数**（从 buf[5] 起的有效字节数；DPI 命令为特例，见下）。
- `buf[5..31]`：数据区（参与校验和）。
- `buf[32]`：`checksum = sum(buf[5..31]) & 0xFF`（在 `SetDeviceData` 内计算，各下发函数不含）。

## 命令码表（buf[1]）

写/复位命令与其对应的读回命令相差 0x10（0x0X 写 → 0x1X 读）：

| buf[1] | 方向 | 功能 | 下发/解析函数 |
|--------|------|------|---------------|
| 0x02 | 写 | 回报率 | `SetMouseReportRateData` |
| 0x03 | 写 | DPI 档位 | `SetMouseDPIData` |
| 0x04 | 写 | DPI 各档颜色 | `SetMouseDPIColorData` |
| 0x05 | 写 | 灯光模式 | `SetLightModeData` |
| 0x06 | 写 | 传感器/抬升高度 | `SetMouseSensorLiftData` |
| 0x07 | 写 | 休眠/移动唤醒 | `SetMouseSleepData` |
| 0x08 | 写 | 宏内容上传 | `SetMouseKeyMacroData`(macro) |
| 0x09 | 写 | 按键映射 | `SetMouseKeyMacroData` |
| 0x0C | 复位 | 恢复默认 DPI | `SetMouseResetDPI` |
| 0x0D | 复位 | 恢复默认回报率(第1帧) | `SetMouseResetReport` |
| 0x0E | 复位 | 恢复默认灯光(第1帧) | `SetMouseResetLight` |
| 0x0F | 复位 | 恢复出厂 | — |
| 0x10 | 读 | 设备信息(电量/固件/IC/在线) | `GetDeviceInfo` |
| 0x11 | 读 | 读会话握手(不解析) | `GetMouseData` |
| 0x12 | 读 | 回报率 | `GetMouseData` |
| 0x13 | 读 | DPI 档位 | `GetMouseData` |
| 0x14 | 读 | DPI 各档颜色 | `GetMouseData` |
| 0x15 | 读 | 灯光模式(复位灯光第2帧亦用) | `GetMouseData` |
| 0x16 | 读 | 传感器/抬升 | `GetMouseData` |
| 0x17 | 读 | 休眠/移动唤醒 | `GetMouseData` |
| 0x20 | 读 | 无线(2.4G) 固件版本 | `GetMouseData` |
| 0x52 | 读 | 原始读('R') | — |

## 0x03 DPI 档位（`SetMouseDPIData`，已完整还原）

- `buf[1]=0x03`, `buf[2]=0x00`, `buf[3]=0x01`, `buf[4]=0x25`
- `buf[5]` = `(当前DPI档 << 4) | DPI档数`
- `buf[6..]` 每档 **4 字节** `reg_lo, reg_hi, reg_lo, reg_hi`（X/Y 相同值，小端），依次 DPI1..DPIn（6 档占 `buf[6..29]`）
- 每档寄存器值 `reg = GetMouseDPIValue(ic_type, dpi)`（16 位），DPI 先裁剪到 `[50, 26000]`

### DPI → 寄存器换算（`GetMouseDPIValue(ic_type, dpi)`，@0x41ee80）
所有除法为向下取整整数除法。`ic_type` 来自设备查询 `dev_info[0x0A]`（见 0x10）。

| ic_type | 换算 |
|---------|------|
| 0x11 (17) | dpi>12999: `(dpi-13000)/1000 + 221`；dpi<10001: `dpi/50`；否则 `(dpi-10000)/100 + 200` |
| 0x05 (5) | dpi<6401: `dpi/50`；否则 `(dpi-6400)/100 + 128` |
| 0x25 (37) | dpi>5000: `(dpi-5000)/500 + 50`；否则 `dpi/100` |
| 0x26 (38) | dpi<5001: `dpi/100 - 1`；否则 `(dpi-5000)/500 + 50` |
| 0x31 (49) | dpi>12000: `(dpi-12000)/1000 + 120`；否则 `dpi/100` |
| 4, 0x10, 0x12, 0x35 | `dpi/100` |
| 其它(默认) | `dpi/50 - 1` |

逆函数 `GetDPIValueByMouseDPI`（@0x41efe0）用于读回时把寄存器值转回 DPI。

## 0x02 回报率（`SetMouseReportRateData`）
- `buf[1]=0x02`, 头 `0x00,0x01,0x01`
- `buf[5]` = 回报率**档位码**（配置键 `report_rate`，出厂默认码 "2"；无线用 `report_rate_wireless` 默认 "1"）。码→Hz(125/250/500/1000) 的对应表在 UI 资源里，反编译不可见，**需实机抓包确认**。

## 0x04 DPI 各档颜色（`SetMouseDPIColorData`）
- `buf[1]=0x04`, 头 `0x00,0x01,0x12`(=18=6档×3)
- `buf[5..22]` = 6 档 RGB，每档 3 字节，取自配置 int：`v&0xFF, (v>>8)&0xFF, (v>>16)&0xFF`（若为 COLORREF 0x00BBGGRR 则为 R,G,B 序）。

## 0x05 灯光模式（`SetLightModeData`）
- `buf[1]=0x05`, `buf[2]=0x00`, `buf[3]=0x01`, `buf[4]=载荷长度`（随模式变）
- `buf[5]` = 模式 id（0..5）
- `buf[6]` = `(亮度<<4) | 速度`（各 0..15，亮度高半字节、速度低半字节）
- 按模式：
  - id 0/4/5 → `buf[4]=2`（仅 buf[5],buf[6]）——效果/关闭类
  - id 1 → `buf[4]=3`（buf[7]=0）
  - id 3 → `buf[4]=5`：`buf[7..9]`=单色 RGB —— **常亮**
  - id 2 → `buf[4]=0x18`(24)：`buf[7]=0x07`(色数)，`buf[8..28]`=7 组 RGB —— **七彩波浪**
- id 0/1/4/5 与 流光/呼吸/霓虹/关闭 的具体对应在 UI 资源，反编译不可判定，**需实机核对**。

## 0x06 传感器/抬升高度（`SetMouseSensorLiftData`）
- `buf[1]=0x06`, 头 `0x00,0x01,0x05`
- `buf[5]` = `liftoff_height`；`buf[6]` = `(sensor_flag & 0x08)?1:0`；`buf[7]` = `(sensor_flag & 0x04)?1:0`

## 0x07 休眠/移动唤醒（`SetMouseSleepData`）
- `buf[1]=0x07`, 头 `0x00,0x01,0x04`
- `buf[5]`=`sleep_light`(休眠超时) `buf[6]`=`move_wakeup` `buf[7]`=`move_closelight` `buf[8]`=`button_respondtime`

## 0x0C DPI 复位（`SetMouseResetDPI`）
- `buf[1]=0x0C`, 头 `0x00,0x01,0x01`, `buf[5]=0x01`

## 复位灯光/回报率（各发两帧）
- 灯光：帧1 `buf[1]=0x0E`(头 `0x00,0x01,0x01`,buf[5]=1) → Sleep(100) → 帧2 `buf[1]=0x15`(读回当前灯光)
- 回报率：帧1 `buf[1]=0x0D`(头 `0x00,0x01,0x01`,buf[5]=1) → Sleep(100) → 帧2 `buf[1]=0x12`(读回并写入配置)

## 0x09 按键映射（`SetMouseKeyMacroData`）
- `buf[1]=0x09`, 头 `0x00,0x01,0x0F`(=15=5键×3)
- 载荷为按物理键索引排列的 3 字节条目：`buf[5 + key*3 .. +2]` = `[功能类型字节, v1, v2]`
- 功能类型半字节：`0x10`=鼠标键 `0x20`=滚轮/特殊 `0x30`=连发 `0x40`=DPI `0x60`=系统/配置 `0x70`=键盘键 `0x80`=多媒体 `0x90`=宏
- 键盘键(0x70)：`v1`=修饰键掩码(`macro_value2`)，`v2`=`GetSysKeyCode(usage)`；若 `macro_value2==0` 且 value∈0xE0..0xE7，则 `v1`=修饰位、`v2`=0
- 宏(0x90)：`v1`=宏槽(key+1)，按重复模式 `|0x10/0x40/0x20`（0x20 时 `v2`=重复次数）
- 完整分支见 `decomp_callers.c` @0x438090。

## 0x08 宏内容上传（`SetMouseKeyMacroData` 重载 @0x438b80）
- 头字 `0x01000800` → `buf[1]=0x08`,`buf[2]=0x00`,`buf[3]=0x01`；`buf[4]`=`事件数*4+1`
- `buf[5]`=宏索引+1，`buf[6..]`=宏事件（每事件 4 字节）
- 超过 6 事件时分块：每块头 `buf[3]=(块序<<4)|总块数`，`buf[4]=0x1B`(整块)/余数(末块)

## 修饰键编码（`ProtocolData::GetSysKeyCode` @0x4551e0）
Windows VK/HID → 修饰位掩码：LCtrl 0xE0/0xA2→0x01, LShift 0xE1/0xA0→0x02, LAlt 0xE2/0xA4→0x04, LGui 0xE3/0x5B→0x08, RCtrl 0xE4/0xA3→0x10, RShift 0xE5/0xA1→0x20, RAlt 0xE6/0xA5→0x40, RGui 0xE7/0x5C→0x80；0→0；其余原样返回（视作已是 HID usage）。
`GetFlashKeyCode`（@0x454bb0）：VK→HID Usage 表（A–Z:0x41-0x5A→0x04-0x1D；1–9:0x31-0x39→0x1E-0x26；0→0x27；Enter→0x28；Esc→0x29；退格→0x2A；Tab→0x2B；空格→0x2C；及标点表），用于宏/flash 键录制。

## 0x10 设备信息 / 电量（`GetDeviceInfo` → `GetMouseData` 解析 `dev_info`）
- `dev_info[8..9]` → 固件版本 `byte[9]<<8 | byte[8]`
- `dev_info[0x0A]` → ic_type（决定 DPI 换算与上下限）
- `dev_info[0x0B]` → 回报率档数上限（0x10→5,0x20→6,0x40→7,否则4）
- `dev_info[0x0C]` → 充电标志；`dev_info[0x0D]` → 电量百分比；`dev_info[0x0E]` → 在线标志
- IC 对应 DPI 上下限：0x11/0x05→max12000, 4/0x10/0x12→max4800, 0x25→max10000, 0x26→200-10000, 0x31→max24000, 0x35→max16000, 默认 50-26000

## 读回（`GetMouseData`）
连接后依次发 0x11(握手,不解析)→0x12(回报率)→0x13(DPI)→0x14(DPI色)→0x15(灯光)→0x16(传感器)→0x17(休眠)→[2.4G 时] 0x20(无线固件)。
- 0x13 DPI：`dpi_flag/dpi_count/dpi_index` + 6 档值，小端 uint16、4 字节步进：g0=buf[5..6], g1=buf[9..10], g2=buf[13..14], g3=buf[17..18], g4=buf[21..22], g5=buf[25..26]；每值经 `GetDPIValueByMouseDPI` 反算（>0x31 有效）
- 0x14 DPI色：g0=buf[4..6], g1=buf[7..9], g2=buf[10..12]...（每档 3 字节 RGB）
- 0x15 灯光：`buf[4]`=当前模式 id；`buf[5]` 低半字节=亮度、高半字节=速度
- 0x16：`liftoff_height`,`sensor_flag`；0x17：`sleep_light`,`move_wakeup`,`move_closelight`,`button_respondtime`

## 异步通知（`GetDeviceNotify` @0x452a10）
非请求/响应：`hid_read_timeout` 读 0x41 字节输入报文。魔数 `resp[0]==0xC0` 才是有效通知；`resp[1]==1`=设备在线，否则离线。在线时会重新 `GetDeviceInfo` 刷新。

## 设备枚举/定位
按 config.xml 的 VID/PID + 接口 `MI_02` 定位；USB=VID_248A，2.4G=VID_249A。

## 待实机确认（反编译不可判定）
1. 回报率档位码 ↔ Hz 的对应表（125/250/500/1000）。
2. 灯光模式 id 0/1/4/5 ↔ 流光/呼吸/霓虹/关闭 的对应。
3. DPI 命令 `buf[4]=0x25` 的确切含义（不等于载荷字节数，按原样字节复刻即可）。
4. 按键映射为 5 键（`buf[4]=0x0F`），而型号元数据为 6 键，需核对是否有一键固定不可改。

