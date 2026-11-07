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
            EnableDarkTitleBar(form);
        }

        // Altium 运行在 System DPI 感知模式，WinForms 认为窗口仍是 96 DPI
        // （DeviceDpi 恒为 96），而 GDI 已按 144 DPI 渲染字体。按 96 DPI 摆放的
        // 对话框会被放大后的文字挤满，这里按实际 DPI 手动缩放一次布局。
        public static void ScaleFormToDpi(Form form)
        {
            var handle = form.Handle;
            using (var graphics = form.CreateGraphics())
            {
                var factor = graphics.DpiX / 96f;
                if (factor < 1.02f)
                {
                    return;
                }
                form.SuspendLayout();
                foreach (Control child in form.Controls)
                {
                    ScaleBounds(child, factor);
                }
                form.ClientSize = new Size(
                    (int)Math.Round(form.ClientSize.Width * factor),
                    (int)Math.Round(form.ClientSize.Height * factor));
                form.ResumeLayout(true);
            }
        }

        private static void ScaleBounds(Control control, float factor)
        {
            control.Bounds = new Rectangle(
                (int)Math.Round(control.Left * factor),
                (int)Math.Round(control.Top * factor),
                (int)Math.Round(control.Width * factor),
                (int)Math.Round(control.Height * factor));
            foreach (Control child in control.Controls)
            {
                ScaleBounds(child, factor);
            }
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
            EnableOwnTextPaint(button);
            return button;
        }

        // Inside the Altium host, WinForms computes a text rectangle that is shorter
        // than the control (measured: bottom 4px of a 26px button never get painted),
        // which cuts the glyph bottoms. Repaint the caption ourselves, centred in the
        // full client area, so the layout no longer depends on that internal rectangle.
        public static void EnableOwnTextPaint(Button button)
        {
            button.Paint += (s, e) =>
            {
                var b = (Button)s;
                using (var fill = new SolidBrush(b.BackColor))
                {
                    e.Graphics.FillRectangle(fill, b.ClientRectangle);
                }
                var border = b.FlatAppearance.BorderSize;
                if (border > 0)
                {
                    using (var pen = new Pen(b.FlatAppearance.BorderColor, border))
                    {
                        e.Graphics.DrawRectangle(pen, 0, 0, b.Width - border, b.Height - border);
                    }
                }
                if (string.IsNullOrEmpty(b.Text))
                {
                    return;
                }
                TextRenderer.DrawText(
                    e.Graphics,
                    b.Text,
                    b.Font,
                    b.ClientRectangle,
                    b.Enabled ? b.ForeColor : TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            };
        }

        // 浅色系统勾选框放在深色底上几乎看不见，方框与对勾全部自绘。
        public static void StyleCheckBox(CheckBox check)
        {
            check.FlatStyle = FlatStyle.Flat;
            check.Font = UiFont;
            check.ForeColor = Text;
            check.BackColor = check.Parent != null ? check.Parent.BackColor : Back;
            check.Paint += (s, e) => PaintCheckBox((CheckBox)s, e);
        }

        private static void PaintCheckBox(CheckBox check, PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var fill = new SolidBrush(check.BackColor))
            {
                g.FillRectangle(fill, check.ClientRectangle);
            }
            var size = Math.Max(12, check.Font.Height - 4);
            if (size > check.Height - 2)
            {
                size = Math.Max(12, check.Height - 2);
            }
            var box = new Rectangle(1, (check.Height - size) / 2, size, size);
            using (var back = new SolidBrush(InputBack))
            using (var pen = new Pen(check.Enabled ? Border : SurfaceAlt))
            {
                g.FillRectangle(back, box);
                g.DrawRectangle(pen, box);
            }
            if (check.Checked)
            {
                using (var pen = new Pen(check.Enabled ? Accent : TextMuted, Math.Max(2f, size / 7f)))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    g.DrawLines(pen, new[]
                    {
                        new PointF(box.Left + size * 0.20f, box.Top + size * 0.52f),
                        new PointF(box.Left + size * 0.42f, box.Top + size * 0.74f),
                        new PointF(box.Left + size * 0.80f, box.Top + size * 0.28f),
                    });
                }
            }
            var offset = box.Right + 6;
            if (offset < check.Width)
            {
                TextRenderer.DrawText(
                    g,
                    check.Text,
                    check.Font,
                    new Rectangle(offset, 0, check.Width - offset, check.Height),
                    check.Enabled ? check.ForeColor : TextMuted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        public static void StyleRadioButton(RadioButton radio)
        {
            radio.FlatStyle = FlatStyle.Flat;
            radio.Font = UiFont;
            radio.ForeColor = Text;
            radio.BackColor = radio.Parent != null ? radio.Parent.BackColor : Back;
            radio.Paint += (s, e) => PaintRadioButton((RadioButton)s, e);
        }

        private static void PaintRadioButton(RadioButton radio, PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var fill = new SolidBrush(radio.BackColor))
            {
                g.FillRectangle(fill, radio.ClientRectangle);
            }
            var size = Math.Max(12, radio.Font.Height - 4);
            if (size > radio.Height - 2)
            {
                size = Math.Max(12, radio.Height - 2);
            }
            var ring = new Rectangle(1, (radio.Height - size) / 2, size, size);
            using (var back = new SolidBrush(InputBack))
            using (var pen = new Pen(radio.Checked ? Accent : (radio.Enabled ? Border : SurfaceAlt)))
            {
                g.FillEllipse(back, ring);
                g.DrawEllipse(pen, ring);
            }
            if (radio.Checked)
            {
                var dot = Math.Max(4, (int)(size * 0.5f));
                using (var brush = new SolidBrush(radio.Enabled ? Accent : TextMuted))
                {
                    g.FillEllipse(brush, ring.Left + (size - dot) / 2, ring.Top + (size - dot) / 2, dot, dot);
                }
            }
            var offset = ring.Right + 6;
            if (offset < radio.Width)
            {
                TextRenderer.DrawText(
                    g,
                    radio.Text,
                    radio.Font,
                    new Rectangle(offset, 0, radio.Width - offset, radio.Height),
                    radio.Enabled ? radio.ForeColor : TextMuted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        // Fixed-height, text-measured width: AutoSize alone under-measures flat
        // buttons and clips the glyph bottoms on high-DPI or narrow panels.
        public static void SizeToText(Button button, int height, int minWidth = 0)
        {
            button.AutoSize = false;
            button.TextAlign = ContentAlignment.MiddleCenter;
            // Flat buttons reserve ~6px of vertical padding; a control shorter than
            // the font's line height silently clips the top of the glyphs.
            var lineHeight = TextRenderer.MeasureText("Hg", button.Font).Height;
            button.Height = Math.Max(height, lineHeight + 6);
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
                    StyleCheckBox(check);
                }
                else if (control is RadioButton radio)
                {
                    StyleRadioButton(radio);
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
