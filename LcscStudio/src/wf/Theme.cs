using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LcscStudio
{
    // Central dark palette tuned to Altium Designer's dark UI.
    internal static class Theme
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static readonly Color Back = Color.FromArgb(0x2B, 0x2B, 0x2B);
        public static readonly Color Surface = Color.FromArgb(0x33, 0x33, 0x33);
        public static readonly Color SurfaceAlt = Color.FromArgb(0x3A, 0x3A, 0x3A);
        public static readonly Color InputBack = Color.FromArgb(0x1E, 0x1F, 0x22);
        public static readonly Color Border = Color.FromArgb(0x47, 0x47, 0x47);
        public static readonly Color Text = Color.FromArgb(0xE3, 0xE3, 0xE3);
        public static readonly Color TextMuted = Color.FromArgb(0xB5, 0xB5, 0xB5);
        public static readonly Color Accent = Color.FromArgb(0x3C, 0x9B, 0xD8);
        public static readonly Color AccentHover = Color.FromArgb(0x4F, 0xB0, 0xEC);
        public static readonly Color AccentText = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static readonly Color Canvas = Color.FromArgb(0x1E, 0x1E, 0x1E);
        public static readonly Color CanvasGrid = Color.FromArgb(0x2A, 0x2A, 0x2A);
        public static readonly Color CanvasInk = Color.FromArgb(0xD0, 0xD0, 0xD0);
        public static readonly Color PinInk = Color.FromArgb(0xE0, 0x8A, 0x8A);
        public static readonly Color Success = Color.FromArgb(0x5C, 0xB8, 0x5C);
        public static readonly Color Error = Color.FromArgb(0xE0, 0x5A, 0x5A);

        public static readonly Font UiFont = new Font("Microsoft YaHei UI", 9f);
        public static readonly Font TitleFont = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
        public static readonly Font SmallFont = new Font("Microsoft YaHei UI", 9f);
        public static readonly Font MonoFont = new Font("Consolas", 8.5f);

        public static void StyleForm(Form form)
        {
            form.BackColor = Back;
            form.ForeColor = Text;
            form.Font = UiFont;
            form.AutoScaleDimensions = new SizeF(96f, 96f);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            EnableDarkTitleBar(form);
        }

        public static void EnableDarkTitleBar(Form form)
        {
            void Apply()
            {
                try
                {
                    var enabled = 1;
                    if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)) != 0)
                        DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
                }
                catch
                {
                    // Older Windows versions simply keep the default title bar.
                }
            }
            if (form.IsHandleCreated) Apply();
            else form.HandleCreated += (s, e) => Apply();
        }

        // Turns the native list/text scrollbars dark through the Explorer theme.
        public static void EnableDarkScrollbars(Control control)
        {
            void Apply()
            {
                try
                {
                    SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
                }
                catch
                {
                    // Non-themed scrollbars remain light; purely cosmetic.
                }
            }
            if (control.IsHandleCreated) Apply();
            else control.HandleCreated += (s, e) => Apply();
        }

        public static void AddTopBorder(Control control, Color? color = null)
        {
            var penColor = color ?? Border;
            control.Paint += (s, e) =>
            {
                using (var pen = new Pen(penColor))
                    e.Graphics.DrawLine(pen, 0, 0, control.Width, 0);
            };
        }

        public static void AddBottomBorder(Control control, Color? color = null)
        {
            var penColor = color ?? Border;
            control.Paint += (s, e) =>
            {
                using (var pen = new Pen(penColor))
                    e.Graphics.DrawLine(pen, 0, control.Height - 1, control.Width, control.Height - 1);
            };
        }

        public static Button StyleButton(Button button, bool primary = false)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.BackColor = primary ? Accent : SurfaceAlt;
            button.ForeColor = primary ? AccentText : Text;
            button.Font = UiFont;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            var normal = button.BackColor;
            var hover = primary ? AccentHover : Color.FromArgb(0x46, 0x49, 0x4D);
            button.MouseEnter += (s, e) => { if (button.Enabled) button.BackColor = hover; };
            button.MouseLeave += (s, e) => { button.BackColor = normal; };
            button.EnabledChanged += (s, e) =>
            {
                button.BackColor = button.Enabled ? normal : Surface;
                button.ForeColor = button.Enabled ? (primary ? AccentText : Text) : TextMuted;
            };
            return button;
        }

        // Fixed-height, text-measured width: AutoSize alone under-measures flat
        // buttons and clips the glyph bottoms on high-DPI or narrow panels.
        public static void SizeToText(Button button, int height, int minWidth = 0)
        {
            button.AutoSize = false;
            button.Height = height;
            button.TextAlign = ContentAlignment.MiddleCenter;
            var measured = TextRenderer.MeasureText(button.Text, button.Font).Width;
            button.Width = Math.Max(minWidth, measured + 22);
        }

        public static void SetTabActive(Button button, bool active)
        {
            button.BackColor = active ? Accent : SurfaceAlt;
            button.ForeColor = active ? AccentText : TextMuted;
            button.FlatAppearance.BorderColor = active ? Accent : Border;
        }

        public static void StyleTextBox(TextBox box, bool multiline = false)
        {
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = InputBack;
            box.ForeColor = Text;
            box.Font = multiline ? MonoFont : UiFont;
        }

        public static void StyleList(ListView list)
        {
            list.BackColor = InputBack;
            list.ForeColor = Text;
            list.BorderStyle = BorderStyle.None;
            list.Font = UiFont;
        }

        public static void StyleLabel(Label label, bool muted = false)
        {
            label.ForeColor = muted ? TextMuted : Text;
            label.BackColor = Color.Transparent;
        }

        public static void ApplyButtons(Control root, params Button[] primary)
        {
            foreach (Control control in root.Controls)
            {
                if (control is Button button)
                {
                    if (Array.IndexOf(primary, button) < 0) StyleButton(button);
                }
                ApplyButtons(control, primary);
            }
        }

        public static void ApplyRecursive(Control root)
        {
            foreach (Control control in root.Controls)
            {
                if (control is TextBox box)
                {
                    StyleTextBox(box, box.Multiline);
                }
                else if (control is ListView list)
                {
                    StyleList(list);
                }
                else if (control is CheckBox check)
                {
                    check.FlatStyle = FlatStyle.Flat;
                    check.BackColor = Color.Transparent;
                    check.ForeColor = Text;
                    check.Font = UiFont;
                }
                else if (control is RadioButton radio)
                {
                    radio.FlatStyle = FlatStyle.Flat;
                    radio.BackColor = Color.Transparent;
                    radio.ForeColor = Text;
                    radio.Font = UiFont;
                }
                else if (control is ComboBox combo)
                {
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.BackColor = InputBack;
                    combo.ForeColor = Text;
                }
                else if (control is GroupBox group)
                {
                    group.BackColor = Back;
                    group.ForeColor = TextMuted;
                    group.Font = UiFont;
                }
                else if (control is Label label)
                {
                    StyleLabel(label);
                }
                else if (control is TabPage page)
                {
                    page.BackColor = Surface;
                    page.ForeColor = Text;
                }
                else if (control is SectionPanel section)
                {
                    section.BackColor = Surface;
                    section.ForeColor = TextMuted;
                }
                else if (control is Panel || control is TableLayoutPanel || control is FlowLayoutPanel)
                {
                    control.BackColor = Back;
                }
                ApplyRecursive(control);
            }
        }
    }

    // Dark replacement for GroupBox: flat surface, 1px border and caption.
    internal sealed class SectionPanel : Panel
    {
        private readonly string _caption;

        public SectionPanel(string caption)
        {
            _caption = caption;
            BackColor = Theme.Surface;
            ForeColor = Theme.TextMuted;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            using (var pen = new Pen(Theme.Border))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(g, _caption, Theme.UiFont, new Point(8, 4), Theme.TextMuted);
        }
    }
}
