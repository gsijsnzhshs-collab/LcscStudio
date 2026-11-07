using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DXP;

namespace LcscLoader
{
    internal static class PluginLog
    {
        private static readonly object Sync = new object();

        public static void Write(string message)
        {
            try
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AltiumLCSC");
                Directory.CreateDirectory(directory);
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

    internal static class PluginContext
    {
        public static IClient Client;
        public static DXP.CommandLauncher Launcher;
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class LcscModule : ServerModule
    {
        private bool _registered;

        public LcscModule(IClient client) : base(client, "LCSCLoader")
        {
            PluginContext.Client = client;
            PluginLog.Write("LcscModule ctor");
            var timer = new System.Windows.Forms.Timer { Interval = 10000 };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                timer.Dispose();
                try { LcscWindow.Show(); }
                catch (Exception ex) { PluginLog.Write("auto show failed: " + ex); }
            };
            timer.Start();
        }

        protected override IServerDocument NewDocumentInstance(string kind, string fileName)
        {
            return null;
        }

        protected override void InitializeCommands()
        {
            PluginLog.Write("InitializeCommands");
            RegisterCommands();
        }

        protected override void ApplicationIdleImpl()
        {
            base.ApplicationIdleImpl();
            RegisterCommands();
        }

        private void RegisterCommands()
        {
            if (_registered) return;
            var launcher = CommandLauncher as DXP.CommandLauncher;
            if (launcher == null) return;

            PluginContext.Launcher = launcher;
            launcher.RegisterCommand("ShowLCSCLoader",
                new CommandProc(ShowLoader), new GetStateProc(GetLoaderState));
            _registered = true;
            PluginLog.Write("registered ShowLCSCLoader");
        }

        private void ShowLoader(IServerDocumentView view, ref string parameters)
        {
            PluginLog.Write("ShowLoader invoked");
            try
            {
                LcscWindow.Show();
            }
            catch (Exception ex)
            {
                PluginLog.Write("ShowLoader failed: " + ex);
                MessageBox.Show(ex.Message, "LCSC Loader", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        }

        private void GetLoaderState(
            IServerDocumentView view,
            ref string parameters,
            ref bool enabled,
            ref bool checkState,
            ref bool visible,
            ref string caption,
            ref string imageFile)
        {
            enabled = true;
            visible = true;
        }
    }
}

namespace CSharpPlugin
{
    public interface IPluginFactory
    {
        object InvokePluginFactory(IClient client);
    }

    // Altium looks up this exact type name when instantiating an extension.
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public class PluginFactory : IPluginFactory
    {
        public object InvokePluginFactory(IClient client)
        {
            LcscLoader.PluginContext.Client = client;
            LcscLoader.PluginLog.Write("InvokePluginFactory; assembly=" +
                typeof(PluginFactory).Assembly.Location);
            return new LcscLoader.LcscModule(client);
        }
    }
}
