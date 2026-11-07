using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LcscLoader;

namespace LcscStudio
{
    internal sealed class PanelForm : UserControl
    {
        private readonly AppSettings _settings;
        private readonly TextBox _search;
        private readonly Button _searchButton;
        private readonly Label _info;
        private readonly ListView _list;
        private readonly Label _partTitle;
        private readonly Label _partMeta;
        private readonly SymbolCanvas _symbolCanvas = new SymbolCanvas();
        private readonly FootprintCanvas _footprintCanvas = new FootprintCanvas();
        private readonly Label _modelHint = new Label
        {
            Text = "3D 预览暂不可用（导入后可在 AD 中查看模型）",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gray,
        };
        private readonly Label _targetLabel;
        private readonly Button _importButton;
        private readonly Button _placeButton;
        private readonly TextBox _log;
        private readonly ProgressBar _progress;
        private readonly TableLayoutPanel _root;
        private readonly Button[] _tabButtons = new Button[3];
        private Panel _logBody;
        private Button _logToggle;
        private bool _logExpanded = true;
        private TableLayoutPanel _headerTable;

        private CancellationTokenSource _searchCts;
        private PartDetails _details;
        private int _page = 1;
        private int _total;

        public PanelForm()
        {
            _settings = SettingsStore.Load();
            BackColor = Theme.Back;
            Font = Theme.UiFont;
            DoubleBuffered = true;

            _root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 8, 10, 8),
                ColumnCount = 1,
                RowCount = 7,
                BackColor = Theme.Back,
            };
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 44f));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 56f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84f));
            Controls.Add(_root);
            var root = _root;

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Margin = new Padding(0, 0, 0, 2),
                BackColor = Theme.Back,
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var title = new Label
            {
                Text = "立创选料",
                Font = Theme.TitleFont,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.Text,
                Margin = new Padding(0),
            };
            var actions = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0),
                BackColor = Theme.Back,
            };
            var settingsButton = Theme.StyleButton(new Button
            {
                Text = "设置", Width = 52, Height = 24, Margin = new Padding(4, 2, 0, 2),
            });
            var targetButton = Theme.StyleButton(new Button
            {
                Text = "下载位置", Width = 76, Height = 24, Margin = new Padding(0, 2, 0, 2),
            });
            foreach (var button in new[] { settingsButton, targetButton })
            {
                button.FlatAppearance.BorderSize = 0;
                button.BackColor = Theme.Back;
                button.ForeColor = Theme.TextMuted;
                button.Font = Theme.SmallFont;
            }
            settingsButton.Click += (s2, e2) => OpenSettings();
            targetButton.Click += (s2, e2) => ChangeTarget();
            actions.Controls.Add(settingsButton);
            actions.Controls.Add(targetButton);
            header.Controls.Add(title, 0, 0);
            header.Controls.Add(actions, 1, 0);
            root.Controls.Add(header, 0, 0);

            var searchRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                Margin = new Padding(0, 0, 0, 2),
                BackColor = Theme.Back,
            };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _search = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2), AccessibleName = "LCSC搜索框" };
            Theme.StyleTextBox(_search);
            _search.PreviewKeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.IsInputKey = true;
                    _page = 1;
                    RunSearch();
                }
            };
            _search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    _page = 1;
                    RunSearch();
                }
            };
            _searchButton = Theme.StyleButton(new Button
            {
                Text = "搜索", Width = 64, Height = 28, Margin = new Padding(4, 2, 0, 2),
                AccessibleName = "LCSC搜索按钮",
            });
            _searchButton.Click += (s, e) => { _page = 1; RunSearch(); };
            var pasteButton = Theme.StyleButton(new Button { Text = "粘贴", Width = 52, Height = 28, Margin = new Padding(4, 2, 0, 2) });
            pasteButton.Click += (s, e) => PasteAndSearch();
            searchRow.Controls.Add(_search, 0, 0);
            searchRow.Controls.Add(_searchButton, 1, 0);
            searchRow.Controls.Add(pasteButton, 2, 0);
            root.Controls.Add(searchRow, 0, 1);

            var listPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 0), BackColor = Theme.Back };
            var infoRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 22,
                ColumnCount = 3,
                AutoSize = false,
                BackColor = Theme.Back,
            };
            infoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            infoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            infoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _info = new Label
            {
                Text = "输入编号或型号开始搜索",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.TextMuted,
                Font = Theme.SmallFont,
                Margin = new Padding(0),
            };
            var prevButton = Theme.StyleButton(new Button { Text = "◀", Width = 24, Height = 20, Enabled = false, Font = Theme.SmallFont, Margin = new Padding(0) });
            var nextButton = Theme.StyleButton(new Button { Text = "▶", Width = 24, Height = 20, Enabled = false, Font = Theme.SmallFont, Margin = new Padding(0) });
            prevButton.Click += (s, e) => { if (_page > 1) { _page--; RunSearch(); } };
            nextButton.Click += (s, e) => { _page++; RunSearch(); };
            infoRow.Controls.Add(_info, 0, 0);
            infoRow.Controls.Add(prevButton, 1, 0);
            infoRow.Controls.Add(nextButton, 2, 0);
            infoRow.Tag = new[] { prevButton, nextButton };
            listPanel.Controls.Add(infoRow);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                HeaderStyle = ColumnHeaderStyle.None,
                Margin = new Padding(0),
            };
            Theme.StyleList(_list);
            _list.Columns.Add("编号", 82);
            _list.Columns.Add("型号", 120);
            _list.Columns.Add("封装", 78);
            _list.Columns.Add("库存", 52);
            _list.Columns.Add("单价", 52);
            _list.SmallImageList = new ImageList
            {
                ImageSize = new Size(1, 22),
                ColorDepth = ColorDepth.Depth32Bit,
            };
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is SearchItem item)
                    LoadDetails(item);
            };
            _list.SizeChanged += (s, e) => AdjustColumns();
            listPanel.Controls.Add(_list);
            var columnHeader = BuildColumnHeader();
            listPanel.Controls.Add(columnHeader);
            listPanel.Controls.Add(infoRow);
            columnHeader.BringToFront();
            root.Controls.Add(listPanel, 0, 2);

            var preview = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 0), BackColor = Theme.Back };
            _partTitle = new Label
            {
                Text = "未选择器件",
                Dock = DockStyle.Top,
                Height = 24,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                ForeColor = Theme.Text,
                Margin = new Padding(0),
            };
            _partMeta = new Label
            {
                Text = "",
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = Theme.TextMuted,
                Font = Theme.SmallFont,
                AutoEllipsis = true,
            };

            var tabStrip = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                WrapContents = false,
                BackColor = Theme.Back,
                Margin = new Padding(0),
            };
            var previewHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };

            _modelHint.Dock = DockStyle.Fill;
            _modelHint.TextAlign = ContentAlignment.MiddleCenter;
            _modelHint.ForeColor = Theme.TextMuted;
            _modelHint.BackColor = Theme.Canvas;
            _symbolCanvas.Dock = DockStyle.Fill;
            _footprintCanvas.Dock = DockStyle.Fill;
            previewHost.Controls.Add(_modelHint);
            previewHost.Controls.Add(_footprintCanvas);
            previewHost.Controls.Add(_symbolCanvas);

            var tabNames = new[] { "符号", "封装", "3D" };
            for (var i = 0; i < tabNames.Length; i++)
            {
                var index = i;
                var button = new Button
                {
                    Text = tabNames[i],
                    Width = 62,
                    Height = 26,
                    Margin = new Padding(0, 2, 6, 2),
                    Font = Theme.SmallFont,
                    FlatStyle = FlatStyle.Flat,
                    UseVisualStyleBackColor = false,
                    Cursor = Cursors.Hand,
                };
                button.FlatAppearance.BorderSize = 1;
                button.Click += (s, e) => SelectPreviewTab(index);
                _tabButtons[index] = button;
                tabStrip.Controls.Add(button);
            }

            preview.Controls.Add(previewHost);
            preview.Controls.Add(tabStrip);
            preview.Controls.Add(_partMeta);
            preview.Controls.Add(_partTitle);
            root.Controls.Add(preview, 0, 3);

            var footer = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 0), BackColor = Theme.Back };
            _targetLabel = new Label
            {
                Text = "下载位置：" + _settings.TargetSummary(),
                Dock = DockStyle.Top,
                Height = 18,
                ForeColor = Theme.TextMuted,
                Font = Theme.SmallFont,
                AutoEllipsis = true,
                Margin = new Padding(0),
            };
            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Theme.Back,
                Margin = new Padding(0),
            };
            _importButton = Theme.StyleButton(new Button
            {
                Text = "导入到库", Width = 108, Height = 30, Margin = new Padding(6, 2, 0, 2),
            }, true);
            _placeButton = Theme.StyleButton(new Button
            {
                Text = "放置到原理图", Width = 122, Height = 30, Margin = new Padding(0, 2, 0, 2),
            });
            _importButton.Click += OnImport;
            _placeButton.Click += OnPlace;
            buttonRow.Controls.Add(_importButton);
            buttonRow.Controls.Add(_placeButton);
            footer.Controls.Add(buttonRow);
            footer.Controls.Add(_targetLabel);
            root.Controls.Add(footer, 0, 4);

            _log = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
            };
            Theme.StyleTextBox(_log, true);
            _logBody = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                BackColor = Theme.Surface,
                Padding = new Padding(6, 4, 6, 4),
            };
            _progress = new ProgressBar
            {
                Dock = DockStyle.Bottom,
                Height = 3,
                Visible = false,
                Style = ProgressBarStyle.Continuous,
            };
            _logBody.Controls.Add(_log);
            _logBody.Controls.Add(_progress);

            var logHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Margin = new Padding(0, 2, 0, 0),
                BackColor = Theme.Back,
            };
            logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var logLabel = new Label
            {
                Text = "运行日志",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.TextMuted,
                Font = Theme.SmallFont,
                Margin = new Padding(0),
            };
            _logToggle = new Button
            {
                Text = "收起",
                Width = 52,
                Height = 22,
                Font = Theme.SmallFont,
                Margin = new Padding(0, 2, 0, 2),
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
                BackColor = Theme.Back,
                ForeColor = Theme.TextMuted,
            };
            _logToggle.FlatAppearance.BorderSize = 0;
            _logToggle.Click += (s, e) => ToggleLog();
            logHeader.Controls.Add(logLabel, 0, 0);
            logHeader.Controls.Add(_logToggle, 1, 0);
            root.Controls.Add(logHeader, 0, 5);
            root.Controls.Add(_logBody, 0, 6);

            UpdatePagerButtons();
            SelectPreviewTab(0);
            AdjustColumns();
        }

        private void SelectPreviewTab(int index)
        {
            _symbolCanvas.Visible = index == 0;
            _footprintCanvas.Visible = index == 1;
            _modelHint.Visible = index == 2;
            for (var i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] != null) Theme.SetTabActive(_tabButtons[i], i == index);
            }
        }

        private void ToggleLog()
        {
            _logExpanded = !_logExpanded;
            _root.RowStyles[6].Height = _logExpanded ? 84f : 0f;
            _logBody.Visible = _logExpanded;
            _logToggle.Text = _logExpanded ? "收起" : "展开";
        }

        public void FocusSearch()
        {
            _search.Focus();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct HDITEM
        {
            public int mask;
            public int cxy;
            public IntPtr pszText;
            public IntPtr hbm;
            public int cchTextMax;
            public int fmt;
            public IntPtr lParam;
            public int iImage;
            public int iOrder;
            public int type;
            public IntPtr pvFilter;
            public int state;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref HDITEM lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private void FixHeaderHeight()
        {
            try
            {
                var header = SendMessage(_list.Handle, 0x101F, IntPtr.Zero, IntPtr.Zero); // LVM_GETHEADER
                if (header == IntPtr.Zero) return;
                for (var i = 0; i < _list.Columns.Count; i++)
                {
                    var item = new HDITEM { mask = 0x0002, cxy = 30 }; // HDI_HEIGHT
                    SendMessage(header, 0x120C, new IntPtr(i), ref item); // HDM_SETITEMW
                }
            }
            catch
            {
                // Header sizing is cosmetic.
            }
        }

        private void UpdatePagerButtons()
        {
            // Pager buttons live inside the info row; locate them by tag.
            foreach (Control control in _list.Parent.Controls)
            {
                if (control.Tag is Button[] buttons)
                {
                    buttons[0].Enabled = _page > 1;
                    buttons[1].Enabled = _page * Math.Max(10, _settings.SearchPageSize) < _total;
                }
            }
        }

        private Control BuildColumnHeader()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 24,
                ColumnCount = 5,
                BackColor = Theme.Surface,
                Margin = new Padding(0),
            };
            var widths = new[] { 82, 120, 78, 52, 52 };
            var names = new[] { "编号", "型号", "封装", "库存", "单价" };
            for (var i = 0; i < widths.Length; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, widths[i]));
                table.Controls.Add(new Label
                {
                    Text = names[i],
                    Dock = DockStyle.Fill,
                    TextAlign = i >= 3 ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft,
                    ForeColor = Theme.TextMuted,
                    Font = Theme.SmallFont,
                    Margin = new Padding(2, 0, 2, 0),
                }, i, 0);
            }
            _headerTable = table;
            return table;
        }

        private void AdjustColumns()
        {
            if (_headerTable == null || _list.Columns.Count < 2) return;
            const int fixedWidth = 82 + 78 + 52 + 52;
            var nameWidth = _list.ClientSize.Width - fixedWidth - 2;
            if (nameWidth < 80) nameWidth = 80;
            if (_list.Columns[1].Width != nameWidth) _list.Columns[1].Width = nameWidth;
            if (_headerTable.ColumnStyles.Count > 1) _headerTable.ColumnStyles[1].Width = nameWidth;
        }

        private async void RunSearch()
        {
            var keyword = _search.Text.Trim();
            if (keyword.Length == 0)
            {
                _info.Text = "请输入立创编号或型号";
                return;
            }

            _searchCts?.Cancel();
            var cts = new CancellationTokenSource();
            _searchCts = cts;
            _searchButton.Enabled = false;
            _info.Text = "搜索中…";

            try
            {
                var page = await SearchClient.SearchAsync(
                    keyword, _page, Math.Max(10, _settings.SearchPageSize), cts.Token);
                if (cts.IsCancellationRequested) return;

                _total = page.Total;
                _list.BeginUpdate();
                _list.Items.Clear();
                foreach (var item in page.Items)
                {
                    var row = new ListViewItem(item.LcscId) { Tag = item };
                    row.SubItems.Add(item.Mpn);
                    row.SubItems.Add(item.Package);
                    row.SubItems.Add(item.StockText);
                    row.SubItems.Add(item.PriceText);
                    _list.Items.Add(row);
                }
                _list.EndUpdate();

                PluginLog.Write("panel search '" + keyword + "' total=" + page.Total +
                    " items=" + page.Items.Count);
                _info.Text = string.Format("共 {0:N0} 条 · 第 {1} 页", _total, _page);
                UpdatePagerButtons();

                if (_list.Items.Count > 0) _list.Items[0].Selected = true;
                else ClearDetails("没有匹配的器件");
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer search.
            }
            catch (Exception ex)
            {
                _info.Text = "搜索失败：" +
                    (ex.Message.Length > 60 ? ex.Message.Substring(0, 60) : ex.Message);
                Log("搜索失败：" + ex.Message);
                PluginLog.Write("panel search failed: " + ex);
            }
            finally
            {
                if (ReferenceEquals(_searchCts, cts)) _searchCts = null;
                _searchButton.Enabled = true;
            }
        }

        private void PasteAndSearch()
        {
            try
            {
                var text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text)) return;
                _search.Text = text.Trim();
                _page = 1;
                RunSearch();
            }
            catch (Exception ex)
            {
                Log("读取剪贴板失败：" + ex.Message);
            }
        }

        private async void LoadDetails(SearchItem item)
        {
            ClearDetails("正在加载器件数据…");
            _partTitle.Text = item.Mpn;
            _partMeta.Text = item.LcscId + " · " + item.Package + " · " + item.Manufacturer;
            try
            {
                var details = await PartCache.LoadAsync(item.LcscId, _settings.ImportModel, CancellationToken.None);
                _details = details;
                _partTitle.Text = details.Part.Title;
                _partMeta.Text = details.Part.LcscId + " · " + details.Part.Footprint.Name +
                                 " · " + details.Part.Manufacturer;
                _symbolCanvas.Symbol = details.Part.Symbol;
                _footprintCanvas.Footprint = details.Part.Footprint;
                _modelHint.Text = details.Part.Footprint.Model == null
                    ? "该器件没有 3D 模型"
                    : "3D 预览暂不可用（导入后可在 AD 中查看模型）";
            }
            catch (Exception ex)
            {
                _partMeta.Text = "器件数据加载失败：" + ex.Message;
            }
        }

        private void ClearDetails(string hint)
        {
            _details = null;
            _symbolCanvas.Symbol = null;
            _footprintCanvas.Footprint = null;
            _partMeta.Text = hint ?? "";
        }

        private void OpenSettings()
        {
            using (var form = new SettingsForm(_settings))
            {
                if (form.ShowDialog(FindForm()) != DialogResult.OK || form.Result == null) return;
                CopyTarget(form.Result, _settings);
                _settings.SearchPageSize = form.Result.SearchPageSize;
                SettingsStore.Save(_settings);
                UpdateTarget();
            }
        }

        private void ChangeTarget()
        {
            using (var form = new TargetForm(_settings))
            {
                if (form.ShowDialog(FindForm()) != DialogResult.OK || form.Result == null) return;
                CopyTarget(form.Result, _settings);
                SettingsStore.Save(_settings);
                UpdateTarget();
            }
        }

        private void UpdateTarget()
        {
            _targetLabel.Text = "下载位置：" + _settings.TargetSummary();
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

        private async void OnImport(object sender, EventArgs e)
        {
            var selected = _list.SelectedItems.Cast<ListViewItem>()
                .Select(row => row.Tag as SearchItem)
                .Where(item => item != null)
                .ToList();
            if (selected.Count == 0)
            {
                Log("请先选择器件。");
                return;
            }

            using (var form = new TargetForm(_settings))
            {
                if (form.ShowDialog(FindForm()) != DialogResult.OK || form.Result == null) return;
                CopyTarget(form.Result, _settings);
                SettingsStore.Save(_settings);
                UpdateTarget();
            }

            _importButton.Enabled = false;
            _progress.Visible = true;
            try
            {
                var request = new ImportRequest
                {
                    SchLibPath = _settings.ResolveSchLib(),
                    PcbLibPath = _settings.ResolvePcbLib(),
                    Symbol = _settings.ImportSymbol,
                    Footprint = _settings.ImportFootprint,
                    Overwrite = _settings.Overwrite,
                    Place = false,
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

                    request.Parts.Add(new ImportPart { Part = details.Part, Model = model });
                }

                var outcome = ImportService.Execute(request, (done, total) =>
                {
                    _progress.Value = total == 0 ? 0 : done * 100 / total;
                });

                var text = ImportLog.Text;
                if (!string.IsNullOrWhiteSpace(text)) Log(text.TrimEnd());
                Log(string.Format("导入完成：成功 {0} 个，失败 {1} 个。", outcome.Imported, outcome.Failed));

                if (_settings.PlaceAfterImport && request.Parts.Count == 1)
                {
                    var part = request.Parts[0].Part;
                    var reference = string.IsNullOrWhiteSpace(part.Symbol.Name) ? part.LcscId : part.Symbol.Name;
                    if (AdApi.PlaceFromLibrary(reference, _settings.ResolveSchLib()))
                        Log("已进入放置状态，请在原理图中点击放置：" + reference);
                    else
                        Log("自动放置失败，请手动点“放置到原理图”。");
                }
            }
            catch (Exception ex)
            {
                Log("导入失败：" + ex.Message);
                PluginLog.Write("panel import failed: " + ex);
            }
            finally
            {
                _importButton.Enabled = true;
                _progress.Visible = false;
            }
        }

        private async void OnPlace(object sender, EventArgs e)
        {
            if (_details == null)
            {
                Log("请先选择器件。");
                return;
            }

            _placeButton.Enabled = false;
            try
            {
                var schLib = _settings.ResolveSchLib();
                var exists = AdApi.SymbolExists(_details.LibReference, schLib);
                if (!exists || _settings.Overwrite)
                {
                    Log(exists ? "正在按最新规则刷新库中符号…" : "库中没有该器件，正在导入…");
                    var model = await BuildModelAsync(_details);
                    var request = new ImportRequest
                    {
                        SchLibPath = schLib,
                        PcbLibPath = _settings.ResolvePcbLib(),
                        Symbol = true,
                        Footprint = _settings.ImportFootprint,
                        Overwrite = _settings.Overwrite,
                        Place = false,
                    };
                    request.Parts.Add(new ImportPart { Part = _details.Part, Model = model });
                    ImportLog.Clear();
                    ImportService.Execute(request);
                    var text = ImportLog.Text;
                    if (!string.IsNullOrWhiteSpace(text)) Log(text.TrimEnd());
                }

                var reference = _details.LibReference;
                var timer = new System.Windows.Forms.Timer { Interval = 350 };
                timer.Tick += (sender2, args2) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    try
                    {
                        ImportLog.Clear();
                        if (AdApi.PlaceViaProcess(reference, schLib))
                        {
                            Log("已进入 AD 放置状态，移动鼠标到图纸上点击放下：" + reference);
                            return;
                        }
                        PluginLog.Write("process fallback: choose location");
                        if (AdApi.PlaceWithLocationPicker(reference, schLib))
                            Log("请在图纸上点击放置位置：" + reference);
                        else if (AdApi.PlaceInteractive(reference, schLib))
                            Log("已进入放置状态，请在原理图中移动并点击放置：" + reference);
                        else
                        {
                            var reason = ImportLog.Text.Trim();
                            Log(string.IsNullOrEmpty(reason)
                                ? "放置失败：请确认 AD 当前打开的是原理图，且库路径为 " + schLib
                                : "放置失败：" + reason);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("放置失败：" + ex.Message);
                    }
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Log("放置失败：" + ex.Message);
                PluginLog.Write("panel place failed: " + ex);
            }
            finally
            {
                _placeButton.Enabled = true;
            }
        }

        private async Task<Model3DData> BuildModelAsync(PartDetails details)
        {
            if (!_settings.ImportModel || details.Part.Footprint.Model == null) return null;
            await PartCache.EnsureStepAsync(details, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(details.StepPath)) return null;
            return new Model3DData
            {
                StepPath = details.StepPath,
                AnchorX = details.AnchorX,
                AnchorY = details.AnchorY,
                AnchorZ = details.AnchorZ,
            };
        }

        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            var line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
            _log.AppendText(_log.TextLength == 0 ? line + Environment.NewLine : line + Environment.NewLine);
        }
    }
}
