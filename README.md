# Inphic Mouse — 现代化 WinUI3 上位机（重写版）

为 **Inphic IN9 系列无线/有线鼠标**（原厂配置软件 `Inphic Mouse 1.0.2.8`）重写的现代化 WinUI 3 界面。
目标：**只改界面、不改驱动**——在 C# 中 1:1 复刻原软件与鼠标通信的 HID 协议，保证发给鼠标的字节与原厂一致。

> 原厂软件是 MFC 纯用户态程序，通过 HID API（`SetupDi*` 枚举 + hidapi 的 `hid_write`/`hid_read`）直接与鼠标通信，**没有内核驱动**。本项目在协议层逐字节复刻，不涉及任何驱动安装。

---

## ⚠️ 测试范围（重要）

**本项目仅在一只「Inphic IN9 升级版」鼠标（USB VID_248A / 2.4G VID_249A，ic_type 0x11）上实测过。**

- 其它 Inphic 型号 / 其它固件版本**没有实机验证**：协议参数（DPI 寄存器换算、灯光模式 id、回报率码、按键数量）可能不同，**可能不工作甚至写坏设置**；
- 使用前请先记下原厂软件里的原始设置，必要时可用原厂软件恢复默认；
- 所有"应用"操作都是**直接写入鼠标**，请自行评估风险。

---

## 1. 功能一览

| 页面 | 功能 | 状态 |
|------|------|------|
| 设备信息 | 型号 / 连接 / 模式(USB·2.4G) / 电量 / 固件 / 传感器 IC；刷新、重连；实机诊断（逐条读 0x10~0x20 原始字节 + 解码，可一键复制） | ✅ 可用 |
| DPI | **档位数 1–6**、各档 DPI 值 + 每档 RGB 颜色、当前档；应用 / 恢复默认；物理 DPI 键切换实时跟随 | ✅ 可用 |
| 回报率 | 125/250/500/1000 Hz（码↔Hz 已定死：码=1000/Hz） | ✅ 可用 |
| 灯光 | 模式（关闭/呼吸/常亮/霓虹…按型号启用项）、亮度(1–4)、速度(1–4)、常亮颜色；按模式自动隐藏无关控件 | ✅ 可用 |
| 鼠标按键 | 为每个物理键分配：鼠标功能 / DPI / 多媒体 / 宏 / 关闭 / 键盘快捷键（组合键捕获）；应用前"无左键"强制警告 | ✅ 可用 |
| 快捷指令（宏） | 宏编辑器：宏列表（可拖动排序 / 双击改名）、事件列表（可拖动排序 / 双击编辑 / 上下移动 / 动画）、录制/插入、延时与循环设置；单个导出/导入 JSON；本地保存 | ✅ 可用（0x08 线格式已用官方 USB 抓包校准） |
| 设置 | 休眠时间（分钟）、移动唤醒、移动关灯、按键响应、抬升高度、传感器 flag；**主题（跟随系统 / 亮色 / 暗色）** | ✅ 可用 |

---

## 2. 下载与安装

到 **[Releases](../../releases)** 页面下载：

| 文件 | 说明 |
|------|------|
| `InphicMouse-Setup.msi` | **安装包**（推荐）。带安装向导，**可以自定义安装目录**，会创建开始菜单 + 桌面快捷方式，可正常在「设置 → 应用」里卸载。默认装在 `%LOCALAPPDATA%\Programs\InphicMouse`（免管理员）。 |
| `InphicMouse-portable.zip` | **免安装版**。解压到任意目录，双击 `InphicMouse.App.exe` 即用；配置保存在程序目录。 |

安装包使用（可选，命令行）：

```powershell
# 静默安装到默认目录
msiexec /i InphicMouse-Setup.msi /qn
# 静默安装到自定义目录
msiexec /i InphicMouse-Setup.msi /qn INSTALLFOLDER="D:\Apps\InphicMouse"
# 静默卸载
msiexec /x InphicMouse-Setup.msi /qn
```

> 安装包与免安装版都是 **自包含** 的，目标机**不需要装 .NET / Windows App SDK**。
> 程序数据（`macros.json` / `settings.json`）优先放在程序目录；若程序目录不可写（例如装在 `Program Files`），会自动改用 `%APPDATA%\InphicMouse`。

