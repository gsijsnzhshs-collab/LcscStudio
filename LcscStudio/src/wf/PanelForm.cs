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

        private CancellationTokenSource _searchCts;
        private PartDetails _details;
        private int _page = 1;
        private int _total;

        public PanelForm()
        {
            _settings = SettingsStore.Load();
            BackColor = Color.FromArgb(0xF4, 0xF5, 0xF7);
            Font = new Font("Microsoft YaHei UI", 9f);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                ColumnCount = 1,
                RowCount = 6,
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80f));
            Controls.Add(root);

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                Margin = new Padding(0),
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var title = new Label
            {
                Text = "立创选料",
                Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 4, 0, 0),
            };
            var actions = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0),
            };
            var settingsButton = new Button { Text = "设置", Width = 68, Height = 26, Margin = new Padding(4, 0, 0, 0) };
            var targetButton = new Button { Text = "下载位置", Width = 100, Height = 26, Margin = new Padding(0) };
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
                AutoSize = true,
                ColumnCount = 3,
                Margin = new Padding(0, 4, 0, 0),
            };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _search = new TextBox { Dock = DockStyle.Fill, Height = 26, Margin = new Padding(0, 0, 4, 0), AccessibleName = "LCSC搜索框" };
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
            _searchButton = new Button { Text = "搜索", Width = 70, Height = 26, Margin = new Padding(0, 0, 4, 0), AccessibleName = "LCSC搜索按钮" };
            _searchButton.Click += (s, e) => { _page = 1; RunSearch(); };
            var pasteButton = new Button { Text = "粘贴", Width = 62, Height = 26 };
            pasteButton.Click += (s, e) => PasteAndSearch();
            searchRow.Controls.Add(_search, 0, 0);
            searchRow.Controls.Add(_searchButton, 1, 0);
            searchRow.Controls.Add(pasteButton, 2, 0);
            root.Controls.Add(searchRow, 0, 1);

            var listPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
            var infoRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 20,
                ColumnCount = 3,
                AutoSize = false,
            };
            infoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            infoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            infoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _info = new Label { Text = "输入编号或型号开始搜索", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
            var prevButton = new Button { Text = "◀", Width = 26, Height = 20, Enabled = false };
            var nextButton = new Button { Text = "▶", Width = 26, Height = 20, Enabled = false };
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
                BorderStyle = BorderStyle.FixedSingle,
                HeaderStyle = ColumnHeaderStyle.None,
            };
            _list.Columns.Add("编号", 80);
            _list.Columns.Add("型号", 150);
            _list.Columns.Add("封装", 130);
            _list.Columns.Add("库存", 80);
            _list.Columns.Add("单价", 80);
            _list.Font = new Font("Microsoft YaHei UI", 9f);
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is SearchItem item)
                    LoadDetails(item);
            };
            listPanel.Controls.Add(_list);
            var columnHeader = BuildColumnHeader();
            listPanel.Controls.Add(columnHeader);
            listPanel.Controls.Add(infoRow);
            columnHeader.BringToFront();
            root.Controls.Add(listPanel, 0, 2);

            var preview = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
            _partTitle = new Label { Text = "未选择器件", Dock = DockStyle.Top, Height = 20, Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold) };
            _partMeta = new Label { Text = "", Dock = DockStyle.Top, Height = 32, ForeColor = Color.DimGray };
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var symbolTab = new TabPage("符号");
            symbolTab.Controls.Add(_symbolCanvas);
            _symbolCanvas.Dock = DockStyle.Fill;
            var footprintTab = new TabPage("封装");
            footprintTab.Controls.Add(_footprintCanvas);
            _footprintCanvas.Dock = DockStyle.Fill;
            var modelTab = new TabPage("3D");
            modelTab.Controls.Add(_modelHint);
            tabs.TabPages.Add(symbolTab);
            tabs.TabPages.Add(footprintTab);
            tabs.TabPages.Add(modelTab);
            preview.Controls.Add(tabs);
            preview.Controls.Add(_partMeta);
            preview.Controls.Add(_partTitle);
            root.Controls.Add(preview, 0, 3);

            var footer = new Panel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            _targetLabel = new Label
            {
                Text = "下载位置：" + _settings.TargetSummary(),
                Dock = DockStyle.Top,
                Height = 18,
                ForeColor = Color.DimGray,
                AutoEllipsis = true,
            };
            var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, FlowDirection = FlowDirection.RightToLeft };
            _importButton = new Button { Text = "导入到库", Width = 110, Height = 28 };
            _placeButton = new Button { Text = "放置到原理图", Width = 130, Height = 28, Margin = new Padding(0, 0, 6, 0) };
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
                Font = new Font("Consolas", 8.5f),
                BackColor = Color.White,
            };
            var logPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
            _progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 3, Visible = false };
            logPanel.Controls.Add(_log);
            logPanel.Controls.Add(_progress);
            root.Controls.Add(logPanel, 0, 5);

            UpdatePagerButtons();
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            if (!_settings.DarkPanel) return;
            var back = Color.FromArgb(0x2B, 0x2D, 0x30);
            var panel = Color.FromArgb(0x33, 0x36, 0x3A);
            var text = Color.FromArgb(0xE6, 0xE6, 0xE6);
            BackColor = back;
            ApplyTheme(this, back, panel, text);
        }

        private static void ApplyTheme(Control parent, Color back, Color panel, Color text)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is TextBox tb)
                {
                    tb.BackColor = Color.FromArgb(0x1E, 0x1F, 0x22);
                    tb.ForeColor = text;
                }
                else if (c is ListView lv)
                {
                    lv.BackColor = Color.FromArgb(0x1E, 0x1F, 0x22);
                    lv.ForeColor = text;
                }
                else if (c is Button b)
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.BackColor = Color.FromArgb(0x3A, 0x3D, 0x41);
                    b.ForeColor = text;
                    b.FlatAppearance.BorderColor = Color.FromArgb(0x55, 0x58, 0x5C);
                }
                else if (c is TabPage tp)
                {
                    tp.BackColor = Color.FromArgb(0x26, 0x28, 0x2B);
                    tp.ForeColor = text;
                }
                else if (c is Label)
                {
                    c.ForeColor = text;
                    c.BackColor = Color.Transparent;
                }
                else if (c is Panel || c is TableLayoutPanel || c is FlowLayoutPanel)
                {
                    c.BackColor = panel;
                }
                ApplyTheme(c, back, panel, text);
            }
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

        private static Control BuildColumnHeader()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 26,
                ColumnCount = 5,
                BackColor = Color.FromArgb(0x3A, 0x3D, 0x41),
                Margin = new Padding(0),
            };
            var widths = new[] { 80, 150, 130, 80, 80 };
            var names = new[] { "编号", "型号", "封装", "库存", "单价" };
            for (var i = 0; i < widths.Length; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, widths[i]));
                table.Controls.Add(new Label
                {
                    Text = names[i],
                    Dock = DockStyle.Fill,
                    TextAlign = i >= 3 ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft,
                    ForeColor = Color.White,
                    Margin = new Padding(2, 0, 2, 0),
                }, i, 0);
            }
            return table;
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
                ApplyTheme();
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
