using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace LcscSetup
{
    internal sealed class Options
    {
        public string Mode = "ui";   // ui | list | install | uninstall
        public string Dir;
        public string OutPath;

        public static Options Parse(string[] args)
        {
            var options = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "--list":
                        options.Mode = "list";
                        break;
                    case "--install":
                        options.Mode = "install";
                        options.Dir = Next(args, ref i, arg);
                        break;
                    case "--uninstall":
                        options.Mode = "uninstall";
                        options.Dir = Next(args, ref i, arg);
                        break;
                    case "--out":
                        options.OutPath = Next(args, ref i, arg);
                        break;
                    case "--silent-ui":
                        options.Mode = "ui";
                        break;
                    case "--help":
                    case "-h":
                        options.Mode = "help";
                        break;
                    default:
                        if (arg.StartsWith("--", StringComparison.Ordinal))
                            throw new ArgumentException("未知参数：" + arg);
                        break;
                }
            }
            return options;
        }

        private static string Next(string[] args, ref int index, string name)
        {
            if (index + 1 >= args.Length) throw new ArgumentException(name + " 缺少参数");
            return args[++index];
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Options options = null;
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            try
            {
                options = Options.Parse(args);
                if (options.Mode == "help")
                {
                    Write(options, Help());
                    return 0;
                }

                if (options.Mode == "ui")
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new SetupForm());
                    return 0;
                }

                return RunCommandLine(options);
            }
            catch (Exception ex)
            {
                Write(options, "ERROR: " + ex.Message);
                if (options == null || options.Mode == "ui")
                    MessageBox.Show(ex.Message, "LCSC ADesignK 安装器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int RunCommandLine(Options options)
        {
            var text = new StringBuilder();
            text.AppendLine("LCSC ADesignK · Altium 插件安装器 " + Payload.Info.Version);
            text.AppendLine("内置构建 " + Payload.Info.ShortHash + "   " + Payload.Info.Built);
            text.AppendLine();

            if (options.Mode == "list")
            {
                var installs = AdFinder.FindAll();
                text.AppendLine("检测到 " + installs.Count + " 个 Altium Designer：");
                foreach (var install in installs)
                    text.AppendLine("  AD" + install.ShortVersion + "  " + install.SystemDir + "  " + install.StatusText);
                if (installs.Count == 0)
                    text.AppendLine("  （未找到，可用 --install <System 目录> 显式指定）");
                Write(options, text.ToString());
                return 0;
            }

            if (string.IsNullOrWhiteSpace(options.Dir))
                throw new ArgumentException(options.Mode + " 需要目标 System 目录");

            var list = new List<AdInstall>();
            AdFinder.Add(list, Path.GetFullPath(options.Dir), "命令行");
            if (list.Count == 0)
                throw new InvalidOperationException("目标目录不像 Altium 的 System 目录（父目录缺少 X2.EXE）：" + options.Dir);
            var target = list[0];

            Action<string> log = line =>
            {
                text.AppendLine(line);
                Console.WriteLine(line);
            };

            text.AppendLine("目标：" + target.SystemDir);
            var message = options.Mode == "install"
                ? Installer.Install(target, true, log)
                : Installer.Uninstall(target, log);
            text.AppendLine(message);
            Write(options, text.ToString());
            return 0;
        }

        private static void Write(Options options, string text)
        {
            if (options != null && !string.IsNullOrWhiteSpace(options.OutPath))
            {
                File.WriteAllText(options.OutPath, text, new UTF8Encoding(false));
                return;
            }
            try
            {
                if (Environment.UserInteractive)
                {
                    var console = GetConsoleWindow();
                    if (console != IntPtr.Zero) Console.WriteLine(text);
                }
            }
            catch
            {
                // 无控制台时静默。
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        private static string Help()
        {
            return string.Join(Environment.NewLine,
                "LCSC ADesignK · Altium 插件安装器 " + Payload.Info.Version,
                "",
                "无参数                  打开图形界面",
                "--list                  列出检测到的 Altium Designer",
                "--install <System 目录>  安装到指定目录",
                "--uninstall <System 目录> 从指定目录卸载",
                "--out <文件>            把结果写入文件（GUI 之外使用）",
                "",
                "示例：",
                "  LcscSetup.exe --list --out result.txt",
                "  LcscSetup.exe --install \"C:\\Program Files\\Altium\\AD22\\System\"");
        }
    }
}

