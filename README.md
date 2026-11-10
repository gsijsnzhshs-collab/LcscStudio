# ADesignK — Altium Designer 立创商城元件导入插件

在 Altium Designer 内以停靠面板（标题 `LCSC ADesignK`）搜索立创商城元件，
预览符号 / 封装 / 3D / 商品参数，并一键导入到当前原理图库、PCB 库或直接放置到原理图。

- 当前版本：`1.0.3.0`（唯一版本源：[version.json](version.json)）
- 已验证版本：Altium Designer 20.0.13、**AD22 (22.9.1)**（详见 [docs/compat-matrix.md](docs/compat-matrix.md)）
- 平台：Windows / .NET Framework 4.8（由 AD 宿主加载，无需单独运行时）

## 功能

| 功能 | 说明 |
|---|---|
| 搜索 | 按立创编号（`C269701`）或型号（`2SC1623`）检索，列表显示编号 / 型号 / 封装 / 库存 / 单价 |
| 符号预览 | 自绘原理图符号，支持 EasyEDA 多部件（subparts）上下堆叠显示 |
| 封装预览 | 解析 EasyEDA 封装图元（焊盘、线、圆弧、多边形、PATH） |
| 3D 预览 | 面板内提示占位，模型随导入写入 AD 库后查看 |
| 参数页 | 显示立创商品参数（属性 / 参数值），已过滤 Package、EasyEDA UUID、Supplier、Supplier Part |
| 单件 / 批量导入 | 导入符号 + 封装 + 3D 模型到目标库；批量导入带进度与取消 |
| 放置到原理图 | 导入后可选「放置到当前原理图」，符号跟随鼠标 |
| 多部件导入 | `SetState_PartCountNoPart0` + `OwnerPartId`，STM32 等电源分区器件按部件导入 |
| 菜单入口 | 原理图 / PCB / 原理图库 / PCB 库四个编辑器的 Tools 菜单均出现 `LCSC Component Import...` |

## 目录结构

```
LcscLoader/          插件主体（AD 扩展、导入逻辑、解析器）
  src/               C# 源码：LcscModule、SymbolImporter、FootprintImporter、EasyEDA 解析
  tests/             离线回归 Harness（无 AD SDK 依赖，读 fixtures 校验解析）
  deploy.js          部署 / 卸载脚本
  LCSCLoader.rcs     四个编辑器的菜单与命令注册
  LCSCLoader.Ins     扩展描述文件模板（版本与日期由构建注入）
LcscStudio/          面板 UI 源码（PanelForm、PreviewCanvas、Theme…），编译进同一 DLL
installer/           一键安装器（内嵌插件产物，WinForms 单 exe）
tools/               构建工具链：SDK 解析、工具链恢复、部署校验、版本读取
fixtures/            离线测试夹具（EasyEDA JSON / STEP / OBJ）
docs/                AD 兼容矩阵
legacy/wpf/          早期 WPF 界面，不参与编译
```

## 环境要求

1. Windows + 已安装 Altium Designer（构建需要其 `System` 目录下的
   `Altium.SDK.dll`、`Altium.SDK.Interfaces.dll`、`Newtonsoft.Json.dll`）
2. Node.js（构建脚本全部为 Node，无需 .NET SDK / MSBuild）
3. 首次使用先恢复本地工具链（Roslyn 4.13.0 编译器 + .NET Framework 4.8 引用程序集）：

```
node tools/restore-tools.js
```

> `_tools/` 与所有构建产物都在 `.gitignore` 中，不随仓库分发；恢复脚本优先使用本地
> `*.nupkg`，否则从 NuGet 下载并按固定 SHA256 校验。

## 构建

```
node LcscLoader/build.js                          # 自动探测 AD SDK
node LcscLoader/build.js --ad-version 22          # 指定 AD 主版本
node LcscLoader/build.js --sdk-dir "D:\AD\AD20\System"
```

SDK 解析优先级：`--sdk-dir` > 环境变量 `AD_SDK_DIR` > 自动探测（注册表 / 常见安装路径）。

产物：`LcscLoader/bin/LcscLoader.dll`、`LCSCLoader.Ins`、`build-info.json`
（含编译器 / 引用 / 源文件 / 输出 DLL 的 SHA256 与 git 快照，便于复现与校验）。

## 部署与安装

方式一：一键安装器（推荐给使用者）

```
node installer/build.js        # 需先构建 LcscLoader
installer/bin/LcscSetup.exe    # 图形界面安装，可自动识别 AD 版本与路径
```

安装器支持命令行：`--list`、`--install`、`--uninstall`、`--out <file>`。
安装前会备份原有文件并保留最近 3 份，AD 运行中会拒绝覆盖。

方式二：脚本部署（开发调试）

```
node LcscLoader/deploy.js --target system         # 或 --target programdata
node tools/verify-deploy.js --target system       # 校验文件哈希、EditorExePath、.Ins 版本
node LcscLoader/deploy.js --target system --uninstall
```

跨机器部署（目标机没有 SDK 时）：

```
node LcscLoader/deploy.js --target-dir "C:\Program Files\Altium\AD22\System"
```

部署后须重启 Altium Designer；四个编辑器的 Tools 菜单出现 `LCSC Component Import...` 即为生效。

## 测试

```
node LcscLoader/tests/build.js        # 编译 Harness 并跑 --offline 夹具
node LcscLoader/tests/build.js --no-run
```

夹具为 4 个真实器件的脱敏数据：0603 电阻、QFP-48、BGA-60、QFN-56（含 STEP），
断言引脚数、焊盘数、层数、3D 标志与格式守卫跳过数（见 [fixtures/expected.json](fixtures/expected.json)）。
回归无需安装 AD，仅需已恢复的工具链与 `Newtonsoft.Json.dll`。

## 版本与发布

- `version.json` 是唯一版本源，驱动 `AssemblyVersion`、`.Ins` 的 Version/Date 与部署校验。
- 构建会生成 `LcscLoader/bin/build-info.json`，安装器从该文件读取版本与产物哈希。

## 兼容性

| AD 版本 | 状态 |
|---|---|
| AD20 (20.0.13) | 全部检查项 PASS |
| AD22 (22.9.1) | 全部检查项 PASS |
| AD22 (22.0.2) | 导入链路 PASS（已修复 ShowDocument RPC_E_SERVERFAULT 误报） |
| AD24 / AD25 | 未执行（本机无环境） |

已知限制：
- 重启 AD 后面板不自动展开，仅保留右侧折叠按钮（点击后按默认宽度展开）
- easyeda.com 最近对 `/api/products/{lcsc}/components` 返回 **CloudFront 403**（接口限流），只剩 `lceda.cn` 主机部分可用；已下架/索引残留的料号会返回 `success=false, code=404`，插件面板显示"抓取失败：未找到器件"后继续下一件，不影响其他料号
- `tools/restore-tools.js` 在 Windows PowerShell 5 下解压 `.nupkg` 时 `Expand-Archive` 会因扩展名白名单拒绝，绕过方式：手动用 Python `zipfile` 解压到 `_tools/<name>/` 即可

完整矩阵与复现步骤：[docs/compat-matrix.md](docs/compat-matrix.md)。

## 说明

- 元件数据来自立创商城 / EasyEDA 公开接口，请遵守其服务条款，控制请求频率。
- 本项目与立创商城、Altium 无隶属关系；导入结果请自行核对后再用于生产设计。
