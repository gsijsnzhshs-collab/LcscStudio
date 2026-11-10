# Changelog

本文件记录 LcscLoader 的版本变更。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，并遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## v1.0.3.0 — 2026-11-10

立创商城器件查找 / 封装导入 / 3D 预览插件，适配 Altium Designer 22。

### Added

- **3D 引脚识别三路径**：新增 `ModelCanvas.cs` 渲染画布。引脚依次走 连通块法 → 材质法 → 外形切片兜底（`MarkPinsBySilhouette`）。TO-220 类模型（L7812、L7809 等）本体与引脚焊死成同一块网格且同材质，旧逻辑无法区分；新增的外形切片沿 Z 轴切 40 层、按每层宽度定位引脚区，三根引脚自动变亮银色
- **符号多部件支持**：多 part 器件（如运算放大器）现在能正确解析、预览并导入到 AD 原理图库
- **商品参数面板**：选中器件时显示立创商品参数（输出电压、工作温度、封装类型等）
- **一键安装器**：`node LcscLoader/deploy.js --target system` 自动识别 AD 版本并支持安装/卸载，部署时备份最近 3 份旧版
- **部署工具**：支持 `--target-dir` 指定任意 AD System 目录，便于跨机器部署与 dry-run 预览

### Changed

- **2D 丝印层色**：TopSilk `#E6EAF0`、BottomSilk `#9CA8B8`，与 Multi-Layer 焊盘 `#C89632` 金铜色拉开，解决 C90797 / TO-126 (D882) 封装焊盘与丝印撞色问题
- **面板标题**：插件面板缩短为 **ADesignK**
- **构建产物路径**：`LcscLoader/bin/build-info.json` 附带每个源码文件的 sha256、编译器版本、SDK 引用，便于离线核对

### Fixed

- **TO-220 焊死模型**：本体与引脚同材质、厚度贯穿全高时旧逻辑直接放弃上色，现已由外形切片兜底接管
- **对话框定位**：`Theme.PlaceDialog()` 把对话框限制在 owner 所在显示器工作区内，并避开 Win11 右下角输入法工具栏浮层遮挡
- **EasyEDA 详情接口 `success=false`**：easyeda.com / lceda.cn 常在 HTTP 200 里返回 `{"success":false,"code":404}` 表示器件未找到（器件下架或索引残留），旧代码把空壳数据当正常 LcscPart 喂给 AD COM → 抛 RPC_E_SERVERFAULT。现在解析 body 后抛 `EeNotFoundError`，面板显示"抓取失败：未找到器件"，继续导入下一件
- **导入成功误报失败**：`ImportService.ExecuteCoreAsync` 收尾用 `IClient.ShowDocument` 恢复用户原视图，但 `EnsureDocumentHidden` 以隐藏方式打开的 SchLib/PcbLib 文档在 AD22.0.x 上调用 ShowDocument 会抛 `RPC_E_SERVERFAULT (0x80010105)`。异常从 finally 冒泡后覆盖了已经计入的 `outcome.Imported++` → 面板显示"导入失败"，但库文件实际已正确写入。抽 `RestoreView(IServerDocument)` 独立方法吞异常，finally 不再影响导入结果

### Tech

- .NET Framework 4.8，Roslyn 编译器 v4.13.0-3.25120.11
- SDK 自动探测：从 AD22 注册表路径加载 `Altium.SDK.dll`、`Altium.SDK.Interfaces.dll`、`Newtonsoft.Json.dll`

---

## pre-t4-6 — 2026-11-09

批量导入异步化、端点配置化、错误分类。

### Added

- **批量导入异步推进**：支持进度显示与取消
- **端点配置化**：host / API 版本 / 路径 / 超时可外置覆盖，默认走 `lceda.cn` / `easyeda.com`

### Fixed

- **错误分类**：接口错误区分网络失败、格式错误、未找到（返回 `EeNetworkError` / `EeFormatError` / `EeNotFoundError`）
- **符号覆盖**：覆盖已有符号改为原地清空，修复 AD 空引用
- **JSON 解析守卫**：字段不足或未知图元不再静默通过

---

## baseline — 2026-11-07

首次可运行基线：器件搜索、封装导入、3D 预览基础版。
