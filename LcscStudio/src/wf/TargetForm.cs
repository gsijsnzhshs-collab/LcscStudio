using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using LcscLoader;

namespace LcscStudio
{
    internal sealed class TargetForm : Form
    {
        private readonly AppSettings _draft;
        // 设置里带出来的旧值与程序自动填的值分开记，只有这两类值才允许被工程切换覆盖；
        // 用户本次在对话框里手动改过的路径一律不动。
        private readonly string _savedMergeSch;
        private readonly string _savedMergePcb;
        private string _autoSch = "";
        private string _autoPcb = "";
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
        private readonly Button _ok;
        private readonly Button _cancel;

        public AppSettings Result { get; private set; }

        public TargetForm(AppSettings settings)
        {
            _draft = settings.Clone();
            _savedMergeSch = _draft.MergeSchLib ?? "";
            _savedMergePcb = _draft.MergePcbLib ?? "";
            Text = "下载位置";
            Width = 620;
            // 底部多留 44px：中文输入法的候选条是浮层，会盖住窗口右下角，
            // 原来按钮离底边只有 ~17px，“确定”经常点不到。加大安全边距后
            // 候选条只压到空白区；同时下面设置 AcceptButton，直接回车也能确认。
            Height = 540;
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

            _ok = new Button { Text = "确定", Left = 426, Top = 430, Width = 84, Height = 28 };
            _cancel = new Button { Text = "取消", Left = 518, Top = 430, Width = 84, Height = 28 };
            _ok.Click += (s, e) => Confirm();
            _cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            // 回车 = 确定，Esc = 取消；输入法候选条打开时回车先被输入法吃掉，不会误确认。
            AcceptButton = _ok;
            CancelButton = _cancel;
            Controls.Add(_ok);
            Controls.Add(_cancel);
            Theme.ApplyRecursive(this);
            Theme.StyleButton(_ok, true);
            Theme.ApplyButtons(this, _ok);

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

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // 中文输入法的浮动条钉在工作区右下角，显示前把按钮行挪出那一带。
            Theme.PlaceDialog(this, Rectangle.Union(_ok.Bounds, _cancel.Bounds));
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
            if (!isNew) SyncMergePathsToProject();
            UpdatePreview();
        }

        // 合并目标跟随当前工程：换到另一个项目时，上一项目的库路径不能继续沿用，
        // 否则器件会被合并进别的项目的库里。工程目录下恰好一个库时直接填好，
        // 多个或没有则清空，由用户在已定位到工程目录的浏览框里选。
        private void SyncMergePathsToProject()
        {
            var folder = AdApi.CurrentProjectFolder;
            if (string.IsNullOrEmpty(folder)) return;
            SyncOne(_mergeSch, _savedMergeSch, ref _autoSch, folder, "*.SchLib");
            SyncOne(_mergePcb, _savedMergePcb, ref _autoPcb, folder, "*.PcbLib");
        }

        private static void SyncOne(TextBox box, string saved, ref string auto, string folder, string pattern)
        {
            var current = box.Text.Trim();
            var managed = current.Length == 0
                || string.Equals(current, saved.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(current, auto, StringComparison.OrdinalIgnoreCase);
            if (!managed) return; // 用户本次手动改过，不覆盖

            // 已经指向当前工程目录：沿用，同项目连续导入时路径保持稳定。
            if (current.Length > 0 && IsInFolder(current, folder))
            {
                auto = current;
                return;
            }

            auto = FindSingleLibrary(folder, pattern);
            box.Text = auto;
        }

        private static bool IsInFolder(string path, string folder)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (dir == null) return false;
                return string.Equals(dir.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string FindSingleLibrary(string folder, string pattern)
        {
            try
            {
                var files = Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly);
                return files.Length == 1 ? files[0] : "";
            }
            catch
            {
                return "";
            }
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
                else
                {
                    // 还没选过库文件时直接定位到当前工程目录，不用从系统默认目录一层层翻。
                    var projectFolder = AdApi.CurrentProjectFolder;
                    if (!string.IsNullOrEmpty(projectFolder)) dialog.InitialDirectory = projectFolder;
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
