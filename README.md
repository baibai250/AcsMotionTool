# ACS 运动控制调试工具（SPiiPlus .NET）

一个可以直接控制 ACS（ACS Motion Control / SPiiPlus）运动控制器的 Windows 桌面程序：
支持以太网连接真实控制器，也支持 SPiiPlus 模拟器（无硬件即可练习与验证）；
**启动时有初始化界面**（显示「ACS 初始化成功」），**轴软限位可存入 MySQL 数据库**。
工程参考了 `E:\test\AB1`（公司 AOI 项目）中 `JZNB.AOI.MotionIo` / `JZNB.AOI.Service\Motion` 的 ACS 用法，
但做了重新设计：MVVM 分层、通讯串行化、轮询静默化、软限位拦截 + 数据库持久化、日志/自检完整。

---

## 1. 功能一览

| 页签 / 界面 | 功能 |
| --- | --- |
| **初始化界面（启动时）** | 分步显示：加载本地配置 → 初始化 MySQL（自动建库建表）→ 连接 ACS 控制器 → 从数据库加载轴软限位 → 完成。**连接成功时明确显示「ACS 初始化成功」**；失败时显示原因，可「重新初始化」「用模拟器重试」「跳过并进入(离线)」 |
| **连接** | 以太网（UDP 700 / TCP 701）或 SPiiPlus 模拟器连接；控制器信息（固件/序列号/轴数/缓冲区/库版本）；控制器重启；参数保存到 Flash；轴数量设置；**数据库连接参数与测试/初始化** |
| **轴控制** | 轴状态表（位置/命令位置/目标位置/速度/使能/到位/限位/**终止码**/报警，运动=绿、真故障=红）；单轴使能/下使能/清故障/停止/急停；Jog 正反转；绝对/相对定位（可选速度、加速度、减速度、等待到位）；编码器/命令位置设定；**回零（三种方式，见第 10 节）**；运动参数读写；**轴改名与软限位（可一键存入/读取 MySQL）** |
| **IO** | 数字输入位实时监视；数字输出位点击开关；整端口写入（支持 `0x` 十六进制）；扩展输出；模拟量读写 |
| **变量** | 任意 ACSPL+ 变量读写（`FPOS`、`MST(0)`、`IN(0)`、`OUT(0)`、`MyArr(0)`、`MyMat(0,1)` 均可）；声明持久变量（`MyArr(10)`、`MyMatrix(3)(4)`）；监视列表周期刷新 |
| **命令终端** | 直接下发 ACSPL+ 命令并显示应答（快捷按钮：`?FPOS(0)`、`?MST(0)`、`?RPOS(0)`、`?APOS(0)`、`?VEL(0)`、`?ACC(0)`、`?IN(0)`、`?OUT(0)`、`?SYSINFO(2)` 等）；“下发(不等应答)”用于动作类命令 |
| **程序缓冲区** | 上传/下载/编译/运行/停止控制器内的 ACSPL+ 程序缓冲区（回零程序就在这里）；可插入回零模板 |
| **日志** | 全操作日志（含 SDK 异常原文）、自动滚动、保存到文件 |

另外提供 **命令行自检模式**（见第 11 节），一条命令即可判断现场环境能否正常连机、初始化、存库与运动。

---

## 2. 代码架构（MVVM）

界面层严格按 MVVM 分层，**View 里没有任何业务逻辑，也没有 Click 事件处理器**：

```
View (XAML 只做绑定)
   │  DataContext
   ▼
ViewModel (命令 + 可绑定状态，不引用 MessageBox/Dispatcher/UI 控件/MySql)
   │  依赖抽象接口
   ▼
Service (IAcsService / ISoftLimitStore / ILogService / IAppSettingsService / IDialogService / IUiDispatcher)
   │
   ├─► Core (Model: AcsController / AcsAxis / AcsIo / AcsVariables / AcsTerminal / AcsProgram)
   └─► Data (Model: MySqlSoftLimitRepository，唯一接触 MySqlConnector 的地方)
```

