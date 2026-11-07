using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LcscStudio
{
    internal sealed class TargetForm : Form
    {
        private readonly AppSettings _draft;
        private readonly RadioButton _newMode;
        private readonly RadioButton _mergeMode;
        private readonly TextBox _folder;
        private readonly TextBox _name;
        private readonly TextBox _mergeSch;
        private readonly TextBox _mergePcb;
        private readonly CheckBox _symbol;
        private readonly CheckBox _footprint;
        private readonly CheckBox _model;
        private readonly CheckBox _overwrite;
        private readonly CheckBox _place;
        private readonly Label _preview;

        public AppSettings Result { get; private set; }

        public TargetForm(AppSettings settings)
        {
            _draft = settings.Clone();
            Text = "下载位置";
            Width = 620;
            Height = 496;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Theme.StyleForm(this);

            var libraryGroup = new SectionPanel("库文件位置") { Left = 12, Top = 12, Width = 590, Height = 300 };
            Controls.Add(libraryGroup);

            _newMode = new RadioButton
            {
                Text = "新建库文件（在指定目录生成 SchLib / PcbLib）",
                Left = 12,
                Top = 22,
                Width = 400,
                Checked = !_draft.MergeIntoExisting,
            };
            libraryGroup.Controls.Add(_newMode);

            libraryGroup.Controls.Add(new Label { Text = "目录", Left = 24, Top = 54, Width = 40 });
            _folder = new TextBox { Left = 70, Top = 50, Width = 400, Text = _draft.NewLibraryFolder };
            libraryGroup.Controls.Add(_folder);
            var browseFolder = new Button { Text = "浏览…", Left = 480, Top = 49, Width = 70 };
            browseFolder.Click += (s, e) => BrowseFolder(_folder);
            libraryGroup.Controls.Add(browseFolder);

            libraryGroup.Controls.Add(new Label { Text = "文件名", Left = 24, Top = 84, Width = 50 });
            _name = new TextBox { Left = 80, Top = 80, Width = 190, Text = _draft.NewLibraryName };
            libraryGroup.Controls.Add(_name);
            libraryGroup.Controls.Add(new Label
            {
                Text = ".SchLib / .PcbLib",
                Left = 280,
                Top = 84,
                Width = 160,
                ForeColor = Color.Gray,
            });
            _preview = new Label { Left = 80, Top = 110, Width = 470, Height = 36, ForeColor = Color.Gray };
            libraryGroup.Controls.Add(_preview);

            _mergeMode = new RadioButton
            {
                Text = "合并到已有的库文件",
                Left = 12,
                Top = 150,
                Width = 300,
                Checked = _draft.MergeIntoExisting,
            };
            libraryGroup.Controls.Add(_mergeMode);

            libraryGroup.Controls.Add(new Label { Text = "SchLib", Left = 24, Top = 182, Width = 50 });
            _mergeSch = new TextBox { Left = 80, Top = 178, Width = 390, Text = _draft.MergeSchLib };
            libraryGroup.Controls.Add(_mergeSch);
            var browseSch = new Button { Text = "浏览…", Left = 480, Top = 177, Width = 70 };
            browseSch.Click += (s, e) => BrowseFile(_mergeSch, "SchLib");
            libraryGroup.Controls.Add(browseSch);

            libraryGroup.Controls.Add(new Label { Text = "PcbLib", Left = 24, Top = 212, Width = 50 });
            _mergePcb = new TextBox { Left = 80, Top = 208, Width = 390, Text = _draft.MergePcbLib };
            libraryGroup.Controls.Add(_mergePcb);
            var browsePcb = new Button { Text = "浏览…", Left = 480, Top = 207, Width = 70 };
            browsePcb.Click += (s, e) => BrowseFile(_mergePcb, "PcbLib");
            libraryGroup.Controls.Add(browsePcb);

            var optionsGroup = new SectionPanel("导入内容") { Left = 12, Top = 320, Width = 590, Height = 80 };
            Controls.Add(optionsGroup);
            _symbol = new CheckBox { Text = "原理图符号", Left = 12, Top = 22, Width = 100, Checked = _draft.ImportSymbol };
            _footprint = new CheckBox { Text = "PCB 封装", Left = 120, Top = 22, Width = 90, Checked = _draft.ImportFootprint };
            _model = new CheckBox { Text = "3D 模型", Left = 220, Top = 22, Width = 80, Checked = _draft.ImportModel };
            _overwrite = new CheckBox { Text = "覆盖已存在的库元件", Left = 12, Top = 50, Width = 150, Checked = _draft.Overwrite };
            _place = new CheckBox { Text = "导入后放置到当前原理图", Left = 220, Top = 50, Width = 180, Checked = _draft.PlaceAfterImport };
            optionsGroup.Controls.Add(_symbol);
            optionsGroup.Controls.Add(_footprint);
            optionsGroup.Controls.Add(_model);
            optionsGroup.Controls.Add(_overwrite);
            optionsGroup.Controls.Add(_place);

            var ok = new Button { Text = "确定", Left = 426, Top = 412, Width = 84, Height = 28 };
            var cancel = new Button { Text = "取消", Left = 518, Top = 412, Width = 84, Height = 28 };
            ok.Click += (s, e) => Confirm();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            Theme.ApplyRecursive(this);
            Theme.StyleButton(ok, true);
            Theme.ApplyButtons(this, ok);

            _newMode.CheckedChanged += (s, e) => UpdateMode();
            _mergeMode.CheckedChanged += (s, e) => UpdateMode();
            _name.TextChanged += (s, e) => UpdatePreview();
            _folder.TextChanged += (s, e) => UpdatePreview();
            _footprint.CheckedChanged += (s, e) =>
            {
                _model.Enabled = _footprint.Checked;
                if (!_footprint.Checked) _model.Checked = false;
            };
            UpdateMode();
            Theme.ScaleFormToDpi(this);
        }

        private void UpdateMode()
        {
            var isNew = _newMode.Checked;
            _folder.Enabled = isNew;
            _name.Enabled = isNew;
            _mergeSch.Enabled = !isNew;
            _mergePcb.Enabled = !isNew;
            _preview.Visible = isNew;
            _model.Enabled = _footprint.Checked;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            try
            {
                var folder = _folder.Text.Trim();
                var name = string.IsNullOrWhiteSpace(_name.Text) ? "LCSC" : _name.Text.Trim();
                _preview.Text = Path.Combine(folder, name + ".SchLib") + "\n" +
                                Path.Combine(folder, name + ".PcbLib");
            }
            catch
            {
                _preview.Text = "";
            }
        }

        private void BrowseFolder(TextBox target)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (Directory.Exists(target.Text)) dialog.SelectedPath = target.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
            }
        }

        private void BrowseFile(TextBox target, string kind)
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = kind + " (*." + kind + ")|*." + kind + "|所有文件 (*.*)|*.*",
                CheckFileExists = true,
            })
            {
                if (File.Exists(target.Text))
                {
                    try { dialog.InitialDirectory = Path.GetDirectoryName(target.Text); } catch { }
                }
                if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
            }
        }

        private void Confirm()
        {
            _draft.MergeIntoExisting = _mergeMode.Checked;
            _draft.NewLibraryFolder = _folder.Text.Trim();
            _draft.NewLibraryName = string.IsNullOrWhiteSpace(_name.Text) ? "LCSC" : _name.Text.Trim();
            _draft.MergeSchLib = _mergeSch.Text.Trim();
            _draft.MergePcbLib = _mergePcb.Text.Trim();
            _draft.ImportSymbol = _symbol.Checked;
            _draft.ImportFootprint = _footprint.Checked;
            _draft.ImportModel = _model.Checked && _draft.ImportFootprint;
            _draft.Overwrite = _overwrite.Checked;
            _draft.PlaceAfterImport = _place.Checked;

            if (!_draft.Validate(out var error))
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_draft.MergeIntoExisting && !Directory.Exists(_draft.NewLibraryFolder))
            {
                try { Directory.CreateDirectory(_draft.NewLibraryFolder); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "无法创建目录：" + ex.Message, Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            Result = _draft;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
