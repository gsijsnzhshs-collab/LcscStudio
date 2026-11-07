using System;
using System.Drawing;
using System.Windows.Forms;

namespace LcscStudio
{
    // Central dark palette tuned to Altium Designer's dark UI.
    internal static class Theme
    {
        public static readonly Color Back = Color.FromArgb(0x2B, 0x2B, 0x2B);
        public static readonly Color Surface = Color.FromArgb(0x33, 0x33, 0x33);
        public static readonly Color SurfaceAlt = Color.FromArgb(0x3A, 0x3A, 0x3A);
        public static readonly Color InputBack = Color.FromArgb(0x1E, 0x1F, 0x22);
        public static readonly Color Border = Color.FromArgb(0x47, 0x47, 0x47);
        public static readonly Color Text = Color.FromArgb(0xE3, 0xE3, 0xE3);
        public static readonly Color TextMuted = Color.FromArgb(0x9C, 0x9C, 0x9C);
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
        public static readonly Font SmallFont = new Font("Microsoft YaHei UI", 8.25f);
        public static readonly Font MonoFont = new Font("Consolas", 8.5f);

        public static void StyleForm(Form form)
        {
            form.BackColor = Back;
            form.ForeColor = Text;
            form.Font = UiFont;
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
                else if (control is Panel || control is TableLayoutPanel || control is FlowLayoutPanel)
                {
                    control.BackColor = Back;
                }
                ApplyRecursive(control);
            }
        }
    }
}
