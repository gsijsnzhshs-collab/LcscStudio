using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LcscSetup
{
    internal sealed class SetupForm : Form
    {
        private static readonly Color Back = Color.FromArgb(0x1B, 0x1C, 0x1E);
        private static readonly Color Surface = Color.FromArgb(0x23, 0x24, 0x27);
        private static readonly Color SurfaceAlt = Color.FromArgb(0x2C, 0x2E, 0x32);
        private static readonly Color Border = Color.FromArgb(0x34, 0x36, 0x3A);
        private static readonly Color TextColor = Color.FromArgb(0xE3, 0xE3, 0xE3);
        private static readonly Color TextMuted = Color.FromArgb(0x9E, 0xA0, 0xA4);
        private static readonly Color Accent = Color.FromArgb(0x3C, 0x9B, 0xD8);
        private static readonly Color Success = Color.FromArgb(0x5C, 0xB8, 0x5C);
        private static readonly Color Error = Color.FromArgb(0xE0, 0x5A, 0x5A);

        private const int SideMargin = 24;

        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Label _listCaption;
        private readonly ListView _list;
        private readonly TextBox _log;
        private readonly Label _logCaption;
        private readonly Label _status;
        private readonly Button _refresh;
        private readonly Button _manual;
        private readonly Button _install;
        private readonly Button _uninstall;
        private readonly Button _close;

        public SetupForm()
        {
            Text = "LCSC ADesignK · Altium 插件安装器";
            ClientSize = new Size(820, 660);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Back;
            Font = new Font("Microsoft YaHei UI", 9F);
            ForeColor = TextColor;
            AutoScaleMode = AutoScaleMode.None;

            var info = Payload.Info;

            _title = new Label
            {
                Text = "LCSC ADesignK · Altium 插件安装器",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
                ForeColor = TextColor,
            };
            Controls.Add(_title);

            _subtitle = new Label
            {
                Text = "内置版本 " + info.Version + "   ·   构建 " + info.ShortHash + "   ·   " + info.Built,
                AutoSize = true,
                ForeColor = TextMuted,
            };
            Controls.Add(_subtitle);

            _listCaption = new Label
            {
                Text = "检测到的 Altium Designer（勾选后点“安装到选中”）：",
                AutoSize = true,
                ForeColor = TextColor,
            };
            Controls.Add(_listCaption);

            _list = new ListView
            {
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                MultiSelect = true,
                HideSelection = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Surface,
                ForeColor = TextColor,
            };
            _list.Columns.Add("版本", 100);
            _list.Columns.Add("安装目录", 400);
            _list.Columns.Add("状态", 220);
            Controls.Add(_list);

            _refresh = MakeButton("刷新", false);
            _refresh.Click += (s, e) => RefreshList();
            Controls.Add(_refresh);

            _manual = MakeButton("手动选择…", false);
            _manual.Click += (s, e) => AddManual();
            Controls.Add(_manual);

            _install = MakeButton("安装到选中", true);
            _install.Click += (s, e) => RunForChecked(true);
            Controls.Add(_install);

            _uninstall = MakeButton("卸载选中", false);
            _uninstall.Click += (s, e) => RunForChecked(false);
            Controls.Add(_uninstall);

            _close = MakeButton("关闭", false);
            _close.Click += (s, e) => Close();
            Controls.Add(_close);

            _logCaption = new Label
            {
                Text = "日志",
                AutoSize = true,
                ForeColor = TextMuted,
            };
            Controls.Add(_logCaption);

            _log = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(0x17, 0x18, 0x1B),
                ForeColor = TextMuted,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5F),
            };
            Controls.Add(_log);

            _status = new Label
            {
                AutoEllipsis = true,
                ForeColor = TextMuted,
            };
            Controls.Add(_status);

            Load += (s, e) =>
            {
                LayoutAll();
                RefreshList();
            };
        }

        private Button MakeButton(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : SurfaceAlt,
                ForeColor = primary ? Color.White : TextColor,
                UseVisualStyleBackColor = false,
            };
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.FlatAppearance.MouseOverBackColor = primary
                ? Color.FromArgb(0x4F, 0xB0, 0xEC)
                : Color.FromArgb(0x34, 0x37, 0x3B);
            return button;
        }

        // 高 DPI 下字体比硬编码像素大，所有位置按实际字体高度推算，避免文字被截断。
        private void LayoutAll()
        {
            var pad = Math.Max(8, (int)(Font.Height * 0.7));
            var contentWidth = ClientSize.Width - SideMargin * 2;
            var y = pad;

            _title.Location = new Point(SideMargin, y);
            y += _title.Height + 2;

            _subtitle.Location = new Point(SideMargin + 2, y);
            y += _subtitle.Height + pad;

            _listCaption.Location = new Point(SideMargin + 2, y);
            y += _listCaption.Height + 6;

            var statusHeight = Font.Height + 10;
            var logHeight = Math.Max(90, (int)(Font.Height * 6));
            var buttonHeight = Font.Height + 18;
            var spacing = Math.Max(12, (int)(Font.Height * 0.8));

            var listHeight = ClientSize.Height - y - buttonHeight - spacing
                             - _logCaption.Height - 6 - logHeight - spacing - statusHeight - pad;
            _list.Location = new Point(SideMargin, y);
            _list.Size = new Size(contentWidth, Math.Max(160, listHeight));

            var checkWidth = 26;
            var versionWidth = Math.Max(110, TextRenderer.MeasureText("AD 20.0.13.296", Font).Width + 8);
            var statusWidth = Math.Max(210, TextRenderer.MeasureText("已安装 1.0.3.0（与安装包一致）", Font).Width + 8);
            _list.Columns[0].Width = versionWidth + checkWidth;
            _list.Columns[2].Width = statusWidth;
            _list.Columns[1].Width = Math.Max(160, contentWidth - _list.Columns[0].Width - statusWidth - 4);

            y += _list.Height + spacing;

            var closeWidth = ButtonWidth("关闭");
            var uninstallWidth = ButtonWidth("卸载选中");
            var installWidth = ButtonWidth("安装到选中");
            var refreshWidth = ButtonWidth("刷新");
            var manualWidth = ButtonWidth("手动选择…");

            _close.Size = new Size(closeWidth, buttonHeight);
            _close.Location = new Point(SideMargin + contentWidth - closeWidth, y);

            _uninstall.Size = new Size(uninstallWidth, buttonHeight);
            _uninstall.Location = new Point(_close.Left - 8 - uninstallWidth, y);

            _install.Size = new Size(installWidth, buttonHeight);
            _install.Location = new Point(_uninstall.Left - 8 - installWidth, y);

            _refresh.Size = new Size(refreshWidth, buttonHeight);
            _refresh.Location = new Point(SideMargin, y);

            _manual.Size = new Size(manualWidth, buttonHeight);
            _manual.Location = new Point(_refresh.Right + 8, y);

            y += buttonHeight + spacing;

            _logCaption.Location = new Point(SideMargin + 2, y);
            y += _logCaption.Height + 4;

            _log.Location = new Point(SideMargin, y);
            _log.Size = new Size(contentWidth, logHeight);
            y += logHeight + 4;

            _status.Location = new Point(SideMargin, y);
            _status.Size = new Size(contentWidth, statusHeight);
        }

        private int ButtonWidth(string text)
        {
            return Math.Max(72, TextRenderer.MeasureText(text, Font).Width + 28);
        }

        private void Log(string message)
        {
            _log.AppendText(message + Environment.NewLine);
        }

        private void SetStatus(string message, bool error = false)
        {
            _status.Text = message;
            _status.ForeColor = error ? Error : TextMuted;
        }

        private void RefreshList()
        {
            var checkedDirs = new HashSet<string>(
                _list.Items.Cast<ListViewItem>()
                    .Where(item => item.Checked)
                    .Select(item => ((AdInstall)item.Tag).SystemDir),
                StringComparer.OrdinalIgnoreCase);

            _list.BeginUpdate();
            _list.Items.Clear();
            try
            {
                var installs = AdFinder.FindAll();
                foreach (var install in installs)
                {
                    var item = new ListViewItem(new[]
                    {
                        "AD " + install.ShortVersion,
                        install.SystemDir,
                        install.StatusText,
                    })
                    {
                        Tag = install,
                        Checked = checkedDirs.Count == 0 || checkedDirs.Contains(install.SystemDir),
                    };
                    item.ForeColor = install.SameAsPayload ? Success : TextColor;
                    _list.Items.Add(item);
                }

                if (installs.Count == 0)
                {
                    SetStatus("未检测到 Altium Designer。若已安装，请用“手动选择…”指定它的 System 目录。", true);
                }
                else
                {
                    SetStatus("检测到 " + installs.Count + " 个 Altium Designer。若要安装，先关闭 AD，再点“安装到选中”。");
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            if (Installer.AltiumRunning(out var runningDetail))
            {
                Log("提示：" + runningDetail);
                SetStatus(runningDetail, true);
            }
        }

        private void AddManual()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 Altium Designer 的 System 目录（或安装根目录）";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var installs = new List<AdInstall>();
                AdFinder.Add(installs, dialog.SelectedPath, "手动");
                if (installs.Count == 0)
                {
                    MessageBox.Show(this,
                        "这个目录不像 Altium 的安装目录。\n请选择包含 X2.EXE 的上一级下的 System 目录。",
                        "LCSC ADesignK 安装器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var install = installs[0];
                var item = new ListViewItem(new[]
                {
                    "AD " + install.ShortVersion,
                    install.SystemDir,
                    install.StatusText,
                })
                {
                    Tag = install,
                    Checked = true,
                };
                item.ForeColor = install.SameAsPayload ? Success : TextColor;
                _list.Items.Add(item);
                Log("手动添加：" + install.SystemDir);
                SetStatus("已添加 " + install.SystemDir);
            }
        }

        private void RunForChecked(bool install)
        {
            var targets = _list.Items.Cast<ListViewItem>()
                .Where(item => item.Checked)
                .Select(item => (AdInstall)item.Tag)
                .ToList();

            if (targets.Count == 0)
            {
                SetStatus("请先勾选至少一个 Altium Designer。", true);
                return;
            }

            if (Installer.AltiumRunning(out var runningDetail))
            {
                MessageBox.Show(this, runningDetail, "LCSC ADesignK 安装器",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus(runningDetail, true);
                return;
            }

            var failed = 0;
            foreach (var target in targets)
            {
                try
                {
                    Log((install ? "安装 → " : "卸载 → ") + target.SystemDir);
                    var message = install
                        ? Installer.Install(target, true, Log)
                        : Installer.Uninstall(target, Log);
                    Log("  " + message);
                }
                catch (UnauthorizedAccessException)
                {
                    failed++;
                    Log("  失败：没有写入权限（需要管理员）");
                    if (AskElevate()) return;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log("  失败：" + ex.Message);
                }
            }

            RefreshList();
            if (failed == 0)
            {
                SetStatus(install
                    ? "安装完成，请启动 Altium Designer，在 Tools 菜单里使用 LCSC Component Import。"
                    : "卸载完成。");
            }
            else
            {
                SetStatus(failed + " 个目标操作失败，详见日志。", true);
            }
        }

        private bool AskElevate()
        {
            var result = MessageBox.Show(this,
                "写入 Altium 安装目录需要管理员权限。是否以管理员身份重新启动安装器？",
                "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return false;
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                });
                Close();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "提权失败：" + ex.Message, "LCSC ADesignK 安装器",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }
}