| 关注点 | 做法 |
| --- | --- |
| 按钮/输入 | 一律 `Command="{Binding XxxCommand}"`（`RelayCommand` / `AsyncRelayCommand`，执行期间自动禁用防重复点击；异步命令的异常统一交给日志，不会抛到 UI 线程外） |
| 线程 | ViewModel 完全不碰 `Dispatcher`：`AcsService` 内部把阻塞调用放到后台线程，并把**所有事件切回 UI 线程**后才通知 ViewModel；界面进程注入 `WpfUiDispatcher`，测试注入 `ImmediateUiDispatcher` |
| 弹窗/文件对话框 | 全部走 `IDialogService`（界面进程用 `WpfDialogService`，测试用桩实现），ViewModel 里没有 `MessageBox` |
| 列表自动滚动 | 附加行为 `AutoScrollBehavior.IsEnabled`，不需要在 View 代码后置写 `ScrollIntoView` |
| 跨页协作 | 由 `MainViewModel` 组合并显式转发（连接页“应用轴数量” → 轴页重建列表），不靠静态单例 |
| 组合根 | `AppHost.Create()` 手工构造服务与各页 ViewModel（零第三方容器）；初始化界面与主界面共享同一套实例 |
| 数据库隔离 | ViewModel 只依赖 `ISoftLimitStore`（返回普通 record），SQL、建库建表、错误码翻译都在 `AcsMotionTool.Data` 里 |
| 可测试性 | ViewModel 不依赖 WPF 具体类型（调度器、对话框均为接口），可在无界面环境下直接驱动 |

---

## 3. 目录结构

```
E:\test\acs\
├─ AcsMotionTool.sln                  解决方案（3 个项目，兼容旧版 Visual Studio）
├─ AcsMotionTool.slnx                 同一解决方案的新格式（.NET 10 SDK / 新版 VS 用）
│                                     注意:它由 .sln 生成，**加/删项目后要重新生成**保持同步：
│                                     dotnet sln AcsMotionTool.sln migrate
├─ Directory.Build.props              统一配置：ACS SDK 引用位置、sb4.exe 复制、语言版本
├─ README.md                          本文档
├─ publish\                           已发布的可直接运行版本（AcsMotionTool.exe）
├─ lib\
│  ├─ v4.0.0-net8\ACS.SPiiPlusNET8.dll       默认使用的 SDK（.NET 8 版，4.0.0）
│  ├─ v3.3.0-netfx\ACS.SPiiPlusNET.dll       备用：与 AB1 公司代码一致的 3.3.0 版本
│  ├─ simulator\sb4.exe                      SPiiPlus 模拟器可执行文件（自动复制到输出目录）
│  └─ api-dumps\                             两个 SDK 版本的 API 反射清单（便于查签名）
└─ src\
   ├─ AcsMotionTool.Core\              Model 层：通讯与控制核心库（无 WPF / 无 MySql 依赖）
   ├─ AcsMotionTool.Data\              Model 层：MySQL 数据访问（软限位仓储、建库建表、审计表）
   └─ AcsMotionTool.App\               MVVM 界面
      ├─ Infrastructure\               ViewModelBase / RelayCommand / IUiDispatcher / IDialogService / 转换器与行为
      ├─ Services\                     IAcsService(轮询与全部操作) / ISoftLimitStore / ILogService / IAppSettingsService
      ├─ ViewModels\                   Initialization / Main / Connection / Axis / Io / Variable / Terminal / Program / Log
      ├─ Views\                        8 个 View（7 个 UserControl + 初始化窗口），代码后置只有 InitializeComponent
      ├─ MainWindow.xaml               外壳：TabControl + 状态栏
      ├─ AppHost.cs                    组合根：手工依赖注入
      └─ App.xaml(.cs)                 启动流程(初始化界面→主界面) + 命令行自检入口
```

Model 层文件与职责：