---

## 3. 工程结构

```
InphicMouse/
├─ src/
│  ├─ InphicMouse.Hid/         HID 设备枚举与读写（SetupDi* + ReadFile/WriteFile）
│  ├─ InphicMouse.Protocol/    协议层（纯类库，可 dotnet test）
│  │  ├─ MouseReport.cs            33 字节帧 + 校验和
│  │  ├─ KeyCodes.cs               VK/HID usage ↔ 修饰位；VkToHidUsage 表
│  │  ├─ Codecs/
│  │  │  ├─ DpiCodec.cs            0x03 DPI 档位 / 0x0C 复位
│  │  │  ├─ DpiConversion.cs       DPI↔寄存器换算（各 ic_type）
│  │  │  ├─ LightCodec.cs          0x05 灯光模式
│  │  │  ├─ DeviceSettingsCodec.cs 0x02 回报率 / 0x04 DPI色 / 0x06 传感器 / 0x07 休眠
│  │  │  ├─ DeviceInfoCodec.cs     0x10 设备信息解析
│  │  │  ├─ KeyMapCodec.cs         0x09 按键映射 + KeyEntry 工厂
│  │  │  ├─ KeyFunctionCatalog.cs  快捷指令功能目录（鼠标/DPI/多媒体/宏/关闭）
│  │  │  └─ MacroCodec.cs          0x08 宏内容上传（分帧 + 事件编码）
│  │  ├─ Comm/MouseCommLink.cs     写+ack+读回（复刻 DriverComm::SetDeviceData）
│  │  └─ Services/
│  │     ├─ IMouseService.cs       高层服务抽象
│  │     ├─ RealMouseService.cs    真机实现（含插拔监视、读回、串行化、诊断）
│  │     ├─ SimulatedMouseService.cs 无设备演示
│  │     ├─ DeviceLocator.cs       按 VID/PID + MI_02 定位
│  │     └─ MouseState.cs          MouseSettings / MouseStatus / ColorUtil
│  ├─ InphicMouse.Core/        型号元数据（解析原厂 config.xml / device xml）
│  │  ├─ Metadata/                 MetadataProvider / ConfigXmlLoader / DeviceXmlLoader
│  │  └─ Models/                   DeviceCapabilities / DpiStage / LightModeInfo …
│  └─ InphicMouse.App/         WinUI3 界面（MVVM + DI）
│     ├─ App.xaml.cs               DI 装配 / 主题 / 窗口图标
│     ├─ AppPaths.cs               数据文件位置（便携 / 不可写时回退 %APPDATA%）
│     ├─ MainWindow.xaml           顶部状态栏 + NavigationView
│     ├─ ViewModels/               各页 VM + MacroStore（宏本地存储）
│     └─ Views/                    各页 XAML
├─ tests/InphicMouse.Protocol.Tests/   协议层单测（91 全绿）
├─ docs/
│  ├─ hid-protocol.md          完整 HID 协议还原文档
│  └─ NEXT-STEPS.md            下一步计划 / 已知 bug / 测试清单
└─ scripts/
   ├─ publish-portable.ps1     一键打便捷版（自包含 + Metadata + 语言精简 + zip）
   ├─ build-installer.ps1      生成 MSI 安装包（WiX）+ 脚本版安装包
   ├─ installer/               安装/卸载脚本（脚本版安装包用）
   ├─ verify-ui.ps1            启动 + 主窗口截图
   └─ verify-pages.ps1         逐页导航截图
```

---

## 4. 构建与运行

### 环境
- Windows 10/11 x64
- .NET 9 SDK
- **VS Build Tools 的 MSBuild**（WinUI 工程必需；路径示例 `C:\BuildTools\MSBuild\Current\Bin\MSBuild.exe`）
- Windows App SDK（WinUI 3）
- （可选，打包安装包）`dotnet tool install --global wix --version 5.*` + `wix extension add -g WixToolset.UI.wixext/5.0.2`

### 构建 App（必须用 MSBuild，不能用 dotnet build）
> dotnet SDK 的 MSBuild 缺 AppxPackage 的 `Microsoft.Build.Packaging.Pri.Tasks.dll`，会报 MSB4062。

