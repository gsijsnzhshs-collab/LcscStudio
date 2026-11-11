using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LcscStudio
{
    internal sealed class TargetWindow : Window
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

        public TargetWindow(AppSettings settings)
        {
            _draft = settings.Clone();

            Title = "下载位置";
            Width = 620;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8));

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var libraryGroup = new GroupBox
            {
                Header = "库文件位置",
                Padding = new Thickness(10),
            };

            var stack = new StackPanel();
            libraryGroup.Content = stack;

            _newMode = new RadioButton
            {
                Content = "新建库文件（在指定目录生成 SchLib / PcbLib）",
                IsChecked = !_draft.MergeIntoExisting,
                Margin = new Thickness(0, 0, 0, 6),
            };
            stack.Children.Add(_newMode);

            var newPanel = new Grid { Margin = new Thickness(24, 0, 0, 10) };
            newPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            newPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            newPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            newPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            newPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            newPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddLabel(newPanel, "目录", 0, 0);
            _folder = AddTextBox(newPanel, _draft.NewLibraryFolder, 0, 1);
            var browseFolder = AddButton(newPanel, "浏览…", 0, 2);
            browseFolder.Click += (s, e) => BrowseFolder(_folder);

            AddLabel(newPanel, "文件名", 1, 0);
            _name = AddTextBox(newPanel, _draft.NewLibraryName, 1, 1);
            _name.Margin = new Thickness(0, 6, 8, 0);
            var suffix = new TextBlock
            {
                Text = ".SchLib / .PcbLib",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = Brushes.Gray,
            };
            Grid.SetRow(suffix, 1);
            Grid.SetColumn(suffix, 2);
            Grid.SetColumnSpan(suffix, 2);
            newPanel.Children.Add(suffix);
            stack.Children.Add(newPanel);

            _preview = new Label
            {
                Margin = new Thickness(24, 0, 0, 6),
                Foreground = Brushes.Gray,
            };
            stack.Children.Add(_preview);

            _mergeMode = new RadioButton
            {
                Content = "合并到已有的库文件",
                IsChecked = _draft.MergeIntoExisting,
                Margin = new Thickness(0, 4, 0, 6),
            };
            stack.Children.Add(_mergeMode);

            var mergePanel = new Grid { Margin = new Thickness(24, 0, 0, 0) };
            mergePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mergePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mergePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mergePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mergePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddLabel(mergePanel, "SchLib", 0, 0);
            _mergeSch = AddTextBox(mergePanel, _draft.MergeSchLib, 0, 1);
            var browseSch = AddButton(mergePanel, "浏览…", 0, 2);
            browseSch.Click += (s, e) => BrowseFile(_mergeSch, "SchLib");

            AddLabel(mergePanel, "PcbLib", 1, 0);
            _mergePcb = AddTextBox(mergePanel, _draft.MergePcbLib, 1, 1);
            _mergePcb.Margin = new Thickness(0, 6, 8, 0);
            var browsePcb = AddButton(mergePanel, "浏览…", 1, 2);
            browsePcb.Margin = new Thickness(0, 6, 0, 0);
            browsePcb.Click += (s, e) => BrowseFile(_mergePcb, "PcbLib");
            stack.Children.Add(mergePanel);

            Grid.SetRow(libraryGroup, 0);
            root.Children.Add(libraryGroup);

            var optionsGroup = new GroupBox
            {
                Header = "导入内容",
                Padding = new Thickness(10),
                Margin = new Thickness(0, 12, 0, 0),
            };
            var options = new WrapPanel();
            _symbol = new CheckBox { Content = "原理图符号", IsChecked = _draft.ImportSymbol, Margin = new Thickness(0, 0, 20, 8) };
            _footprint = new CheckBox { Content = "PCB 封装", IsChecked = _draft.ImportFootprint, Margin = new Thickness(0, 0, 20, 8) };
            _model = new CheckBox { Content = "3D 模型", IsChecked = _draft.ImportModel, Margin = new Thickness(0, 0, 20, 8) };
            _overwrite = new CheckBox { Content = "覆盖已存在的库元件", IsChecked = _draft.Overwrite, Margin = new Thickness(0, 0, 20, 8) };
            _place = new CheckBox { Content = "导入后放置到当前原理图", IsChecked = _draft.PlaceAfterImport };
            options.Children.Add(_symbol);
            options.Children.Add(_footprint);
            options.Children.Add(_model);
            options.Children.Add(_overwrite);
            options.Children.Add(_place);
            optionsGroup.Content = options;
            optionsGroup.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(optionsGroup, 1);
            root.Children.Add(optionsGroup);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var ok = new Button { Content = "确定", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "取消", Width = 90, Height = 30 };
            ok.Click += (s, e) => Confirm();
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Content = root;

            _newMode.Checked += (s, e) => UpdateMode();
            _mergeMode.Checked += (s, e) => UpdateMode();
            _name.TextChanged += (s, e) => UpdatePreview();
            _folder.TextChanged += (s, e) => UpdatePreview();
            _symbol.Checked += (s, e) => UpdateMode();
            _footprint.Checked += (s, e) => UpdateMode();

            UpdateMode();
        }

        private static void AddLabel(Grid grid, string text, int row, int column)
        {
            var label = new Label { Content = text, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, column);
            grid.Children.Add(label);
        }

        private static TextBox AddTextBox(Grid grid, string value, int row, int column)
        {
            var box = new TextBox
            {
                Text = value ?? "",
                Height = 26,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, row == 0 ? 0 : 6, 8, 0),
            };
            Grid.SetRow(box, row);
            Grid.SetColumn(box, column);
            grid.Children.Add(box);
            return box;
        }

        private static Button AddButton(Grid grid, string text, int row, int column)
        {
            var button = new Button { Content = text, Width = 70, Height = 26 };
            Grid.SetRow(button, row);
            Grid.SetColumn(button, column);
            grid.Children.Add(button);
            return button;
        }

        private void UpdateMode()
        {
            var isNew = _newMode.IsChecked == true;
            _folder.IsEnabled = isNew;
            _name.IsEnabled = isNew;
            _mergeSch.IsEnabled = !isNew;
            _mergePcb.IsEnabled = !isNew;
            _preview.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
            _model.IsEnabled = _footprint.IsChecked == true;
            if (_footprint.IsChecked != true) _model.IsChecked = false;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var folder = _folder.Text.Trim();
            var name = string.IsNullOrWhiteSpace(_name.Text) ? "LCSC" : _name.Text.Trim();
            try
            {
                _preview.Content = Path.Combine(folder, name + ".SchLib") + "\n" +
                                   Path.Combine(folder, name + ".PcbLib");
            }
            catch
            {
                _preview.Content = "";
            }
        }

        private void BrowseFolder(TextBox target)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择库文件目录";
                if (Directory.Exists(target.Text)) dialog.SelectedPath = target.Text;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    target.Text = dialog.SelectedPath;
            }
        }

        private void BrowseFile(TextBox target, string kind)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = kind + " (*." + kind + ")|*." + kind + "|所有文件 (*.*)|*.*",
                CheckFileExists = true,
            };
            if (File.Exists(target.Text))
            {
                try { dialog.InitialDirectory = Path.GetDirectoryName(target.Text); } catch { }
            }
            if (dialog.ShowDialog(this) == true) target.Text = dialog.FileName;
        }

        private void Confirm()
        {
            _draft.MergeIntoExisting = _mergeMode.IsChecked == true;
            _draft.NewLibraryFolder = _folder.Text.Trim();
            _draft.NewLibraryName = string.IsNullOrWhiteSpace(_name.Text) ? "LCSC" : _name.Text.Trim();
            _draft.MergeSchLib = _mergeSch.Text.Trim();
            _draft.MergePcbLib = _mergePcb.Text.Trim();
            _draft.ImportSymbol = _symbol.IsChecked == true;
            _draft.ImportFootprint = _footprint.IsChecked == true;
            _draft.ImportModel = _model.IsChecked == true && _draft.ImportFootprint;
            _draft.Overwrite = _overwrite.IsChecked == true;
            _draft.PlaceAfterImport = _place.IsChecked == true;

            if (!_draft.Validate(out var error))
            {
                MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_draft.MergeIntoExisting && !Directory.Exists(_draft.NewLibraryFolder))
            {
                try { Directory.CreateDirectory(_draft.NewLibraryFolder); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "无法创建目录：" + ex.Message, Title,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            Result = _draft;
            DialogResult = true;
            Close();
        }
    }
}