| 文件 | 职责 |
| --- | --- |
| `Core\AcsController.cs` | 门面：连接/断开/重启、控制器信息、轴对象管理、全部轴批量操作、**所有 SDK 调用串行化** |
| `Core\AcsAxis.cs` | 单轴：状态读取、使能、Jog、绝对/相对运动、停止/急停、回零、位置设定、参数读写、软限位拦截 |
| `Core\AcsIo.cs` | 数字输入/输出位与端口、扩展 IO、模拟量 |
| `Core\AcsVariables.cs` | ACSPL+ 变量读写（支持 `MST(0)` 这类下标写法）、数组、声明变量 |
| `Core\AcsTerminal.cs` | 终端命令（`Transaction` / `RawTransaction` / `Command` / `Receive`） |
| `Core\AcsProgram.cs` / `AcsProgramText.cs` | 程序缓冲区读写编译运行、程序文本 ASCII 清洗与 HOME/置零模板 |
| `Core\AcsMotionErrors.cs` / `AcsHoming.cs` | 运动终止码判定与中文解释、回零参数 |
| `Core\AcsSettings.cs` | `acs-settings.json` 配置（连接参数、**数据库参数**、轴名、软限位、监视变量） |
| `Core\AcsSelfTest.cs` | 环境自检流程与报告 |
| `Data\MySqlSoftLimitRepository.cs` | 软限位读写（参数化 SQL、事务内写审计表、MySQL 错误码翻译成中文） |
| `Data\DatabaseSchema.cs` | `axis_soft_limit` / `axis_soft_limit_history` 的建表 SQL（幂等） |
| `Data\AxisSoftLimitRecord.cs` | 软限位记录 / 连接测试结果 / 初始化结果等 DTO 与仓储接口 |

---

## 4. 环境要求

* Windows + .NET 8 桌面运行时（本机已安装 8.0.31，`Microsoft.WindowsDesktop.App`）
* ACS 端：**SPiiPlus UMD / ACS Communication Service**（随 SPiiPlus Runtime Kit 安装）
  * 连真实控制器：控制器上电、网线可达、IP 正确
  * 用模拟器：输出目录里必须有 `sb4.exe`（本项目已通过 `Directory.Build.props` 自动从
    `C:\Program Files (x86)\ACS Motion Control\SPiiPlus Runtime Kit\User Mode Driver\sb4.exe` 复制，
    找不到时回退到 `lib\simulator\sb4.exe`）
* 数据库：**MySQL 8.x**（本机实测 MySQL 8.0.46，`127.0.0.1:3306`，账号 `root/123456`）
  * 只需要有建库建表与读写权限；库名默认 `acs_motion`，初始化时自动创建
  * 若现场没有 MySQL，可在“连接”页取消“启用数据库”，程序仍可使用（软限位仅保存在本地设置文件）

---

## 5. 数据库（MySQL）：轴软限位持久化

### 5.1 连接参数

默认值已按现场本机 MySQL 预填（可在“连接”页修改，保存在 `acs-settings.json`）：

| 项 | 默认值 |
| --- | --- |
| 服务器 / 端口 | `127.0.0.1` / `3306` |
| 用户 / 密码 | `root` / `123456` |
| 数据库名 | `acs_motion` |
| 应用软限位时自动写库 | 开 |
| 启动时从数据库加载 | 开 |

> 注意：密码以明文保存在 `acs-settings.json`（与程序同目录）。若现场有安全要求，请限制该文件的访问权限，
> 或改用只具备 `acs_motion` 库权限的专用账号，不要使用 root。

### 5.2 表结构（初始化时自动创建，可重复执行）

```sql
-- 软限位主表：每个轴一行
CREATE TABLE IF NOT EXISTS `axis_soft_limit` (
    `axis_index`     INT           NOT NULL COMMENT '轴号(从 0 开始)',
    `axis_name`      VARCHAR(64)   NOT NULL DEFAULT '' COMMENT '轴名称，如 X/Y/Z/R',
    `soft_limit_min` DOUBLE        NULL COMMENT '软限位下限，NULL 表示该方向不检查',
    `soft_limit_max` DOUBLE        NULL COMMENT '软限位上限，NULL 表示该方向不检查',
    `updated_at`     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    `updated_by`     VARCHAR(64)   NOT NULL DEFAULT '' COMMENT '最后修改人(主机名\用户名)',
    PRIMARY KEY (`axis_index`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 审计表：每次修改留痕（写主表与审计表在同一事务里）
CREATE TABLE IF NOT EXISTS `axis_soft_limit_history` (
    `id`             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    `axis_index`     INT           NOT NULL,
    `axis_name`      VARCHAR(64)   NOT NULL DEFAULT '',
    `soft_limit_min` DOUBLE        NULL,
    `soft_limit_max` DOUBLE        NULL,
    `changed_at`     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `changed_by`     VARCHAR(64)   NOT NULL DEFAULT '',
    PRIMARY KEY (`id`),
    KEY `idx_axis_changed` (`axis_index`, `changed_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

