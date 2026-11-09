# AD 兼容矩阵（T5-2）

- 执行日期：2026-11-09
- 插件版本：1.0.3.0，构建 `F996CE55DE042ACF6C182513FCC0C38341875835536642199E0673BA8991B377`
- 部署布局：`system`（文件直接放在 AD 的 `System` 目录）
- 目标版本：AD20（已测）+ **AD22（另一台机器，待执行）** + AD24/25（无环境）
- 结论速览：**AD20 全部 PASS**；**AD22 / AD24 / AD25 未执行**

## 1. 结果总表

| 检查项 | AD20 (20.0.13) | AD22 | AD24 | AD25 |
|---|---|---|---|---|
| SDK 目录可解析（三个 DLL 齐全） | PASS | 待测 | 未执行 | 未执行 |
| 扩展加载（`plugin.log` 出现 `InvokePluginFactory` / `registered ShowLCSCLoader`） | PASS | 待测 | 未执行 | 未执行 |
| 原理图 Tools 菜单出现 `LCSC Component Import...` | PASS（首项） | 待测 | 未执行 | 未执行 |
| PCB Tools 菜单出现 | PASS（第 2 项） | 待测 | 未执行 | 未执行 |
| 原理图库 Tools 菜单出现 | PASS（第 3 项） | 待测 | 未执行 | 未执行 |
| PCB 库 Tools 菜单出现 | PASS（第 4 项） | 待测 | 未执行 | 未执行 |
| 菜单命令打开「立创选料」停靠面板 | PASS | 待测 | 未执行 | 未执行 |
| 搜索（`2SC1623` / `C269701`） | PASS | 待测 | 未执行 | 未执行 |
| 符号 / 封装 / 3D 预览 | PASS | 待测 | 未执行 | 未执行 |
| 单件导入 | PASS | 待测 | 未执行 | 未执行 |
| 批量导入（进度条、取消） | PASS | 待测 | 未执行 | 未执行 |
| 放置到原理图（跟随鼠标） | PASS | 待测 | 未执行 | 未执行 |
| 重启后面板布局记忆 | **限制**：面板不自动展开，只保留右侧折叠按钮 | 待测 | 未执行 | 未执行 |
| `--uninstall` 后文件与菜单消失 | PASS | 待测 | 未执行 | 未执行 |

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
- 点击原理图中的该菜单项 → 右侧「立创选料」停靠面板出现（`Trae审核/t52-menu-command.png`）。

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
4. 启动 AD（PcbLib）：窗口树中已无「立创选料」窗口；Tools 菜单不再有
   `LCSC Component Import...`（`Trae审核/t52-pcblib-uninstalled.png`）。
5. 重新部署 + verify-deploy：`verdict: OK`。

> 副作用：`deploy.js` 每次覆盖都会留一个 `.bak-<时间戳>`，本机 `System` 目录已累计
> 约 300 个（DLL 备份每个约 186 KB）。不影响功能，但属于部署卫生问题，
> 建议后续「只保留最近 N 份备份」。

## 3. AD22（另一台机器，待执行）

### 3.1 为什么要在 AD22 上测

- AD22 是当前可用的第二个主版本，比 AD24/25 更接近 AD20；如果 AD22 通过，
  说明 `System` 布局 + `.rcs` 四个 `TargetID` + `PlatformVersions` 这套假设跨过了 20→22 的版本断层。
- 本机没有 AD22，必须在那台机器上执行。

### 3.2 两种执行路径

**路径 A（推荐，能验证「源码对 AD22 SDK 可编译」）**

1. 把整个仓库拷到 AD22 机器（`_tools/` 不需要拷，联网恢复）：
   ```
   node tools\restore-tools.js
   node -e "const a=require('./tools/ad-sdk');console.log(a.describe(a.candidates()))"
   ```
   确认候选列表里出现 AD22 的 `System`，且三个 DLL 状态为 `ok`。
2. 构建并部署：
   ```
   node LcscLoader\build.js --ad-version 22
   node LcscLoader\deploy.js --target system --ad-version 22
   node tools\verify-deploy.js --target system --ad-version 22
   ```
   若 SDK 位置不在自动扫描范围，改用 `--sdk-dir "<AD22 System 路径>"`（此时可省略 `--ad-version`）。
3. 打开 AD22，按 §3.4 逐项记录。

**路径 B（AD22 目录里没有 SDK DLL 时的兜底）**

在本机构建，把产物和脚本拷过去，直接部署到指定的 `System` 目录：

1. 本机：`node LcscLoader\build.js`（用 AD20 SDK 构建，产物不变）。
2. 拷贝这些到 AD22 机器同一相对位置：
   `LcscLoader\bin\`（DLL、`.Ins`、`build-info.json`）、`LcscLoader\LCSCLoader.rcs`、
   `LcscLoader\deploy.js`、`tools\`、`version.json`。
3. 在 AD22 机器上（AD 已关闭）：
   ```
   node LcscLoader\deploy.js --target-dir "C:\Program Files\Altium\AD22\System"
   node tools\verify-deploy.js --target-dir "C:\Program Files\Altium\AD22\System"
   ```
   `--target-dir` 会跳过 SDK 探测，只做文件复制与哈希校验；
   `verify-deploy` 应输出 `verdict: OK`。
4. 打开 AD22，按 §3.4 逐项记录。

> 路径 B 的风险：AD22 若改变了插件宿主 API（`DXP`/`SCH`/`PCB` 接口签名），
> 用 AD20 SDK 编译的 DLL 可能在加载时报类型/方法缺失。此时只能回到路径 A 或改用官方 SDK。

### 3.3 平台版本号（若扩展不加载再处理）

- `system` 布局不写 `ExtensionsRegistry.xml`，因此 `PlatformVersions` 目前**不参与**加载判定；AD20 已验证。
- 只有走 `programdata` 布局时才由 `deploy.js` 写入固定值
  `DXP 1.0.10.0` / `EDP 10.0.10.0`。若 AD22 用该布局且扩展状态为「不兼容」，
  按实际平台版本上调 `deploy.js` 中的两个 `BuildNumber` 后重新部署。

### 3.4 逐项记录清单（把结果填回 §1 的 AD22 列）

1. `我的文档\AltiumLCSC\plugin.log` 是否出现
   `InvokePluginFactory; assembly=...` → `registered ShowLCSCLoader` → `panel view registered`。
   **没有这三行 = DLL 没被加载**，后续菜单检查无意义。
2. 四个编辑器（SchDoc / PcbDoc / SchLib / PcbLib）的 Tools 菜单是否出现
   `LCSC Component Import...`，记录它是第几项。
3. 点击菜单项，右侧「立创选料」面板是否出现。
4. 搜索 `2SC1623`，选择一条，符号 / 封装 / 3D 三个页签是否都能预览。
5. 单件导入一次（目标库建议指向临时目录，别写生产库）。
6. 多选 3 条批量导入，观察进度条推进与「取消」按钮；再取消一次。
7. 单件导入后勾选「导入后放置到当前原理图」，确认符号跟随鼠标。
8. 关闭 AD 再启动，记录面板是展开还是只剩折叠按钮。
9. `--uninstall` 后确认菜单与面板消失，再重新部署恢复。

### 3.5 需要带回的信息

- §1 表格里 AD22 一列的实际结果；
- `plugin.log` 中从 `InvokePluginFactory` 到首次导入结束的片段；
- 四张菜单截图 + 一张导入完成截图；
- 若失败：`verify-deploy` 输出、AD 的错误对话框原文、`Extensions & Updates` 里的扩展状态。

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
