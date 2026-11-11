using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace LcscLoader
{
    // 端点配置：内置默认值 + %AppData%\LCSCLoader\endpoints.json 覆盖。
    // hosts / timeout 在启动时用于创建 HttpClient，修改后需重启 AD 生效。
    internal sealed class EeEndpoints
    {
        public string ApiVersion = "6.4.19.5";

        public string[] ApiHosts = { "https://easyeda.com", "https://lceda.cn" };
        public string[] SearchHosts = { "https://easyeda.com" };
        public string[] ModuleHosts = { "https://modules.easyeda.com", "https://modules.lceda.cn" };

        public string ProductPath = "/api/products/{lcsc}/components";
        public string SearchPath = "/api/eda/product/list";
        public string ComponentPath = "/api/components/{uuid}";
        public string StepPath = "/qAxj6KHrDKw4blvCG8QJPs7Y/{uuid}";
        public string ObjPath = "/3dmodel/{uuid}";

        public int RequestTimeoutSeconds = 30;
        public int DownloadTimeoutSeconds = 180;

        private static EeEndpoints _current;

        public static EeEndpoints Current
        {
            get { return _current ?? (_current = Load()); }
        }

        public static string ConfigPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "LCSCLoader", "endpoints.json");
            }
        }

        private static EeEndpoints Load()
        {
            var config = new EeEndpoints();
            try
            {
                if (!File.Exists(ConfigPath)) return config;

                var root = JObject.Parse(File.ReadAllText(ConfigPath, Encoding.UTF8));
                config.ApiVersion = ReadString(root, "apiVersion", config.ApiVersion);
                config.ApiHosts = ReadHosts(root, "apiHosts", config.ApiHosts);
                config.SearchHosts = ReadHosts(root, "searchHosts", config.SearchHosts);
                config.ModuleHosts = ReadHosts(root, "moduleHosts", config.ModuleHosts);
                config.ProductPath = ReadString(root, "productPath", config.ProductPath);
                config.SearchPath = ReadString(root, "searchPath", config.SearchPath);
                config.ComponentPath = ReadString(root, "componentPath", config.ComponentPath);
                config.StepPath = ReadString(root, "stepPath", config.StepPath);
                config.ObjPath = ReadString(root, "objPath", config.ObjPath);
                config.RequestTimeoutSeconds = ReadSeconds(root, "requestTimeoutSeconds",
                    config.RequestTimeoutSeconds);
                config.DownloadTimeoutSeconds = ReadSeconds(root, "downloadTimeoutSeconds",
                    config.DownloadTimeoutSeconds);

                PluginLog.Write("endpoints.json loaded: " + ConfigPath);
            }
            catch (Exception ex)
            {
                PluginLog.Write("endpoints.json ignored: " + ex.Message);
            }
            return config;
        }

        private static string ReadString(JObject root, string name, string fallback)
        {
            var value = JsonUtil.Str(root, name);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string[] ReadHosts(JObject root, string name, string[] fallback)
        {
            var array = JsonUtil.Arr(root[name]);
            var hosts = new List<string>();
            foreach (var token in array)
            {
                var value = token.Type == JTokenType.String ? (string)token : token.ToString();
                if (string.IsNullOrWhiteSpace(value)) continue;
                hosts.Add(value.Trim().TrimEnd('/'));
            }
            return hosts.Count > 0 ? hosts.ToArray() : fallback;
        }

        private static int ReadSeconds(JObject root, string name, int fallback)
        {
            var seconds = (int)JsonUtil.Num(root, name, fallback);
            return seconds > 0 ? seconds : fallback;
        }
    }
}