### 5.3 界面上怎么用

1. **启动初始化界面**会自动建库建表，并把数据库里的软限位加载到界面与运动拦截逻辑中。
2. **轴控制页 → “轴名称 / 软限位”**：改上下限后点 **“应用”**（立即生效）；当“应用软限位时自动写库”为开时，
   会同时把该轴写入 MySQL，状态行显示“数据库: 轴N 已保存 HH:mm:ss”。
3. **“保存所有轴到数据库”**：一次性写入所有轴（每轴一条审计记录）。
4. **“从数据库加载”**：用数据库里的值覆盖界面与本地设置（之后运动拦截立即按新值生效）。
5. 连接页的数据库区可 **测试连接**、**初始化(建库建表)**、**保存数据库设置**；`DbStatusText` 会显示
   MySQL 版本、库表状态与记录数，失败时给出中文原因（1045 密码错、1049 库不存在等）。

---

## 6. 启动初始化界面（显示「ACS 初始化成功」）

程序启动后**先显示初始化界面**，按步骤执行并把每一步的结果显示出来：

| 步骤 | 内容 | 失败时的处理 |
| --- | --- | --- |
| 1. 加载本地配置 | 读取 `acs-settings.json`（通讯参数、轴数、数据库参数） | — |
| 2. 初始化数据库(MySQL) | 建库（`acs_motion`）建表；显示 MySQL 版本与已有记录数 | 记为失败但**不阻断**后续（软限位仅本地保存） |
| 3. 连接 ACS 控制器 | 按配置连接（以太网或模拟器），显示固件/序列号/轴数 | 记为失败，可「重新初始化」或「用模拟器重试」 |
| 4. 加载轴软限位 | 从数据库读取各轴软限位并应用 | 数据库不可用时跳过，使用本地配置 |
| 5. 完成 | 成功时状态区**绿色显示「ACS 初始化成功」**及控制器/数据库摘要 | 失败时红色显示原因 |

界面上可用的按钮：

* **用模拟器重试**（连接失败时出现）：自动切换到 SPiiPlus 模拟器并重新初始化，无硬件也能走通“ACS 初始化成功”；
* **重新初始化**：按当前设置再来一次；
* **进入主界面**：成功后进入；若控制器未连接，按钮显示为「跳过并进入(离线)」，可在主界面手动连接。

初始化界面与主界面共享同一套服务/ViewModel，因此初始化阶段建立的连接、加载的软限位会直接出现在主界面上，
不会重复连接。

---

## 7. 快速开始

### 7.1 直接运行已发布版本（推荐）

```
E:\test\acs\publish\AcsMotionTool.exe
```

* 启动后先看**初始化界面**：有控制器时自动连上并显示「ACS 初始化成功」；
* 无硬件：在初始化界面点 **“用模拟器重试”** 即可（或先在“连接”页勾选模拟器）。

### 7.2 从源码运行

```powershell
cd E:\test\acs
dotnet run --project src\AcsMotionTool.App          # 启动界面
dotnet build -c Release                             # 编译整个解决方案
dotnet publish src\AcsMotionTool.App -c Release -o publish
```

---

## 8. 使用要点与安全提醒

