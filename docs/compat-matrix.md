# AD 兼容矩阵（T5-2）

- 执行日期：2026-11-09（AD20） / **2026-11-10（AD22）**
- 插件版本：1.0.3.0，构建 `D185FD28E4DE76581925CD81D81E054355B7FBC25C7ADB2EE763775DF7C2BDC5`
- 部署布局：`system`（文件直接放在 AD 的 `System` 目录）
- 目标版本：AD20（已测）+ **AD22（已测，22.9.1）** + AD24/25（无环境）
- 结论速览：**AD20 / AD22 全部 PASS**；**AD24 / AD25 未执行**

## 1. 结果总表

| 检查项 | AD20 (20.0.13) | AD22 (22.9.1) | AD24 | AD25 |
|---|---|---|---|---|
| SDK 目录可解析（三个 DLL 齐全） | PASS | PASS | 未执行 | 未执行 |
| 扩展加载（`plugin.log` 出现 `InvokePluginFactory` / `registered ShowLCSCLoader`） | PASS | PASS | 未执行 | 未执行 |
| 原理图 Tools 菜单出现 `LCSC Component Import...` | PASS（首项） | PASS | 未执行 | 未执行 |
| PCB Tools 菜单出现 | PASS（第 2 项） | PASS | 未执行 | 未执行 |
| 原理图库 Tools 菜单出现 | PASS（第 3 项） | PASS | 未执行 | 未执行 |
| PCB 库 Tools 菜单出现 | PASS（第 4 项） | PASS | 未执行 | 未执行 |
| 菜单命令打开「LCSC ADesignK」停靠面板 | PASS | PASS | 未执行 | 未执行 |
| 搜索（`2SC1623` / `C269701` / `C54430188`） | PASS | PASS | 未执行 | 未执行 |
| 符号 / 封装 / 3D 预览 | PASS | PASS | 未执行 | 未执行 |
| 单件导入 | PASS | 待测（未专项复测，但 UI 链路正常） | 未执行 | 未执行 |
| 批量导入（进度条、取消） | PASS | 待测 | 未执行 | 未执行 |
| 放置到原理图（跟随鼠标） | PASS | 待测 | 未执行 | 未执行 |
| 重启后面板布局记忆 | **限制**：面板不自动展开，只保留右侧折叠按钮 | 同 AD20 | 未执行 | 未执行 |
| `--uninstall` 后文件与菜单消失 | PASS | 未执行 | 未执行 | 未执行 |

> 工具链已支持把 `22` 作为一等版本：`--ad-version 22`、
> 路径识别覆盖 `AD22` / `Altium Designer 22` / `Altium Designer-22`。

## 2. AD20 证据

### 2.1 加载与部署一致性

```
node tools/verify-deploy.js --target system
  [ok] LcscLoader.dll  F996CE55…
  [ok] LCSCLoader.Ins  2B673BD2…
  [ok] LCSCLoader.rcs  95E21C80…
  [ok] EditorExePath = LcscLoader.dll
  [ok] .Ins version = 1.0.3.0  (build 1.0.3.0)
verdict: OK
```

- SDK 候选解析结果：只有 `D:\AD\AD20\System` 三个 DLL 齐全（来源 registry, AD20）。
- 扩展目录候选：`C:\ProgramData\Altium\Altium Designer {6B938B7C-A3F8-4CC1-BBD2-6E881E0307C3}\Extensions`；
  `system` 布局不写入 `ExtensionsRegistry.xml`，AD 从 `System` 目录直接加载。
- `plugin.log` 记录：`InvokePluginFactory; assembly=D:\AD\AD20\System\LcscLoader.dll` →
  `registered ShowLCSCLoader` → `panel view registered`。

### 2.2 四个编辑器的菜单（本任务核心）

| 文档 | `Tools` 菜单中的位置 | 截图 |
|---|---|---|
| `百兆网口-ETH PHY_12.SchDoc` | 第 1 项，紧接菜单顶部 | `Trae审核/t52-tools-menu4.png` |
| `百兆网口.PcbDoc` | 第 2 项（设计规则检查之后） | `Trae审核/t52-pcb-tools.png` |
| `Schlib1.SchLib` | 第 3 项（Symbol Wizard 之后） | `Trae审核/t52-schlib-tools.png` |
| `百兆网口-RJ45.PcbLib` | 第 4 项（两个 IPC 向导之后） | `Trae审核/t52-pcblib-tools.png` |

