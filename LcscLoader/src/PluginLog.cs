using System;
using System.IO;

namespace LcscLoader
{
    // 独立于 AD SDK：解析器与离线测试也会写这行日志。
    internal static class PluginLog
    {
        private static readonly object Sync = new object();

        public static void Write(string message)
        {
            try
            {
                var directory = StoragePaths.LogDir;
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message +
                           Environment.NewLine;
                lock (Sync)
                {
                    File.AppendAllText(Path.Combine(directory, "plugin.log"), line);
                }
            }
            catch
            {
                // Diagnostics must never break the plugin.
            }
        }
    }
}