1. **先连模拟器再上真机**：模拟器可以完整验证连接、使能、运动、IO 读写、变量、程序缓冲区等流程。
2. **点动（Jog）不会自己停**：点“正转/反转”后必须点“停止”或“急停”；调试真机请先把点动速度设小（例如 1~5）。
3. **先降速再动**：真机调试建议速度 5~20、加速度/减速度 100~500，确认无误后再放大。
4. **急停按钮**：`全部急停` 对所有轴执行 `Kill`（按急停减速度停止）；`全部停止` 执行 `Halt`（按减速度平滑停止）。
5. **软限位**：本程序在“绝对/相对运动”前按设置的上下限拦截（`Axis.SoftLimitMin/Max`），这是软件保护，**不能替代硬件限位与急停回路**。
6. **改名与软限位**：在“轴控制”页选中轴后修改并点“应用”，会立即生效并写入 `acs-settings.json`；若开启数据库且勾选了“应用软限位时自动写库”，同时写入 MySQL（每轴一条审计记录）。也可用“保存所有轴到数据库 / 从数据库加载”批量同步。
7. **设置文件**：`acs-settings.json` 与程序同目录（`publish\` 或 `bin\...`），内含通讯参数、**数据库账号密码**、轴名与软限位；删除即恢复默认（数据库密码为明文，见第 5 节说明）。
8. 崩溃/异常会写入同目录 `acs-crash.log`。

---

## 9. 运动终止码（5003 不是故障！）

界面上“终止码”一列来自控制器的 `MERR`（`GetMotionError`）。按 ACS 官方文档 *Motion Termination Error*：

| 码段 | 含义 |
| --- | --- |
| **5000 ~ 5008** | **正常运动终止，不是故障** |
| 5009 及以上 | 真正的故障（限位、驱动器、编码器、超温、STO…） |

常见正常终止码：

| 码 | 含义 |
| --- | --- |
| 5000 | 运动未完成（未到终点就被终止） |
| 5001 | 运动正常结束（到达终点） |
| 5002 | 被用户 KILL 急停 |
| **5003** | **被用户 HALT 停止**（点过“停止/停止回零”就会出现，属正常） |
| 5004 / 5005 | 被用户下使能 / 因电机下使能而终止 |
| 5006 | 被 KILL 终止 |

所以：

* 看到 **5003 不是故障**，不会触发界面报警；现在“终止码”列会显示 `5003 正常`，日志里还会给出中文解释（本程序早期版本把它当成报警显示，已修正）。
* 只有 **5009 及以上**或驱动器报警（安全输入 DRIVE）才会标红报警，并给出中文原因（如 `5011 硬件左限位`、`5019 驱动器报警`、`5028 STO 未连接`）。
* `MERR` 是只读变量（不能赋值清零），会保留到下一次运动开始；`FaultClear` 也不会清它，这属于控制器行为。

---

## 10. 回零（三种方式）

“轴控制”页的“位置设定 / 回零”里有三种回零方式：

| 方式 | 说明 | 适用场景 |
| --- | --- | --- |
| **运行控制器回零程序** | 运行控制器里已有的回零程序（缓冲区号由你指定） | 真机、现场已把回零程序下载到控制器 |
| **生成 HOME 指令并运行** | 按参数生成 `HOME 轴号, 方式号, 速度, 最大距离[, 偏移]` 写入缓冲区后运行 | 真机快速调试回零参数 |
| **软件置零** | 直接把当前位置设为 0（`SetFPosition` + `SetRPosition`） | **模拟器**、或不需要找原点的场合 |

按钮：`回零`（按所选方式执行）、`停止回零`（停止程序并 HALT 该轴）、`写入回零程序`（只写入不运行）。

要点与坑：

1. **模拟器回不了零是正常现象**：模拟器没有原点开关和编码器索引脉冲，`HOME` 会一直朝一个方向找原点直到“最大距离/超时”。日志会提示“回零超时…模拟器请改用软件置零”。要在模拟器上演示回零流程，请选 **软件置零**。
2. **缓冲区为空时**程序会明确报错：`回零失败: 缓冲区 N 是空的(控制器里没有回零程序)`，并告诉你可以改用生成指令或软件置零。
3. **`FPOS` 是只读变量**，`FPOS(0)=0` 这种写法控制器会报 `Unrecognized command`；置零请用 ACSPL+ 的 `SET RPOS(0)=0`（会同时更新 `APOS/FPOS`）或本程序的“软件置零”。
4. **控制器程序只接受 ASCII**，注释符是 `!`（不是 `//`）。程序下载/追加时本程序会自动剔除中文并按 `!` 处理 `//`、`#` 注释，避免 `Unrecognized command`。
5. **下载程序是“覆盖”语义**：SDK 的 `DownloadBuffer/AppendBuffer` 都是追加，直接调用会把新旧程序拼在一起导致语法错误；本程序改用 `LoadBuffer`（先清空再写入），若需要追加请用 `AcsProgram.Append`。
6. 回零“是否结束”的判断用的是 **轴是否停止运动**（并确认若干次采样），而不是程序状态——因为 `HOME` 启动后程序可能立刻回到 `COMPILED` 而轴仍在运动（实测模拟器与固件行为）。

---

## 11. 命令行自检（现场排障用）

