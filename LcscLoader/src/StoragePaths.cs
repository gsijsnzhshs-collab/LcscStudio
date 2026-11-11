using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace LcscLoader
{
    // 统一管理 plugin.log / 3D 缓存 / settings / 新建库 的根路径。
    // 优先读插件 dll 旁边的 paths.json（安装器部署时写入，允许用户自定义）；
    // 不存在时用 .NET SpecialFolder 默认值，保持向后兼容。
    internal static class StoragePaths
    {
        private static readonly object Sync = new object();
        private static bool _initialized;
        private static string _logDir;
        private static string _cacheDir;
        private static string _settingsDir;
        private static string _libraryDir;

        public static string LogDir
        {
            get { Ensure(); return _logDir; }
        }

        public static string CacheDir
        {
            get { Ensure(); return _cacheDir; }
        }

        public static string SettingsDir
        {
            get { Ensure(); return _settingsDir; }
        }

        public static string DefaultLibraryFolder
        {
            get { Ensure(); return _libraryDir; }
        }

        internal static void Reset()
        {
            lock (Sync)
            {
                _initialized = false;
                _logDir = _cacheDir = _settingsDir = _libraryDir = null;
            }
        }

        private static void Ensure()
        {
            lock (Sync)
            {
                if (_initialized) return;
                _initialized = true;

                string jsonPath = null;
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var dir = Path.GetDirectoryName(asm.Location);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        jsonPath = Path.Combine(dir, "paths.json");
                        if (File.Exists(jsonPath))
                        {
                            var root = JObject.Parse(File.ReadAllText(jsonPath));
                            _logDir = GetString(root, "logDir");
                            _cacheDir = GetString(root, "cacheDir");
                            _settingsDir = GetString(root, "settingsDir");
                            _libraryDir = GetString(root, "libraryDir");
                        }
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Write("paths.json 加载失败：" + ex.Message + "（文件：" + jsonPath + "）");
                }

                // 任意字段缺失 → 回退默认值
                if (string.IsNullOrWhiteSpace(_logDir))
                    _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AltiumLCSC");
                if (string.IsNullOrWhiteSpace(_cacheDir))
                    _cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LCSCLoader", "cache");
                if (string.IsNullOrWhiteSpace(_settingsDir))
                    _settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LCSCLoader");
                if (string.IsNullOrWhiteSpace(_libraryDir))
                    _libraryDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AltiumLCSC");

                try { Directory.CreateDirectory(_logDir); } catch { }
                try { Directory.CreateDirectory(_cacheDir); } catch { }
                try { Directory.CreateDirectory(_settingsDir); } catch { }
                try { Directory.CreateDirectory(_libraryDir); } catch { }
            }
        }

        private static string GetString(JObject root, string name)
        {
            var token = root[name];
            if (token == null || token.Type != JTokenType.String) return null;
            var value = (string)token;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