```powershell
& "C:\BuildTools\MSBuild\Current\Bin\MSBuild.exe" `
  src\InphicMouse.App\InphicMouse.App.csproj `
  /t:Restore,Build /p:Configuration=Debug /p:Platform=x64
```

产物：`src\InphicMouse.App\bin\x64\Debug\net9.0-windows10.0.19041.0\win-x64\InphicMouse.App.exe`

### 纯类库（Hid / Protocol / Core）与单测
```powershell
dotnet build src\InphicMouse.Protocol\InphicMouse.Protocol.csproj
dotnet test  tests\InphicMouse.Protocol.Tests
```

### 打便捷版（免安装自包含 + zip）
```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-portable.ps1
```
产物：`artifacts\InphicMouse-portable.zip`

### 打安装包（MSI + 脚本版）
```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```
产物：`artifacts\InphicMouse-Setup.msi`、`artifacts\InphicMouse-Setup\`

---

## 5. HID 协议要点（详见 `docs/hid-protocol.md`）

- **传输**：Output/Input Report，发送固定 **33 字节**（`hid_write`），读 30 字节（`hid_read_timeout`）。
- **帧布局**：`[0]=报告ID(0) [1]=命令码 [2]=0x00 [3]=0x01 [4]=载荷长度 [5..31]=载荷 [32]=校验和`，校验和 = `sum(buf[5..31]) & 0xFF`。
- **读回剥 report id**：Windows 原始 ReadFile 首字节是 report id(0)，原程序经 hidapi 已剥掉；本实现统一剥掉再按逆向偏移解析。
- **命令码**：0x02 回报率 / 0x03 DPI / 0x04 DPI色 / 0x05 灯光 / 0x06 传感器 / 0x07 休眠 / 0x08 宏内容 / 0x09 按键映射 / 0x0C-0x0E 各复位 / 0x10-0x17 读回 / 0x20 无线固件。

### 实机已校准（IN9 升级版 / ic 0x11）
- **DPI**：ic 0x11，寄存器换算已对齐官方；档位数 1–6。
- **灯光**：关闭=0 呼吸=2 常亮=3 霓虹=4；颜色类（2/3）单帧下发；亮度有效 1–4、速度 1–4。
- **回报率**：码=1000/Hz（1↔1000, 2↔500, 4↔250, 8↔125）。
- **固件**：0x0142 按十六进制显示 "1.42"。
- **休眠**：原始值 = 分钟 × 6。

### 宏数据模型（来自官方 SQLite `mouse_in9(3311)_data1.db`）
- `t_macrorecord`：**type** 1=延时 2=键盘按下 3=键盘松开 4=鼠标按下 5=鼠标松开；**value** 键盘=Windows VK（A=65）、鼠标=按钮位（左1/右2/中4）、延时=ms。延时是独立事件。
- 键绑宏：`t_key_macro_data` 的 `macro_type=4, macro_value=macro_id` → wire 0x90；**宏槽 = 键位下标 key_code**，v1 低半字节 = key_code+1。
- ✅ **0x08 每动作 4 字节 = `[动作类型, 延时低, 延时高, 键值]`**（DB 延时记录并入前一动作，动作间最小 1ms）。已用官方 USB 抓包逐字节校准（见 `docs/NEXT-STEPS.md` 与 `MacroCodec`）。

---

## 6. 已知限制

- **仅实测过 Inphic IN9 升级版**一只鼠标；其它型号/固件请谨慎尝试。
- 宏下发格式已按官方抓包校准（`MacroCodec`），真机触发效果建议自行复测。
- 安装包/免安装版均**未做代码签名**，部分机器的 Smart App Control / SmartScreen 可能拦截，需要手动"仍要运行"。

---

## 7. 逆向与校准资料

- 协议还原文档：`docs/hid-protocol.md`（含各命令字节布局与逆向依据）。
- 官方配置数据库样本：`mouse_in9(3311)_data1.db`（SQLite，含灯光/宏/DPI 实际存储）。
- 校准方法：用 Wireshark + USBPcap 抓原厂软件的下发报文，对照 `MacroCodec` / 各 Codec 逐字节比对。

## 社区
感谢 [LINUX DO](https://linux.do) 社区提供开放、友善的技术交流平台