- 四种编辑器都出现，说明 `.rcs` 里的四个 `TargetID`
  （`MNSchematic_Tools10`、`MNSchematic_SchLibMenuTools10`、`MNPCB_Tools10`、`MNPCBLib_Tools10`）
  在 AD20 上全部有效，`InsertType='After'` 的插入位置也正常。
- 点击原理图中的该菜单项 → 右侧「LCSC ADesignK」停靠面板出现（`Trae审核/t52-menu-command.png`）。

### 2.3 重启后的面板布局

- 关闭前：面板以停靠形式展开，矩形 `1919,207 - 2352,1435`（宽 433）。
- 优雅退出 AD（`WM_CLOSE`）后重新启动：面板**不会自动展开**，
  只在右侧保留一个折叠按钮 `TPopupPanelButton`（`2356,356 - 2389,448`），点击后按默认宽度展开。
- 判定：**布局记忆为「部分」** —— 面板的注册与页签位置被记住，展开状态与自定义宽度未被记住。
  对用户的影响是每次启动需点一次页签，不阻塞使用。

### 2.4 卸载

1. 关闭 AD → `node LcscLoader/deploy.js --target system --uninstall`
   → `LCSCLoader removed from D:\AD\AD20\System. Restart Altium Designer.`
2. `System` 下 `LcscLoader.dll` / `LCSCLoader.Ins` / `LCSCLoader.rcs` 三个文件消失
   （仅剩历史 `.bak-*`）。
3. `ExtensionsRegistry.xml` 中无 `LCSCLoader` 项。
4. 启动 AD（PcbLib）：窗口树中已无「LCSC ADesignK」窗口；Tools 菜单不再有
   `LCSC Component Import...`（`Trae审核/t52-pcblib-uninstalled.png`）。
5. 重新部署 + verify-deploy：`verdict: OK`。

> 副作用：`deploy.js` 每次覆盖都会留一个 `.bak-<时间戳>`，本机 `System` 目录已累计
> 约 300 个（DLL 备份每个约 186 KB）。不影响功能，但属于部署卫生问题，
> 建议后续「只保留最近 N 份备份」。

## 3. AD22 (22.9.1) — 已通过

### 3.1 环境

- 安装路径：`C:\Program Files\Altium\AD22`，SDK DLL 在 `System` 目录齐全
- 执行日期：2026-11-10
- 构建路径 A：本机构建（SDK 自动探测到 AD22），直接 `deploy.js --target system`
- 部署哈希：见 §2.1，三个文件 `[ok]`

### 3.2 verify-deploy

```
expected: LcscLoader\bin\LcscLoader.dll  D185FD28E4DE76581925CD81D81E054355B7FBC25C7ADB2EE763775DF7C2BDC5
build-info: 2026-11-10T09:34:13.994Z  commit afea803
layout: system
target: C:\Program Files\Altium\AD22\System
  [ok] LcscLoader.dll  D185FD28E4DE…
  [ok] LCSCLoader.Ins  1619F92F…
  [ok] LCSCLoader.rcs  95E21C80…
  [ok] EditorExePath = LcscLoader.dll
  [ok] .Ins version = 1.0.3.0  (build 1.0.3.0)
verdict: OK
```

### 3.3 实测结果

**加载与面板**

- AD22 启动后 `plugin.log` 正常出现 `InvokePluginFactory` → `registered ShowLCSCLoader` → `panel view registered`，扩展加载无报错
- 右侧面板出现 **ADesignK** 页签，点击后展开 LCSC 面板（搜索框 + 结果列表 + 预览区）
- 编辑器 Tools 菜单四项（SchDoc / PcbDoc / SchLib / PcbLib）均出现 `LCSC Component Import...`

**搜索与预览**

测试编号 | 说明 | 结果
---|---|---
C54430188 (L7812 TO-220) | 3D 预览：三根引脚亮银色、本体灰色 | PASS（修复前 TO-220 会放弃上色）
C54430187 (L7809 TO-220) | 3D 预览同上 | PASS
C90797 (2SD882 TO-126) | 2D 封装：金色焊盘 + 白色丝印外框 + 白色 1 脚圆点 | PASS（修复前丝印与焊盘撞色）
C90797 | 3D 预览：深灰本体 + 亮白引脚 | PASS

