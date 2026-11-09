using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace LcscSetup
{
    internal sealed class AdInstall
    {
        public string SystemDir = "";
        public string ExePath = "";
        public string DisplayVersion = "";
        public string InstalledVersion = "";
        public string InstalledHash = "";
        public string Source = "";

        public bool Installed
        {
            get { return !string.IsNullOrEmpty(InstalledVersion); }
        }

        public bool SameAsPayload
        {
            get
            {
                return Installed &&
                       string.Equals(InstalledHash, Payload.Info.DllSha256, StringComparison.OrdinalIgnoreCase);
            }
        }

        public string StatusText
        {
            get
            {
                if (!Installed) return "未安装";
                if (SameAsPayload) return "已安装 " + InstalledVersion + "（与安装包一致）";
                return "已安装 " + InstalledVersion + "（可覆盖更新）";
            }
        }

        public string ShortVersion
        {
            get
            {
                var text = DisplayVersion;
                if (string.IsNullOrEmpty(text)) text = "未知版本";
                var parts = text.Split('.');
                if (parts.Length > 3) text = string.Join(".", new[] { parts[0], parts[1], parts[2] });
                return text;
            }
        }
    }

    internal static class AdFinder
    {
        private sealed class Hit
        {
            public string Dir = "";
            public string Version = "";
        }

        public static List<AdInstall> FindAll()
        {
            var found = new List<AdInstall>();
            foreach (var hit in Candidates()) Add(found, hit.Dir, "自动检测", hit.Version);
            found.Sort((a, b) => string.Compare(a.ShortVersion, b.ShortVersion, StringComparison.OrdinalIgnoreCase));
            return found;
        }

        private static IEnumerable<Hit> Candidates()
        {
            foreach (var hit in FromRegistry()) yield return hit;

            foreach (var root in CommonRoots())
            {
                if (!Directory.Exists(root)) continue;
                string[] children;
                try { children = Directory.GetDirectories(root); }
                catch { continue; }
                foreach (var child in children)
                {
                    yield return new Hit { Dir = Path.Combine(child, "System") };
                    yield return new Hit { Dir = child };
                }
            }
        }

        private static IEnumerable<Hit> FromRegistry()
        {
            var results = new List<Hit>();
            var subKeys = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            };

            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var subKey in subKeys)
                {
                    try
                    {
                        using (var key = hive.OpenSubKey(subKey))
                        {
                            if (key == null) continue;
                            foreach (var name in key.GetSubKeyNames())
                            {
                                try
                                {
                                    using (var item = key.OpenSubKey(name))
                                    {
                                        if (item == null) continue;
                                        var display = item.GetValue("DisplayName") as string;
                                        if (string.IsNullOrEmpty(display) ||
                                            display.IndexOf("Altium Designer", StringComparison.OrdinalIgnoreCase) < 0)
                                            continue;

                                        var version = (item.GetValue("DisplayVersion") as string ?? "").Trim();
                                        if (version.Length == 0)
                                        {
                                            var number = System.Text.RegularExpressions.Regex.Match(
                                                display, @"(\d+(?:\.\d+)+)");
                                            if (number.Success) version = number.Groups[1].Value;
                                        }

                                        var location = item.GetValue("InstallLocation") as string;
                                        if (!string.IsNullOrEmpty(location))
                                        {
                                            results.Add(new Hit { Dir = Path.Combine(location, "System"), Version = version });
                                            results.Add(new Hit { Dir = location, Version = version });
                                        }

                                        var icon = item.GetValue("DisplayIcon") as string;
                                        if (!string.IsNullOrEmpty(icon))
                                        {
                                            icon = icon.Trim('"');
                                            var comma = icon.LastIndexOf(',');
                                            if (comma > 2 && icon.Substring(comma + 1).Trim().TrimStart('-').Length <= 5)
                                                icon = icon.Substring(0, comma);
                                            if (icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                                results.Add(new Hit { Dir = Path.GetDirectoryName(icon), Version = version });
                                        }
                                    }
                                }
                                catch
                                {
                                    // A broken uninstall entry must not stop the scan.
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Registry access is best-effort.
                    }
                }
            }
            return results;
        }

        private static IEnumerable<string> CommonRoots()
        {
            var roots = new List<string>();
            foreach (var programFiles in new[]
                     {
                         Environment.GetEnvironmentVariable("ProgramFiles"),
                         Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                     })
            {
                if (!string.IsNullOrEmpty(programFiles))
                    roots.Add(Path.Combine(programFiles, "Altium"));
            }

            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed) continue;
                }
                catch
                {
                    continue;
                }
                roots.Add(Path.Combine(drive.Name, "AD"));
                roots.Add(Path.Combine(drive.Name, "Altium"));
            }
            return roots;
        }

        // A usable target is a System directory whose parent holds X2.EXE.
        public static void Add(List<AdInstall> list, string candidate, string source, string displayVersion = null)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return;
            string dir;
            try { dir = Path.GetFullPath(candidate).TrimEnd('\\'); }
            catch { return; }

            var systemDir = Path.GetFileName(dir).Equals("System", StringComparison.OrdinalIgnoreCase)
                ? dir
                : Path.Combine(dir, "System");
            var exePath = Path.Combine(Path.GetDirectoryName(systemDir) ?? "", "X2.EXE");
            if (!File.Exists(exePath)) return;
            if (!Directory.Exists(systemDir)) return;

            foreach (var existing in list)
            {
                if (string.Equals(existing.SystemDir, systemDir, StringComparison.OrdinalIgnoreCase)) return;
            }

            var install = new AdInstall
            {
                SystemDir = systemDir,
                ExePath = exePath,
                DisplayVersion = ResolveVersion(exePath, displayVersion, systemDir),
                Source = source,
            };
            ReadInstalled(install);
            list.Add(install);
        }

        // X2.EXE usually carries no version resource, so prefer the registry value
        // and fall back to the folder name (AD22 → 22).
        private static string ResolveVersion(string exePath, string displayVersion, string systemDir)
        {
            try
            {
                var product = FileVersionInfo.GetVersionInfo(exePath).ProductVersion;
                if (!string.IsNullOrWhiteSpace(product)) return product.Trim();
            }
            catch
            {
                // Fall through.
            }

            if (!string.IsNullOrWhiteSpace(displayVersion)) return displayVersion.Trim();

            var match = System.Text.RegularExpressions.Regex.Match(
                systemDir, @"[\\/](?:AD|Altium Designer)[ _-]?(\d{2})(?:[\\/]|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "未知版本";
        }

        private static void ReadInstalled(AdInstall install)
        {
            var dll = Path.Combine(install.SystemDir, "LcscLoader.dll");
            if (!File.Exists(dll)) return;
            install.InstalledHash = Hashes.Sha256(dll);
            try { install.InstalledVersion = FileVersionInfo.GetVersionInfo(dll).FileVersion ?? "?"; }
            catch { install.InstalledVersion = "?"; }
        }
    }

    internal static class Installer
    {
        public static bool AltiumRunning(out string detail)
        {
            var running = Process.GetProcessesByName("X2");
            if (running.Length == 0)
            {
                detail = null;
                return false;
            }
            detail = "Altium Designer 正在运行（X2.EXE，PID " + running[0].Id + "），请先完全关闭再操作。";
            foreach (var process in running) process.Dispose();
            return true;
        }

        public static string Install(AdInstall install, bool backup, Action<string> log)
        {
            if (AltiumRunning(out var runningDetail)) throw new InvalidOperationException(runningDetail);

            Directory.CreateDirectory(install.SystemDir);
            var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            foreach (var name in Payload.FileNames)
            {
                var target = Path.Combine(install.SystemDir, name);
                if (backup && File.Exists(target))
                {
                    var backupPath = target + ".bak-" + stamp;
                    File.Copy(target, backupPath, true);
                    log("  备份 " + name + " → " + Path.GetFileName(backupPath));
                }
                File.WriteAllBytes(target, Payload.For(name));
                var actual = Hashes.Sha256(target);
                var expected = Payload.ExpectedHash(name);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(name + " 写入后哈希不一致：" + actual);
                log("  写入 " + name + "  " + actual.Substring(0, 12) + "…");
            }

            ReadInstalledState(install);
            return "已安装到 " + install.SystemDir + "，请启动 Altium Designer。";
        }

        public static string Uninstall(AdInstall install, Action<string> log)
        {
            if (AltiumRunning(out var runningDetail)) throw new InvalidOperationException(runningDetail);

            var removed = new List<string>();
            foreach (var name in Payload.FileNames)
            {
                var target = Path.Combine(install.SystemDir, name);
                if (!File.Exists(target)) continue;
                File.Delete(target);
                removed.Add(name);
                log("  删除 " + name);
            }

            var registryNote = RemoveProgramDataEntry(log);
            install.InstalledVersion = "";
            install.InstalledHash = "";

            if (removed.Count == 0 && registryNote.Length == 0)
                return "没有找到已安装的文件：" + install.SystemDir;
            return "已从 " + install.SystemDir + " 卸载" +
                   (registryNote.Length > 0 ? "；" + registryNote : "") + "。";
        }

        private static void ReadInstalledState(AdInstall install)
        {
            var dll = Path.Combine(install.SystemDir, "LcscLoader.dll");
            if (!File.Exists(dll)) return;
            install.InstalledHash = Hashes.Sha256(dll);
            try { install.InstalledVersion = FileVersionInfo.GetVersionInfo(dll).FileVersion ?? "?"; }
            catch { install.InstalledVersion = "?"; }
        }

        // programdata 布局（旧版部署方式）也一并清理，避免残留项让 AD 加载到旧副本。
        private static string RemoveProgramDataEntry(Action<string> log)
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Altium");
            if (!Directory.Exists(root)) return "";

            var notes = new List<string>();
            foreach (var dataDir in Directory.GetDirectories(root))
            {
                var extensions = Path.Combine(dataDir, "Extensions");
                var registry = Path.Combine(extensions, "ExtensionsRegistry.xml");
                if (File.Exists(registry))
                {
                    try
                    {
                        var xml = File.ReadAllText(registry);
                        var updated = System.Text.RegularExpressions.Regex.Replace(
                            xml, @"[ \t]*<Item HRID=""LCSCLoader""[\s\S]*?</Item>\r?\n?", "");
                        if (updated != xml)
                        {
                            File.WriteAllText(registry, updated);
                            notes.Add("已移除注册表项 " + registry);
                            log("  移除 ExtensionsRegistry.xml 中的 LCSCLoader 项");
                        }
                    }
                    catch (Exception ex)
                    {
                        notes.Add("注册表项清理失败：" + ex.Message);
                    }
                }

                var folder = Path.Combine(extensions, "LCSCLoader");
                if (Directory.Exists(folder))
                {
                    try
                    {
                        Directory.Delete(folder, true);
                        notes.Add("已删除旧目录 " + folder);
                        log("  删除 " + folder);
                    }
                    catch (Exception ex)
                    {
                        notes.Add("旧目录删除失败：" + ex.Message);
                    }
                }
            }
            return string.Join("；", notes);
        }
    }

    internal static class Hashes
    {
        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var bytes = sha.ComputeHash(stream);
                var builder = new System.Text.StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) builder.Append(b.ToString("X2"));
                return builder.ToString();
            }
        }
    }
}