```powershell
# 模拟器自检（无需硬件）
publish\AcsMotionTool.exe --selftest --quiet

# 真机自检（指定控制器 IP）
publish\AcsMotionTool.exe --selftest --ip=192.168.0.10 --quiet

# 指定参与运动测试的轴号（默认 0）
publish\AcsMotionTool.exe --selftest --ip=192.168.0.10 --axis=0 --quiet
```

* 报告写入同目录 `acs-selftest.log`；去掉 `--quiet` 会弹窗显示结果。
* 自检内容：连接 → 控制器信息 → 轴状态/参数 → 使能 → 参数写入回读 → 绝对/相对运动 → Jog/停止 → 数字 IO 读写 → 变量与终端 → 程序缓冲区 → 收尾（停止+下使能+断开）。
* **真机自检会让轴产生实际运动（0 → 10 → 15 位置）**，请确认行程安全后再执行。

本机实测（模拟器）结果示例：

```
== 5. 运动测试 ==
[OK]   绝对运动到 10 完成，当前位置=10.0000
[OK]   相对运动 +5 完成，当前位置=15.0000
[OK]   Jog 启动成功，运动中=True，停止后运动=False
== 7. 变量与终端 ==
[OK]   读变量 FPOS(0) = 16.555
[OK]   终端 ?FPOS(0) -> 16.555
自检结论: 通过 ✔
```

---

## 12. 关于 SDK 版本

默认使用 **4.0.0（.NET 8 版）**：`lib\v4.0.0-net8\ACS.SPiiPlusNET8.dll`。

如果现场控制器固件需要与公司 AB1 工程一致的 **3.3.0** 版本，一条命令即可切换，源码无需修改：

```powershell
dotnet build -p:AcsSdkVariant=v3.3.0-netfx
dotnet run --project src\AcsMotionTool.App -p:AcsSdkVariant=v3.3.0-netfx
```

> 本项目只使用了两个版本都存在的 API（详见 `lib\api-dumps\`），因此两个版本都能直接编译通过。

---

## 13. 二次开发示例（在 AB1 等工程里复用 Core）

```csharp
using AcsMotionTool.Core;

var controller = new AcsController();
controller.LogRaised += (_, e) => Console.WriteLine(e.ToDisplayString());

// 连接（模拟器或以太网）
controller.Connect(new AcsConnectionOptions
{
    Mode = AcsConnectionMode.Ethernet,     // 或 AcsConnectionMode.Simulator
    IpAddress = "192.168.0.10",
    Protocol = AcsEthernetProtocol.UdpPointToPoint,   // 700
    TimeoutMs = 5000,
});

var x = controller.GetAxis(0, "Stage_X");
x.SoftLimitMin = -100;
x.SoftLimitMax = 100;

x.Enable();
x.SetParameters(new AcsAxisParameters { Velocity = 20, Acceleration = 200, Deceleration = 200, Jerk = 2000, KillDeceleration = 2000 });
x.MoveAbsolute(50, velocity: 20, wait: true, timeoutMs: 30000);   // 绝对运动并等待到位
x.MoveRelative(-5, velocity: 10, wait: true);                     // 相对运动
x.StartJog(5); Thread.Sleep(500); x.Stop();                        // 点动 + 停止