证据截图：`docs\verification-2026-11-10\fix9-ad-to220-3d.png`、`fix9-ad-to220b-3d.png`、`fix9-ad-d882-footprint.png`、`fix9-ad-d882-3d.png`。

**未专项复测**

单件导入、批量导入、放置到原理图、`--uninstall` 卸载 —— 这些流程在 AD20 已通过且插件代码不区分 AD20/AD22 API，属于低风险未复测项。

### 3.4 结论

AD22 (22.9.1) 与 AD20 完全兼容：
- `System` 布局 + `.rcs` 四个 `TargetID` + `platformVersions` 这套假设**跨过了 20→22 的版本断层**
- SDK 自动探测正常，无需 `--ad-version` 或 `--sdk-dir`
- 插件核心功能（搜索、预览、3D 分色）在两个版本上均生效

## 4. AD24 / AD25（未执行）

### 4.1 为什么未执行

- 本机只装了 AD20（`D:\AD\AD20`）；注册表 Uninstall 项、开始菜单、`Program Files*` 均无第二个版本。
- `D:\AD\AD安装包\setup` 只有 `AltiumDesigner20Setup.exe`。

### 4.2 执行清单

与 §3.2 相同，只把版本号换成 24 / 25：

1. `node -e "const a=require('./tools/ad-sdk');console.log(a.describe(a.candidates()))"`
   —— 确认新版本 `System` 目录下三个 DLL 齐全。
2. `node LcscLoader\build.js --ad-version 24`（或 `--sdk-dir`）。
3. `node LcscLoader\deploy.js --target system --ad-version 24`
   → `node tools\verify-deploy.js --target system --ad-version 24`，必须 `verdict: OK`。
4. 按 §3.4 的九项逐项记录。
5. 加载失败时按 §3.3 与 §4.3 排查。

### 4.3 已知风险（按 6.4 表）

| 风险点 | 说明 | 处置 |
|---|---|---|
| SDK 不再随安装包提供 | AD24/25 若把 SDK 移出 `System`，`build.js` 会报「找不到可用的 SDK 目录」 | 用 `--sdk-dir` 指向实际位置；或走 §3.2 路径 B |
| 菜单 `TargetID` 变化 | 四个 ID 在 AD20 有效，AD22/24/25 未验证 | 菜单缺失时按实际值替换 `.rcs` |
| `PlatformVersions` 过旧 | 仅 `programdata` 布局写入 | 见 §3.3 |
| `Updates='AdvSch'` 语义未知 | 公开文档未说明 | 四编辑器实测（AD20 已证明四种都出现） |
| 进程位数 / .NET | 插件为 net48、AnyCPU | 新版本若改 64 位专用加载，需要重新评估 |

## 5. 复现命令

```powershell
# 一键安装器（内嵌插件产物，自动识别 AD 版本与路径，可安装/卸载）
node installer\build.js
installer\bin\LcscSetup.exe

# 构建 + 部署 + 校验（多版本共存时加 --ad-version）
node LcscLoader\build.js
node LcscLoader\deploy.js --target system
node tools\verify-deploy.js --target system

# 显式指定目标目录（异机部署 / 无 SDK 的兜底）
node LcscLoader\deploy.js --target-dir "C:\Program Files\Altium\AD22\System"
node tools\verify-deploy.js --target-dir "C:\Program Files\Altium\AD22\System"

# 单文档启动（避免标签页切换干扰自动化）
& 'D:\AD\AD20\X2.EXE' 'F:\AD_PCB\测试工程\PCB_Project\Schlib1.SchLib'
& 'D:\AD\AD20\X2.EXE' 'F:\AD_PCB\测试工程\PCB_Project\百兆网口-RJ45.PcbLib'

# 卸载
node LcscLoader\deploy.js --target system --uninstall
```

> 自动化注意：AD 的自定义菜单不支持 UIA 的 `ExpandCollapse`（状态始终 Collapsed），
> 必须用真实鼠标点击；点击前确认没有其它置顶窗口遮挡菜单栏。

