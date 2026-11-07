using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LcscStudio
{
    internal sealed class SettingsWindow : Window
    {
        private readonly AppSettings _draft;
        private readonly TextBlock _targetSummary;
        private readonly ComboBox _pageSize;

        public AppSettings Result { get; private set; }

        public SettingsWindow(AppSettings settings)
        {
            _draft = settings.Clone();

            Title = "设置";
            Width = 520;
            Height = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8));
            FontFamily = new FontFamily("Microsoft YaHei UI");

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var searchGroup = new GroupBox { Header = "搜索", Padding = new Thickness(10) };
            var searchPanel = new StackPanel { Orientation = Orientation.Horizontal };
            searchPanel.Children.Add(new Label
            {
                Content = "每页结果数",
                VerticalAlignment = VerticalAlignment.Center,
            });
            _pageSize = new ComboBox { Width = 100, Height = 26, Margin = new Thickness(8, 0, 0, 0) };
            _pageSize.Items.Add(20);
            _pageSize.Items.Add(30);
            _pageSize.Items.Add(50);
            _pageSize.Items.Add(100);
            _pageSize.SelectedItem = _pageSize.Items.Contains(_draft.SearchPageSize)
                ? (object)_draft.SearchPageSize
                : 30;
            searchPanel.Children.Add(_pageSize);
            searchGroup.Content = searchPanel;
            Grid.SetRow(searchGroup, 0);
            root.Children.Add(searchGroup);

            var libraryGroup = new GroupBox
            {
                Header = "默认下载位置",
                Padding = new Thickness(10),
                Margin = new Thickness(0, 12, 0, 0),
            };
            var libraryPanel = new Grid();
            libraryPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            libraryPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _targetSummary = new TextBlock
            {
                Text = _draft.TargetSummary(),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            libraryPanel.Children.Add(_targetSummary);
            var change = new Button { Content = "修改…", Width = 80, Height = 28 };
            Grid.SetColumn(change, 1);
            change.Click += (s, e) => ChangeTarget();
            libraryPanel.Children.Add(change);
            libraryGroup.Content = libraryPanel;
            Grid.SetRow(libraryGroup, 1);
            root.Children.Add(libraryGroup);

            var cacheGroup = new GroupBox
            {
                Header = "缓存",
                Padding = new Thickness(10),
                Margin = new Thickness(0, 12, 0, 0),
            };
            var cachePanel = new StackPanel();
            cachePanel.Children.Add(new TextBlock
            {
                Text = "搜索的封装数据与 3D 模型缓存在：",
                Foreground = Brushes.Gray,
            });
            cachePanel.Children.Add(new TextBlock
            {
                Text = PartCache.Root,
                Margin = new Thickness(0, 4, 0, 8),
                TextWrapping = TextWrapping.Wrap,
            });
            var openCache = new Button { Content = "打开缓存目录", Width = 120, Height = 28 };
            openCache.Click += (s, e) => OpenFolder(PartCache.Root);
            cachePanel.Children.Add(openCache);
            cacheGroup.Content = cachePanel;
            Grid.SetRow(cacheGroup, 2);
            root.Children.Add(cacheGroup);

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var ok = new Button { Content = "确定", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "取消", Width = 90, Height = 30 };
            ok.Click += (s, e) => Confirm();
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            footer.Children.Add(ok);
            footer.Children.Add(cancel);
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;
        }

        private void ChangeTarget()
        {
            var window = new TargetWindow(_draft) { Owner = this };
            if (window.ShowDialog() == true && window.Result != null)
            {
                CopyTarget(window.Result, _draft);
                _targetSummary.Text = _draft.TargetSummary();
            }
        }

        internal static void CopyTarget(AppSettings from, AppSettings to)
        {
            to.NewLibraryFolder = from.NewLibraryFolder;
            to.NewLibraryName = from.NewLibraryName;
            to.MergeIntoExisting = from.MergeIntoExisting;
            to.MergeSchLib = from.MergeSchLib;
            to.MergePcbLib = from.MergePcbLib;
            to.ImportSymbol = from.ImportSymbol;
            to.ImportFootprint = from.ImportFootprint;
            to.ImportModel = from.ImportModel;
            to.Overwrite = from.Overwrite;
            to.PlaceAfterImport = from.PlaceAfterImport;
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
                MessageBox.Show(ex.Message, "打开目录", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Confirm()
        {
            if (_pageSize.SelectedItem != null)
                _draft.SearchPageSize = (int)_pageSize.SelectedItem;

            Result = _draft;
            DialogResult = true;
            Close();
        }
    }
}
