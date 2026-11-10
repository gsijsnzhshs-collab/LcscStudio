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
        internal const string PanelCaption = "ADesignK";

        private Size _lastSize;

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        public LcscPanelForm()
        {
            Text = PanelCaption;
            AutoScaleMode = AutoScaleMode.None;
            Controls.Add(new LcscStudio.PanelForm { Dock = DockStyle.Fill });

            // 面板加载后 AD 会把宿主窗体调整到停靠容器大小，加载时对齐一次即可。
            // 之前这里挂了 1.2s 轮询反复改写 Size，会和停靠管理器互相抢宽度，
            // 表现为日志里成对的 width=249/268 与面板被收成右侧竖标签。
        }

        private void FitToHost()
        {
            if (!IsHandleCreated) return;
            var host = GetParent(Handle);
            if (host == IntPtr.Zero)
            {
                LcscLoader.PluginLog.Write("docked panel fit: no host window");
                return;
            }
            RECT rect;
            if (!GetClientRect(host, out rect))
            {
                LcscLoader.PluginLog.Write("docked panel fit: GetClientRect failed host=" + host);
                return;
            }
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width < 200 || height < 200)
            {
                LcscLoader.PluginLog.Write("docked panel fit: host too small " + width + "x" + height);
                return;
            }
            if (Width == width && Height == height) return;
            LcscLoader.PluginLog.Write("docked panel fit " + Width + "x" + Height +
                " -> host " + width + "x" + height);
            Size = new Size(width, height);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LcscLoader.PluginLog.Write("docked panel load size=" + Width + "x" + Height +
                " client=" + ClientSize.Width + "x" + ClientSize.Height +
                " parent=" + (Parent == null ? "null" : Parent.Width + "x" + Parent.Height));
            FitToHost();
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
