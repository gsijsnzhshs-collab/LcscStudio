---
name: lcsc-release
description: Build, tag, and publish LcscLoader releases to GitHub. Use when the user asks to tag, publish, ship, or make a new LcscLoader version for others to download. Do not use for day-to-day commits or plain pushes.
---

# LcscLoader 发布流程

## 铁律（违反即重开）

1. **每次发版必须 bump version.json**，且 CHANGELOG 必须有对应版本块。**没有独立版本块的发布视为无效。**
2. **tag 必须指向干净工作区 + HEAD commit**，顺序严格为：**bump → 写 CHANGELOG → build → commit → tag → push**。
3. **CHANGELOG 版本块从 v1.0.3.1 起固定格式**：标题 `## v{X.Y.Z} — YYYY-MM-DD`，正文按 Added / Changed / Fixed / Tech 分类，每段至少一条。

## 前置校验（先全部过一遍，再动手）

1. **读当前版本**：`version.json` 的 `version` 字段（唯一版本源）。
2. **工作区干净**：`git status` 必须 `nothing to commit, working tree clean`。
3. **gh CLI 登录**：`gh auth status` 返回 `Logged in to github.com`。
4. **远端 tag 未占用**：`git tag -l "v{nextVersion}"` 应返回空。

## 版本号 bump 规则

| 改动性质 | 版本变化 | 判断依据 |
|---|---|---|
| bug fix（修复已有功能） | `+patch`，例 `1.0.3.0 → 1.0.3.1` | commit message 前缀 `fix:`，或 issue 修复 |
| 新功能 | `+minor`，例 `1.0.3.0 → 1.1.0.0` | commit message 前缀 `feat:` |
| 破坏性变更 / API 改签名 | `+major`，例 `1.0.3.0 → 2.0.0.0` | 注释里写 Breaking Change |

如果本次发布同时包含 feat 和 fix，**取最高 bump 类型**（feat > fix）。

## 执行步骤

### 0. 版本 bump + CHANGELOG 版本块（**必须最先做**）

**a. bump version.json**：

```
node -e "const p=JSON.parse(require('fs').readFileSync('version.json','utf8')); const [a,b,c,d]=p.version.split('.').map(Number); const nv=`${a}.${b}.${c+1}.0`; console.log(nv); require('fs').writeFileSync('version.json', JSON.stringify({version:nv}, null, 2)+'\n');"
```

用 bump 后的版本号（记为 `{nextVersion}`）贯穿后续所有步骤。

**b. 写 CHANGELOG.md 顶部新版本块**：

- 从 `git log v{上一版本tag}..HEAD --oneline` 归纳 Added / Changed / Fixed / Tech 内容
- 标题行必须是：`## v{nextVersion} — {YYYY-MM-DD}`
- 上一个版本的版本块不动

**c. 提交版本 bump + CHANGELOG**：

```bash
git add version.json CHANGELOG.md
git commit -m "chore(release): bump to v{nextVersion} + CHANGELOG"
git push
```

### 1. 构建

```bash
node LcscLoader/build.js
```

从 `LcscLoader/bin/build-info.json` 校验三点：
- `git.dirty` 为 `false`
- `git.commit` 与 HEAD 一致
- `output.sha256` 记下来（稍后写进 Release Notes）

### 2. 提交未提交的改动（如有）

如果第 1 步之前 `git status` 不是 clean：

```bash
git add -A
git commit -m "chore: release v{nextVersion}"
git push
```

### 3. 打 tag 并推送

顺序必须是 **commit → tag → push**：

```bash
git tag -a v{nextVersion} -m "Release v{nextVersion}"
git push origin v{nextVersion}
```

### 4. 打包 zip

创建 `C:\Temp\lcscdeploy\release\v{nextVersion}\` 目录，把三个产物放进去：

```
LcscLoader\bin\LcscLoader.dll
LcscLoader\bin\LCSCLoader.Ins
LcscLoader\bin\build-info.json
```

然后压缩成 `LcscLoader-v{nextVersion}.zip`，并用 `Get-FileHash -Algorithm SHA256` 算出 zip 的 sha256。

### 5. 创建 GitHub Release

用 `gh release create` 带 `--target` 指定当前分支，附件是 zip。

Release Notes 必须包含：
- 安装方式一（自动部署）：git clone → build → deploy.js，关 AD 后提权部署
- 安装方式二（手动）：下载 zip → 关 AD → 复制 3 个文件到 AD System 目录覆盖
- 卸载命令：`node LcscLoader/deploy.js --uninstall --target system`
- 校验表：dll sha256、Ins sha256、zip sha256，三列表格

### 6. 收尾

`gh release view v{nextVersion} --json url` 拿到 Release URL，贴给用户。

## 已知坑

- **AD 独占锁**：运行中的 AD 会锁定 `System` 目录下的 dll，连改名都失败。手动部署前必须先 `Stop-Process -Name X2 -Force`。
- **提权**：`Start-Process node -Verb RunAs` 会触发 UAC，不能用非提权进程写 `Program Files`。
- **tag 顺序**：先 tag 后 commit 会让 tag 指向旧提交，历史不可追溯。
- **Windows 环境**：PowerShell 5 不支持 `cmd /c`、`mkdir -p`、`&&`；用 `New-Item -ItemType Directory -Force`、分号、`if ($LASTEXITCODE -eq 0)`。
- **restore-tools.js 在 Windows 上解压 .nupkg 失败**：`Expand-Archive` 只认 `.zip` 扩展名。绕过方式：手动用 Python `zipfile` 解压到 `_tools/<name>/` 即可，SHA256 校验仍走原脚本。
