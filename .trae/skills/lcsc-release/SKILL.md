---
name: lcsc-release
description: Build, tag, and publish LcscLoader releases to GitHub. Use when the user asks to tag, publish, ship, or make a new LcscLoader version for others to download. Do not use for day-to-day commits or plain pushes.
---

# LcscLoader 发布流程

## 前置校验（先全部过一遍，再动手）

1. **版本号源**：读取 `version.json` 里的 `version` 字段（例如 `"1.0.3.0"`）。所有产物版本、tag 名、Release 标题必须与此一致。
2. **工作区干净**：`git status` 必须显示 `nothing to commit, working tree clean`。如果有改动，先提交并 push 到当前分支，再回来继续。
3. **gh CLI 登录**：`gh auth status` 返回 `Logged in to github.com`。
4. **远端 tag 未占用**：`git tag -l "v{version}"` 应返回空。

## 执行步骤

### 1. 构建

```bash
node LcscLoader/build.js
```

从 `LcscLoader/bin/build-info.json` 校验三点：
- `git.dirty` 为 `false`（构建时工作区确实干净）
- `git.commit` 与 HEAD 一致
- `output.sha256` 记下来（稍后写进 Release Notes）

### 2. 提交未提交的改动（如有）

如果第 1 步之前 `git status` 不是 clean：

```bash
git add -A
git commit -m "chore: release v{version}"
git push
```

### 3. 打 tag 并推送

顺序必须是 **commit → tag → push**，不能先 tag：

```bash
git tag -a v{version} -m "Release v{version}"
git push origin v{version}
```

### 4. 打包 zip

创建 `C:\Temp\lcscdeploy\release\v{version}\` 目录，把三个产物放进去：

```
LcscLoader\bin\LcscLoader.dll
LcscLoader\bin\LCSCLoader.Ins
LcscLoader\bin\build-info.json
```

然后压缩成 `LcscLoader-v{version}.zip`，并用 `Get-FileHash -Algorithm SHA256` 算出 zip 的 sha256。

### 5. 创建 GitHub Release

用 `gh release create` 带 `--target` 指定当前分支，附件是 zip。

Release Notes 必须包含：
- **安装方式一（自动部署）**：git clone → build → deploy.js，关 AD 后 `Start-Process -Verb RunAs node -ArgumentList "LcscLoader/deploy.js --target system"`
- **安装方式二（手动）**：下载 zip → 关 AD22 → 复制 3 个文件到 `C:\Program Files\Altium\AD22\System\` 覆盖
- **卸载命令**：`node LcscLoader/deploy.js --uninstall --target system`
- **校验表**：dll sha256、Ins sha256、zip sha256，三列表格

### 6. 收尾

`gh release view v{version} --json url` 拿到 Release URL，贴给用户。

## 已知坑

- **AD 独占锁**：运行中的 AD 会锁定 `System` 目录下的 dll，连改名都失败。手动部署前必须先 `Stop-Process -Name X2 -Force`。
- **提权**：`Start-Process node -Verb RunAs` 会触发 UAC，不能用非提权进程写 `Program Files`。
- **tag 顺序**：先 tag 后 commit 会让 tag 指向旧提交，历史不可追溯。
- **Windows 环境**：PowerShell 5 不支持 `cmd /c`、`mkdir -p`、`&&`；用 `New-Item -ItemType Directory -Force`、分号、`if ($LASTEXITCODE -eq 0)`。
