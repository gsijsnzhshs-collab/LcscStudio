using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LcscLoader;

namespace LcscStudio
{
    // Root control for the docked Altium panel.
    internal sealed class PanelControl : UserControl
    {
        private readonly AppSettings _settings;
        private TextBox _searchBox;
        private Button _searchButton;
        private TextBlock _resultInfo;
        private ListView _list;
        private readonly ObservableCollection<SearchItem> _items = new ObservableCollection<SearchItem>();

        private TextBlock _partTitle;
        private TextBlock _partMeta;
        private Image _partImage;
        private TextBlock _previewHint;
        private readonly SymbolPreview _symbolPreview = new SymbolPreview();
        private readonly FootprintPreview _footprintPreview = new FootprintPreview();
        private readonly Model3DPreview _modelPreview = new Model3DPreview();

        private TextBlock _targetLabel;
        private Button _importButton;
        private Button _placeButton;
        private Button _prevButton;
        private Button _nextButton;
        private TextBlock _log;
        private ProgressBar _progress;

        private CancellationTokenSource _searchCts;
        private CancellationTokenSource _detailCts;
        private PartDetails _details;
        private int _page = 1;
        private int _total;

        public PanelControl()
        {
            _settings = SettingsStore.Load();

            Background = new SolidColorBrush(Theme.Window);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 12;

            var root = new Grid { Margin = new Thickness(8) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 140 });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 200 });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(90) });

            root.Children.Add(BuildHeader());

            var searchBar = BuildSearchBar();
            Grid.SetRow(searchBar, 1);
            root.Children.Add(searchBar);

            _list = BuildList();
            Grid.SetRow(_list, 2);
            root.Children.Add(_list);

            var preview = BuildPreview();
            Grid.SetRow(preview, 3);
            root.Children.Add(preview);

            var footer = BuildFooter();
            Grid.SetRow(footer, 4);
            root.Children.Add(footer);

            var logBorder = new Border
            {
                Margin = new Thickness(0, 6, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFB, 0xFC)),
                BorderBrush = new SolidColorBrush(Theme.Border),
                BorderThickness = new Thickness(1),
            };
            var logPanel = new Grid();
            logPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            logPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _log = new TextBlock
            {
                Margin = new Thickness(4, 2, 4, 2),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Theme.Text),
            };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _log,
            };
            Grid.SetRow(scroll, 1);
            logPanel.Children.Add(scroll);
            _progress = new ProgressBar { Height = 3, Visibility = Visibility.Collapsed };
            logPanel.Children.Add(_progress);
            logBorder.Child = logPanel;
            Grid.SetRow(logBorder, 5);
            root.Children.Add(logBorder);

            Content = root;
            UpdateTargetLabel();

            Loaded += (s, e) => _searchBox.Focus();
        }

        private UIElement BuildHeader()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = "立创选料",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Theme.Text),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var targetButton = new Button { Content = "下载位置", Height = 26, Padding = new Thickness(8, 0, 8, 0) };
            targetButton.Click += (s, e) => ChangeTarget();
            var settingsButton = new Button
            {
                Content = "设置",
                Height = 26,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(6, 0, 0, 0),
            };
            settingsButton.Click += (s, e) => OpenSettings();
            actions.Children.Add(targetButton);
            actions.Children.Add(settingsButton);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
            return grid;
        }

        private UIElement BuildSearchBar()
        {
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _searchBox = new TextBox
            {
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0),
                ToolTip = "输入立创编号（如 C2040）或型号，回车搜索",
            };
            _searchBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Return)
                {
                    e.Handled = true;
                    _page = 1;
                    RunSearch();
                }
            };
            grid.Children.Add(_searchBox);

            _searchButton = new Button
            {
                Content = "搜索",
                Width = 60,
                Height = 28,
                Margin = new Thickness(6, 0, 0, 0),
            };
            _searchButton.Click += (s, e) => { _page = 1; RunSearch(); };
            Grid.SetColumn(_searchButton, 1);
            grid.Children.Add(_searchButton);

            var pasteButton = new Button
            {
                Content = "粘贴",
                Width = 48,
                Height = 28,
                Margin = new Thickness(4, 0, 0, 0),
            };
            pasteButton.Click += (sender, args) => PasteAndSearch();
            Grid.SetColumn(pasteButton, 2);
            grid.Children.Add(pasteButton);

            var infoRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _resultInfo = new TextBlock
            {
                Text = "输入编号或型号开始搜索",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Theme.Muted),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            infoRow.Children.Add(_resultInfo);

            _prevButton = new Button { Content = "◀", Width = 26, Height = 22, IsEnabled = false };
            _prevButton.Click += (s, e) => { if (_page > 1) { _page--; RunSearch(); } };
            Grid.SetColumn(_prevButton, 1);
            infoRow.Children.Add(_prevButton);

            _nextButton = new Button
            {
                Content = "▶",
                Width = 26,
                Height = 22,
                Margin = new Thickness(4, 0, 0, 0),
                IsEnabled = false,
            };
            _nextButton.Click += (s, e) => { _page++; RunSearch(); };
            Grid.SetColumn(_nextButton, 2);
            infoRow.Children.Add(_nextButton);

            var stack = new StackPanel();
            stack.Children.Add(grid);
            stack.Children.Add(infoRow);
            return stack;
        }

        private ListView BuildList()
        {
            var view = new GridView();
            view.Columns.Add(Column("编号", "LcscId", 66));
            view.Columns.Add(Column("型号", "Mpn", 120));
            view.Columns.Add(Column("封装", "Package", 96));
            view.Columns.Add(Column("库存", "StockText", 66));
            view.Columns.Add(Column("单价", "PriceText", 62));

            var list = new ListView
            {
                View = view,
                ItemsSource = _items,
                SelectionMode = SelectionMode.Extended,
                BorderBrush = new SolidColorBrush(Theme.Border),
                Background = Brushes.White,
                Margin = new Thickness(0, 6, 0, 0),
            };
            VirtualizingStackPanel.SetIsVirtualizing(list, true);
            VirtualizingStackPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            list.SelectionChanged += (s, e) =>
            {
                if (list.SelectedItem is SearchItem item) LoadDetails(item);
            };
            return list;
        }

        private static GridViewColumn Column(string header, string path, double width)
        {
            return new GridViewColumn
            {
                Header = header,
                Width = width,
                DisplayMemberBinding = new Binding(path),
            };
        }

        private UIElement BuildPreview()
        {
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _partImage = new Image
            {
                Width = 40,
                Height = 40,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 6, 0),
            };
            header.Children.Add(_partImage);

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _partTitle = new TextBlock
            {
                Text = "未选择器件",
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Theme.Text),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _partMeta = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Theme.Muted),
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
            };
            _previewHint = new TextBlock
            {
                Foreground = new SolidColorBrush(Theme.Danger),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            };
            texts.Children.Add(_partTitle);
            texts.Children.Add(_partMeta);
            texts.Children.Add(_previewHint);
            Grid.SetColumn(texts, 1);
            header.Children.Add(texts);
            grid.Children.Add(header);

            var tabs = new TabControl { Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(0) };
            tabs.Items.Add(new TabItem { Header = "符号", Content = _symbolPreview });
            tabs.Items.Add(new TabItem { Header = "封装", Content = _footprintPreview });
            tabs.Items.Add(new TabItem { Header = "3D", Content = _modelPreview });
            Grid.SetRow(tabs, 1);
            grid.Children.Add(tabs);
            return grid;
        }

        private UIElement BuildFooter()
        {
            var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            _targetLabel = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Theme.Muted),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 4),
            };
            stack.Children.Add(_targetLabel);

            var buttons = new Grid();
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _placeButton = new Button { Content = "放置到原理图", Height = 28, Padding = new Thickness(8, 0, 8, 0) };
            _placeButton.Click += OnPlace;
            Grid.SetColumn(_placeButton, 1);
            buttons.Children.Add(_placeButton);

            _importButton = new Button
            {
                Content = "导入到库",
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(6, 0, 0, 0),
                Background = new SolidColorBrush(Theme.Accent),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Theme.Accent),
                FontWeight = FontWeights.SemiBold,
            };
            _importButton.Click += OnImport;
            Grid.SetColumn(_importButton, 2);
            buttons.Children.Add(_importButton);

            stack.Children.Add(buttons);
            return stack;
        }

        private async void RunSearch()
        {
            var keyword = _searchBox.Text.Trim();
            if (keyword.Length == 0)
            {
                _resultInfo.Text = "请输入立创编号或型号";
                return;
            }

            _searchCts?.Cancel();
            var cts = new CancellationTokenSource();
            _searchCts = cts;
            _searchButton.IsEnabled = false;
            _resultInfo.Text = "搜索中…";

            try
            {
                var page = await SearchClient.SearchAsync(
                    keyword, _page, Math.Max(10, _settings.SearchPageSize), cts.Token);
                if (cts.IsCancellationRequested) return;

                _total = page.Total;
                _items.Clear();
                foreach (var item in page.Items) _items.Add(item);

                PluginLog.Write("panel search '" + keyword + "' total=" + page.Total +
                    " items=" + page.Items.Count);
                _resultInfo.Text = string.Format("共 {0:N0} 条 · 第 {1} 页", _total, _page);
                if (page.Items.Count == 0)
                    _resultInfo.Text = "共 0 条（换个关键词试试）";
                _prevButton.IsEnabled = _page > 1;
                _nextButton.IsEnabled = _page * Math.Max(10, _settings.SearchPageSize) < _total;

                if (_items.Count > 0) _list.SelectedIndex = 0;
                else ClearDetails("没有匹配的器件");
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer search.
            }
            catch (Exception ex)
            {
                _resultInfo.Text = "搜索失败：" +
                    (ex.Message.Length > 60 ? ex.Message.Substring(0, 60) : ex.Message);
                Log("搜索失败：" + ex.Message);
                PluginLog.Write("panel search failed: " + ex);
            }
            finally
            {
                if (ReferenceEquals(_searchCts, cts)) _searchCts = null;
                _searchButton.IsEnabled = true;
            }
        }

        private async void LoadDetails(SearchItem item)
        {
            _detailCts?.Cancel();
            var cts = new CancellationTokenSource();
            _detailCts = cts;

            ClearDetails("正在加载器件数据…");
            _partTitle.Text = item.Mpn;
            _partMeta.Text = item.LcscId + " · " + item.Package + " · " + item.Manufacturer;
            LoadImage(item.ImageUrl);

            try
            {
                var details = await PartCache.LoadAsync(item.LcscId, _settings.ImportModel, cts.Token);
                if (cts.IsCancellationRequested) return;

                _details = details;
                _partTitle.Text = details.Part.Title;
                _partMeta.Text = details.Part.LcscId + " · " + details.Part.Footprint.Name +
                                 " · " + details.Part.Manufacturer;

                _symbolPreview.Symbol = details.Part.Symbol;
                _footprintPreview.Footprint = details.Part.Footprint;
                _modelPreview.SetMesh(details.Mesh);

                _previewHint.Text = !_settings.ImportModel
                    ? ""
                    : details.Part.Footprint.Model == null
                        ? "该器件没有 3D 模型"
                        : details.Mesh == null
                            ? "3D 模型预览不可用（导入后由 AD 加载）"
                            : "";
            }
            catch (OperationCanceledException)
            {
                // Superseded by another selection.
            }
            catch (Exception ex)
            {
                _previewHint.Text = "器件数据加载失败：" + ex.Message;
            }
        }

        private void ClearDetails(string hint)
        {
            _details = null;
            _symbolPreview.Symbol = null;
            _footprintPreview.Footprint = null;
            _modelPreview.SetMesh(null);
            _previewHint.Text = hint ?? "";
            _partImage.Source = null;
        }

        private void LoadImage(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(url, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                _partImage.Source = image;
            }
            catch
            {
                _partImage.Source = null;
            }
        }

        private void OpenSettings()
        {
            var window = new SettingsWindow(_settings);
            var owner = Window.GetWindow(this);
            if (owner != null) window.Owner = owner;
            if (window.ShowDialog() == true && window.Result != null)
            {
                ApplySettings(window.Result);
            }
        }

        private void ChangeTarget()
        {
            var window = new TargetWindow(_settings);
            var owner = Window.GetWindow(this);
            if (owner != null) window.Owner = owner;
            if (window.ShowDialog() == true && window.Result != null)
            {
                SettingsWindow.CopyTarget(window.Result, _settings);
                PersistSettings();
            }
        }

        private void ApplySettings(AppSettings updated)
        {
            SettingsWindow.CopyTarget(updated, _settings);
            _settings.SearchPageSize = updated.SearchPageSize;
            PersistSettings();
        }

        private void PersistSettings()
        {
            SettingsStore.Save(_settings);
            UpdateTargetLabel();
        }

        private void UpdateTargetLabel()
        {
            _targetLabel.Text = "下载位置：" + _settings.TargetSummary();
        }

        private async void OnImport(object sender, RoutedEventArgs e)
        {
            var selected = _list.SelectedItems.Cast<SearchItem>().ToList();
            if (selected.Count == 0)
            {
                Log("请先选择器件。");
                return;
            }

            var targetWindow = new TargetWindow(_settings);
            var owner = Window.GetWindow(this);
            if (owner != null) targetWindow.Owner = owner;
            if (targetWindow.ShowDialog() != true || targetWindow.Result == null) return;

            SettingsWindow.CopyTarget(targetWindow.Result, _settings);
            PersistSettings();

            _importButton.IsEnabled = false;
            _progress.Visibility = Visibility.Visible;
            try
            {
                var request = new ImportRequest
                {
                    SchLibPath = _settings.ResolveSchLib(),
                    PcbLibPath = _settings.ResolvePcbLib(),
                    Symbol = _settings.ImportSymbol,
                    Footprint = _settings.ImportFootprint,
                    Overwrite = _settings.Overwrite,
                    Place = _settings.PlaceAfterImport && selected.Count == 1,
                };

                ImportLog.Clear();
                foreach (var item in selected)
                {
                    Log("准备 " + item.LcscId + " …");
                    var details = _details != null && _details.Part.LcscId == item.LcscId
                        ? _details
                        : await PartCache.LoadAsync(item.LcscId, _settings.ImportModel, CancellationToken.None);

                    if (_settings.ImportModel && details.Part.Footprint.Model != null)
                    {
                        if (!await PartCache.EnsureStepAsync(details, CancellationToken.None))
                            Log("警告：3D 模型下载失败，仅导入符号/封装：" + item.LcscId);
                    }

                    var part = details.Part;
                    Model3DData model = null;
                    if (!string.IsNullOrWhiteSpace(details.StepPath))
                    {
                        model = new Model3DData
                        {
                            StepPath = details.StepPath,
                            AnchorX = details.AnchorX,
                            AnchorY = details.AnchorY,
                            AnchorZ = details.AnchorZ,
                        };
                    }

                    request.Parts.Add(new ImportPart { Part = part, Model = model });
                }

                var outcome = ImportService.Execute(request, (done, total) =>
                {
                    _progress.Value = total == 0 ? 0 : done * 100.0 / total;
                });

                var text = ImportLog.Text;
                if (!string.IsNullOrWhiteSpace(text)) Log(text.TrimEnd());
                Log(string.Format("导入完成：成功 {0} 个，失败 {1} 个。", outcome.Imported, outcome.Failed));
            }
            catch (Exception ex)
            {
                Log("导入失败：" + ex.Message);
                PluginLog.Write("panel import failed: " + ex);
            }
            finally
            {
                _importButton.IsEnabled = true;
                _progress.Visibility = Visibility.Collapsed;
            }
        }

        private void OnPlace(object sender, RoutedEventArgs e)
        {
            if (_details == null)
            {
                Log("请先选择器件。");
                return;
            }

            try
            {
                SymbolImporter.PlaceOnCurrentSheet(_details.LibReference, _settings.ResolveSchLib());
                Log("已放置到当前原理图：" + _details.LibReference);
            }
            catch (Exception ex)
            {
                Log("放置失败：" + ex.Message);
            }
        }

        private void PasteAndSearch()
        {
            try
            {
                var text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text)) return;
                _searchBox.Text = text.Trim();
                _page = 1;
                RunSearch();
            }
            catch (Exception ex)
            {
                Log("读取剪贴板失败：" + ex.Message);
            }
        }

        public void FocusSearch()
        {
            _searchBox.Focus();
            System.Windows.Input.Keyboard.Focus(_searchBox);
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            var line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
            _log.Text = string.IsNullOrEmpty(_log.Text) ? line : _log.Text + Environment.NewLine + line;
        }
    }
}
