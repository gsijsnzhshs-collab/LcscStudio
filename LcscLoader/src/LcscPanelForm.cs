using System;
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

        public LcscPanelForm()
        {
            Text = PanelCaption;
            AutoScaleMode = AutoScaleMode.None;
            Controls.Add(new LcscStudio.PanelForm { Dock = DockStyle.Fill });
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
