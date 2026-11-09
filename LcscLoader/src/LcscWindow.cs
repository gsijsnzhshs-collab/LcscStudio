using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DXP;
using LcscStudio;

namespace LcscLoader
{
    internal static class LcscWindow
    {
        private static Form _form;

        public static void Show()
        {
            if (_form == null || _form.IsDisposed)
            {
                PluginLog.Write("creating panel form");
                var panel = new PanelForm { Dock = DockStyle.Fill };
                _form = new ToolForm
                {
                    Text = "LCSC ADesignK",
                    AutoScaleMode = AutoScaleMode.None,
                    Width = 780,
                    Height = 1100,
                    MinimumSize = new Size(660, 780),
                    FormBorderStyle = FormBorderStyle.SizableToolWindow,
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    MinimizeBox = false,
                    BackColor = Theme.Back,
                    Font = Theme.UiFont,
                };
                _form.Controls.Add(panel);
                _form.Show();

                var main = FindAdMainWindow();
                if (main != IntPtr.Zero) SetWindowLongPtr(_form.Handle, -8, main);
                PositionBesideAd(_form);
                KeepAboveAd();

                var keeper = new System.Windows.Forms.Timer { Interval = 1000 };
                keeper.Tick += (sender, args) => KeepAboveAd();
                keeper.Start();
            }
            else
            {
                _form.Show();
                var main = FindAdMainWindow();
                if (main != IntPtr.Zero) SetWindowLongPtr(_form.Handle, -8, main);
                KeepAboveAd();
            }

            PluginLog.Write("floating window shown bounds=" + _form.Bounds +
                            " owner=" + GetOwner(_form.Handle) +
                            " dpi=" + _form.DeviceDpi +
                            " screen=" + Screen.PrimaryScreen.WorkingArea);
        }

        private static void KeepAboveAd()
        {
            try
            {
                if (_form == null || _form.IsDisposed || !_form.Visible) return;
                var foreground = GetForegroundWindow();
                var myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                var adActive = ProcessIdOf(foreground) == myPid;
                SetWindowPos(_form.Handle, adActive ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch
            {
                // Z-order is best-effort.
            }
        }

        private static IntPtr FindAdMainWindow()
        {
            var myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            EnumWindows((hwnd, lParam) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                if (ProcessIdOf(hwnd) != myPid) return true;
                if (_form != null && hwnd == _form.Handle) return true;
                var length = GetWindowTextLength(hwnd);
                if (length <= 0) return true;
                RECT rect;
                if (!GetWindowRect(hwnd, out rect)) return true;
                var area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                if (area > bestArea)
                {
                    bestArea = area;
                    best = hwnd;
                }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        private static uint ProcessIdOf(IntPtr hwnd)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return pid;
        }

        private static IntPtr GetOwner(IntPtr hwnd)
        {
            try { return GetWindowLongPtr(hwnd, -8); } catch { return IntPtr.Zero; }
        }

        private sealed class ToolForm : Form
        {
            protected override bool ShowWithoutActivation => true;
        }

        private static void PositionBesideAd(Form form)
        {
            try
            {
                var area = Screen.PrimaryScreen.WorkingArea;
                var width = Math.Min(form.Width, Math.Max(660, area.Width - 40));
                var height = Math.Min(form.Height, Math.Max(700, area.Height - 80));
                form.Bounds = new Rectangle(
                    Math.Max(area.Left, area.Right - width - 20),
                    Math.Max(area.Top, area.Top + 40),
                    width,
                    height);
            }
            catch
            {
                // Keep the default bounds.
            }
        }

        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOACTIVATE = 0x0010;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
            int x, int y, int width, int height, int flags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hwnd, int index);

        private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hwnd, index)
                : GetWindowLong32(hwnd, index);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int index, IntPtr value);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, index, value)
                : SetWindowLong32(hWnd, index, value);
        }
    }
}
