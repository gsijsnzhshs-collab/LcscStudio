using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DXP;

namespace LcscLoader
{
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
        private bool _panelRegistrationTried;
        private IServerView _panelView;

        public LcscModule(IClient client) : base(client, "LCSCLoader")
        {
            PluginContext.Client = client;
            PluginLog.Write("LcscModule ctor");
        }

        protected override IServerDocument NewDocumentInstance(string kind, string fileName)
        {
            return null;
        }

        protected override void InitializeCommands()
        {
            PluginLog.Write("InitializeCommands");
            RegisterCommands();
            EnsurePanelView();
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
                ShowPanel();
            }
            catch (Exception ex)
            {
                PluginLog.Write("ShowLoader failed: " + ex);
                MessageBox.Show(ex.Message, "LCSC Loader", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        }

        private void EnsurePanelView()
        {
            if (_panelView != null || _panelRegistrationTried) return;
            _panelRegistrationTried = true;
            try
            {
                var form = new LcscPanelForm();
                var view = new LcscPanelView(form, LcscPanelForm.PanelName, LcscPanelForm.PanelCaption);
                AddView(view);
                var client = PluginContext.Client;
                if (client != null) client.AddServerView(view);
                _panelView = view;
                PluginLog.Write("panel view registered");
            }
            catch (Exception ex)
            {
                PluginLog.Write("panel view registration failed: " + ex);
            }
        }

        // Prefer the native docked panel; fall back to the floating window if the
        // SDK refuses the registration or the GUI manager cannot show it.
        private void ShowPanel()
        {
            try
            {
                EnsurePanelView();
                if (_panelView != null)
                {
                    var gui = PluginContext.Client != null ? PluginContext.Client.GetGUIManager() : null;
                    if (gui != null)
                    {
                        gui.SetPanelVisibleInCurrentForm(LcscPanelForm.PanelName, true);
                        gui.SetPanelActiveInCurrentForm(LcscPanelForm.PanelName);
                        PluginLog.Write("panel shown via GUIManager");
                        return;
                    }
                    _panelView.Show();
                    PluginLog.Write("panel shown via IServerView.Show");
                    return;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Write("docked panel show failed: " + ex);
            }
            LcscWindow.Show();
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