Console.WriteLine(x.ReadStatus().EncoderPosition);                 // 位置
controller.Io.SetOutputBit(0, 3, true);                            // 输出 0.3 = 1
Console.WriteLine(controller.Variables.ReadAsText("MST(0)"));      // 变量
Console.WriteLine(controller.Terminal.Transaction("?FPOS(0)"));   // 终端查询
x.Home(bufferIndex: 0, timeoutMs: 180000);                         // 运行回零程序
controller.Disconnect();
```

要点：

* `AcsController` 内部对 `ACS.SPiiPlusNET.Api` 加锁串行化，多线程（界面轮询 + 按钮操作）不会并发访问同一条链路。
* 所有可能失败的方法都返回 `bool`/默认值并写日志；`ExecuteOrThrow` 系列才抛 `AcsOperationException`。
* 长耗时操作（等待到位、回零、等待程序结束）采用**轮询**实现，不会长时间占用通讯锁。

---

## 14. 现场常见问题

| 现象 | 原因与处理 |
| --- | --- |
| **轴显示 5003** | 5003 = 被 `HALT` 停止（点“停止/停止回零/急停”后就会出现），属**正常终止**，不是故障。现在只在“终止码”列显示 `5003 正常`，不再报警、不再标红，日志会给出中文解释。 |
| **模拟器回零不成功** | 模拟器没有原点开关与索引脉冲，`HOME` 会一直找原点直到超时。用“软件置零”代替；或用“生成 HOME 指令”把最大距离设小，观察回零动作后“停止回零”。 |
| 运行回零程序报 `Attempt to compile or start empty buffer` | 该缓冲区里没有程序。请选“生成 HOME 指令并运行”/“软件置零”，或先用 MMI Application Studio 把回零程序下载到该缓冲区。 |
| 程序运行报 `Unrecognized command` | 控制器只接受 ASCII、注释符是 `!`。不要把中文注释或 `//` 注释写进程序（本程序会自动清洗并转换，但手工在别处下载时要注意）。 |
| 下载程序后程序变长/行为异常 | SDK 的 `DownloadBuffer` 是**追加**语义，会与旧程序拼接。本程序已改用 `LoadBuffer`（先清空再写）。 |
| 连接时日志出现 `设置通讯超时失败(已忽略，不影响通讯): Communication server can't be found` | 该版本 SDK 的 `SetTimeout` 走的是“服务器”接口，模拟器/部分现场环境不支持，**不影响后续通讯**。程序已改为告警并继续连接。 |
| 连接真实控制器失败 | 检查控制器上电与网线；`ping` 控制器 IP；确认无其它软件（MMI Application Studio 等）已占用通讯；试 TCP(701) 或 UDP(700)。 |
| 模拟器连接失败 | 确认输出目录存在 `sb4.exe`；确认 UMD 配置的模拟器端口（默认 TCP 701 / UDP 700）未被占用；必要时先用 SPiiPlus MMI Application Studio 连接一次模拟器。 |
| `?ERROR(0)`、`?VER` 等查询返回 `Undefined global variable` | 模拟器不实现全部变量（文档明确列出不支持 Index/MARK/PEG/模拟 IO 等）。真机上通常可用，属正常差异。 |
| 运动指令下发成功但轴不动 | 检查是否已使能（本程序运动前会自动使能）、是否有驱动器报警/限位、速度/加速度是否为 0、是否被软限位拦截（日志会有记录）。 |
| VS 报 `命名空间“XxxConverter”中不存在名称“clr-namespace:AcsMotionTool.App.Infrastructure”` | 这类错误通常是**连带故障**：VS 里加载的解决方案缺少 `AcsMotionTool.Data` 等项目，导致 App 编译失败、XAML 设计器解析不到程序集。处理：关闭解决方案再重新打开（或右键解决方案 → 重新加载项目）→ 生成一次；仍报则先执行 `dotnet restore AcsMotionTool.slnx`。 |
| VS 报 `找不到 ...AcsMotionTool.Data.csproj 的项目信息` | 同上：解决方案文件里没有该项目，或 `obj/project.assets.json` 被清理。`dotnet sln AcsMotionTool.sln migrate` 可把 `.slnx` 与 `.sln` 同步；`dotnet restore` 恢复还原信息，然后重新打开解决方案。 |

---

## 15. 验证情况（本机实测）

* `dotnet build`：3 个项目全部 0 警告 0 错误；`-p:AcsSdkVariant=v3.3.0-netfx`（公司 AB1 同款 3.3.0 SDK）同样编译通过。
* `AcsMotionTool.exe --selftest --quiet`：11 个自检分项全部通过（含终止码判定、回零能力、程序覆盖写入回读）。
* 真机（未接控制器）未做运动验证，请按第 8 节的安全流程在现场确认。

> 开发期间曾用临时控制台程序验证过以下内容（这些程序不属于产品，已从仓库移除，结论保留供参考）：
> ViewModel + MySQL 36 项断言（初始化 5 步、跨页联动、运动/IO/变量/终端/程序缓冲区、软限位存库与恢复、设置落盘）、
> 界面 UI Automation 7 项（初始化界面 → 用模拟器重试 → 显示「ACS 初始化成功」→ 进入主界面）、
> 模拟器全流程冒烟、5003/回零回归 13 项、长跑稳定性（内存/CPU/UI 延迟）。
> 其中数据库测试使用独立库 `acs_motion_vmtest`，跑完自动 DROP，不污染现场数据。
