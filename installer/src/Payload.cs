using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace LcscSetup
{
    // 内嵌的插件产物。安装器不依赖外部文件，拷走一个 exe 就能用。
    internal static class Payload
    {
        public const string DllResource = "payload.LcscLoader.dll";
        public const string InsResource = "payload.LCSCLoader.Ins";
        public const string RcsResource = "payload.LCSCLoader.rcs";
        private const string InfoResource = "payload.payload.info";

        public static readonly string[] FileNames = { "LcscLoader.dll", "LCSCLoader.Ins", "LCSCLoader.rcs" };

        private static PayloadInfo _info;

        public static PayloadInfo Info
        {
            get { return _info ?? (_info = PayloadInfo.Parse(ReadText(InfoResource))); }
        }

        public static byte[] Read(string resource)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) throw new InvalidOperationException("安装包缺少资源：" + resource);
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    return memory.ToArray();
                }
            }
        }

        private static string ReadText(string resource)
        {
            return Encoding.UTF8.GetString(Read(resource));
        }

        public static byte[] For(string fileName)
        {
            switch (fileName)
            {
                case "LcscLoader.dll": return Read(DllResource);
                case "LCSCLoader.Ins": return Read(InsResource);
                case "LCSCLoader.rcs": return Read(RcsResource);
                default: throw new ArgumentOutOfRangeException("fileName");
            }
        }

        public static string ExpectedHash(string fileName)
        {
            var info = Info;
            switch (fileName)
            {
                case "LcscLoader.dll": return info.DllSha256;
                case "LCSCLoader.Ins": return info.InsSha256;
                case "LCSCLoader.rcs": return info.RcsSha256;
                default: return "";
            }
        }
    }

    internal sealed class PayloadInfo
    {
        public string Version = "unknown";
        public string Built = "";
        public string DllSha256 = "";
        public string InsSha256 = "";
        public string RcsSha256 = "";

        public string ShortHash
        {
            get { return DllSha256.Length >= 12 ? DllSha256.Substring(0, 12) + "…" : DllSha256; }
        }

        public static PayloadInfo Parse(string text)
        {
            var info = new PayloadInfo();
            foreach (var raw in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var index = raw.IndexOf('=');
                if (index <= 0) continue;
                var key = raw.Substring(0, index).Trim();
                var value = raw.Substring(index + 1).Trim();
                switch (key)
                {
                    case "version": info.Version = value; break;
                    case "built": info.Built = value; break;
                    case "dllSha256": info.DllSha256 = value; break;
                    case "insSha256": info.InsSha256 = value; break;
                    case "rcsSha256": info.RcsSha256 = value; break;
                }
            }
            return info;
        }
    }
}
