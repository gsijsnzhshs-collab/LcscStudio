using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DXP;

namespace LcscLoader
{
    // Hosts the LCSC panel as a native Altium docked panel instead of a floating window.
    // Metadata mirrors LCSCLoader.Ins PanelInfo (Name/Category/Hotkey/docking flags).
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class LcscPanelForm : ServerPanelForm, IServerPanelInfo
    {
        internal const string PanelName = "LCSCLoader";
        internal const string PanelCaption = "立创选料";

        private Size _lastSize;

        public LcscPanelForm()
        {
            Text = PanelCaption;
            AutoScaleMode = AutoScaleMode.None;
            Controls.Add(new LcscStudio.PanelForm { Dock = DockStyle.Fill });

            // AD can host the panel in a narrower container than ServerPanelForm's
            // default size; adopt the host size so content is never clipped.
            var fitTimer = new Timer { Interval = 800 };
            var ticks = 0;
            fitTimer.Tick += (s, e) =>
            {
                if (++ticks > 6) { fitTimer.Stop(); fitTimer.Dispose(); return; }
                FitToHost();
            };
            fitTimer.Start();
        }

        private void FitToHost()
        {
            var host = Parent;
            if (host == null || host.ClientSize.Width < 200) return;
            if (Width == host.ClientSize.Width && Height == host.ClientSize.Height) return;
            LcscLoader.PluginLog.Write("docked panel fit " + Width + "x" + Height +
                " -> " + host.ClientSize.Width + "x" + host.ClientSize.Height);
            Size = host.ClientSize;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LcscLoader.PluginLog.Write("docked panel load size=" + Width + "x" + Height +
                " client=" + ClientSize.Width + "x" + ClientSize.Height +
                " parent=" + (Parent == null ? "null" : Parent.Width + "x" + Parent.Height));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Size == _lastSize) return;
            _lastSize = Size;
            LcscLoader.PluginLog.Write("docked panel resize size=" + Width + "x" + Height +
                " client=" + ClientSize.Width + "x" + ClientSize.Height +
                " parent=" + (Parent == null ? "null" : Parent.Width + "x" + Parent.Height));
        }

        public string GetName() { return PanelName; }
        public string GetCategory() { return "&System"; }
        public string GetBitmap() { return string.Empty; }
        public string GetHotkey() { return "L"; }
        public bool GetButtonVisible() { return true; }
        public bool GetMultipleCreation() { return false; }
        public string GetCreationClassName() { return string.Empty; }
        public bool GetCanDockVertical() { return true; }
        public bool GetCanDockHorizontal() { return false; }
        public bool SupportsDocumentKind(string argKind) { return true; }
        public bool SupportsProjectKind(string argKind) { return true; }
        public int GetDocumentKindCount() { return 0; }
        public string GetDocumentKinds(int argIndex) { return string.Empty; }
        public int GetProjectKindCount() { return 0; }
        public string GetProjectKinds(int argIndex) { return string.Empty; }
    }

    // DXP.ServerPanelView is marked abstract (though it has no abstract members),
    // so a thin concrete subclass is required before it can be instantiated.
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class LcscPanelView : ServerPanelView
    {
        public LcscPanelView(ServerPanelForm form, string viewName, string caption)
            : base(form, viewName, caption)
        {
        }
    }
}
