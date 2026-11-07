using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LcscStudio
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _draft;
        private readonly ComboBox _pageSize;
        private readonly Label _targetSummary;
        private CheckBox _darkPanel;
        private CheckBox _overwrite;

        public AppSettings Result { get; private set; }

        public SettingsForm(AppSettings settings)
        {
            _draft = settings.Clone();
            Text = "设置";
            Width = 540;
            Height = 400;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9f);

            var searchGroup = new GroupBox { Text = "搜索", Left = 12, Top = 12, Width = 510, Height = 70 };
            searchGroup.Controls.Add(new Label { Text = "每页结果数", Left = 12, Top = 28, Width = 80 });
            _pageSize = new ComboBox { Left = 100, Top = 24, Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            _pageSize.Items.AddRange(new object[] { 20, 30, 50, 100 });
            _pageSize.SelectedItem = _pageSize.Items.Contains(_draft.SearchPageSize) ? (object)_draft.SearchPageSize : 30;
            searchGroup.Controls.Add(_pageSize);
            Controls.Add(searchGroup);

            var libraryGroup = new GroupBox { Text = "默认下载位置", Left = 12, Top = 90, Width = 510, Height = 100 };
            _targetSummary = new Label
            {
                Left = 12,
                Top = 24,
                Width = 380,
                Height = 60,
                Text = _draft.TargetSummary(),
            };
            libraryGroup.Controls.Add(_targetSummary);
            var change = new Button { Text = "修改…", Left = 410, Top = 30, Width = 84, Height = 28 };
            change.Click += (s, e) => ChangeTarget();
            libraryGroup.Controls.Add(change);
            Controls.Add(libraryGroup);

            var themeBox = new CheckBox
            {
                Text = "深色面板（与 AD 风格一致）",
                Left = 24,
                Top = 205,
                Width = 240,
                Checked = _draft.DarkPanel,
            };
            Controls.Add(themeBox);
            _darkPanel = themeBox;

            var overwriteBox = new CheckBox
            {
                Text = "覆盖库中已存在的同名器件",
                Left = 272,
                Top = 205,
                Width = 230,
                Checked = _draft.Overwrite,
            };
            Controls.Add(overwriteBox);
            _overwrite = overwriteBox;

            var cacheGroup = new GroupBox { Text = "缓存", Left = 12, Top = 236, Width = 510, Height = 90 };
            cacheGroup.Controls.Add(new Label
            {
                Text = PartCache.Root,
                Left = 12,
                Top = 22,
                Width = 480,
                Height = 32,
            });
            var openCache = new Button { Text = "打开缓存目录", Left = 12, Top = 54, Width = 120, Height = 26 };
            openCache.Click += (s, e) => OpenFolder(PartCache.Root);
            cacheGroup.Controls.Add(openCache);
            Controls.Add(cacheGroup);

            var ok = new Button { Text = "确定", Left = 336, Top = 336, Width = 84, Height = 28 };
            var cancel = new Button { Text = "取消", Left = 428, Top = 336, Width = 84, Height = 28 };
            ok.Click += (s, e) => Confirm();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
        }

        private void ChangeTarget()
        {
            using (var form = new TargetForm(_draft))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.Result == null) return;
                PanelForm.CopyTarget(form.Result, _draft);
                _targetSummary.Text = _draft.TargetSummary();
            }
        }

        private static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "打开目录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Confirm()
        {
            if (_pageSize.SelectedItem != null)
                _draft.SearchPageSize = (int)_pageSize.SelectedItem;
            _draft.DarkPanel = _darkPanel.Checked;
            _draft.Overwrite = _overwrite.Checked;

            Result = _draft;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
