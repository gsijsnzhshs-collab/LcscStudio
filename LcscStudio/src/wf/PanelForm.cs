using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
        private readonly ListView _paramList;
        private readonly Label _partTitle;
        private readonly Label _partMeta;
        private readonly SymbolCanvas _symbolCanvas = new SymbolCanvas();
        private readonly FootprintCanvas _footprintCanvas = new FootprintCanvas();
        private readonly ScrollStrip _listScroll = new ScrollStrip();
        private readonly ScrollStrip _logScroll = new ScrollStrip();
        private readonly System.Windows.Forms.Timer _scrollSync = new System.Windows.Forms.Timer { Interval = 120 };
        private const int LvmGetTopIndex = 0x1027;
        private const int LvmGetCountPerPage = 0x1028;
        private const int EmGetFirstVisibleLine = 0x00CE;
        private const int EmLineScroll = 0x00B6;
        private int _hotIndex = -1;
        private readonly Label[] _headerLabels = new Label[5];
        private readonly FlowLayoutPanel _viewBar = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Canvas,
            Margin = new Padding(0),
        };
        private Button _zoomOutButton;
        private Button _zoomInButton;
        private Button _resetViewButton;
        private readonly Label _modelHint = new Label
        {
            Text = "3D 预览暂不可用\n导入后可在 AD 中查看模型",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.TextMuted,
        };
        private readonly Label _targetLabel;
        private readonly Button _importButton;
        private readonly Button _placeButton;
        private Button _cancelImportButton;
        private CancellationTokenSource _importCts;
        private readonly Button _prevButton;
        private readonly Button _nextButton;
        private readonly TextBox _log;
        private readonly ProgressBar _progress;
        private readonly TableLayoutPanel _root;
        private readonly Button[] _tabButtons = new Button[4];
        private Panel _logBody;
        private Button _logToggle;
        private bool _logExpanded = true;
        private int _previewTab;
        private TableLayoutPanel _headerTable;
        private readonly ToolTip _targetTip = new ToolTip();

        private CancellationTokenSource _searchCts;
        private PartDetails _details;
        private int _page = 1;
        private int _total;

        public PanelForm()
        {
            _settings = SettingsStore.Load();
            // WinForms inherits AD's font-based AutoScaleMode here, which scales our
            // 9pt layout to 1.5x inside the 289px docked host. Opt out explicitly.
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Back;
            Font = Theme.UiFont;
            DoubleBuffered = true;

            _root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 10, 10, 10),
                ColumnCount = 1,
                RowCount = 7,
                BackColor = Theme.Back,
            };
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 44f));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 56f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92f));
            Controls.Add(_root);
            var root = _root;

            var header = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 2),
                BackColor = Theme.Back,
            };
            var settingsButton = Theme.StyleButton(new Button
            {
                Text = "设置",
                Margin = new Padding(4, 4, 0, 4),
            });
            var targetButton = Theme.StyleButton(new Button
            {
                Text = "下载位置",
                Margin = new Padding(0, 4, 0, 4),
            });
            foreach (var button in new[] { settingsButton, targetButton })
            {
                button.FlatAppearance.BorderSize = 0;
                button.BackColor = Theme.Back;
                button.ForeColor = Theme.Text;
                button.Font = Theme.SmallFont;
                Theme.SizeToText(button, 26, 44);
            }
            settingsButton.Click += (s2, e2) => OpenSettings();
            targetButton.Click += (s2, e2) => ChangeTarget();
            header.Controls.Add(settingsButton);
            header.Controls.Add(targetButton);
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
                Text = "搜索",
                Margin = new Padding(4, 3, 0, 3),
                AccessibleName = "LCSC搜索按钮",
            });
            Theme.SizeToText(_searchButton, 30, 56);
            _searchButton.Click += (s, e) => { _page = 1; RunSearch(); };
            var pasteButton = Theme.StyleButton(new Button
            {
                Text = "粘贴",
                Margin = new Padding(4, 3, 0, 3),
            });
            Theme.SizeToText(pasteButton, 30, 46);
            pasteButton.Click += (s, e) => PasteAndSearch();
            searchRow.Controls.Add(_search, 0, 0);
            searchRow.Controls.Add(_searchButton, 1, 0);
            searchRow.Controls.Add(pasteButton, 2, 0);
            root.Controls.Add(searchRow, 0, 1);

            var listPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 6, 0, 8),
            };
            Theme.StyleCard(listPanel, new Padding(6));
            listPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            listPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            var infoRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                Margin = new Padding(0),
                AutoSize = false,
                BackColor = Theme.Surface,
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
                Font = Theme.LabelFont,
                Margin = new Padding(0),
                AutoEllipsis = true,
            };
            _prevButton = Theme.StyleButton(new Button { Text = "◀", Enabled = false, Font = Theme.SmallFont, Margin = new Padding(0, 3, 2, 3) });
            _nextButton = Theme.StyleButton(new Button { Text = "▶", Enabled = false, Font = Theme.SmallFont, Margin = new Padding(0, 3, 0, 3) });
            Theme.SizeToText(_prevButton, 24, 30);
            Theme.SizeToText(_nextButton, 24, 30);
            _prevButton.Click += (s, e) => { if (_page > 1) { _page--; RunSearch(); } };
            _nextButton.Click += (s, e) => { _page++; RunSearch(); };
            infoRow.Controls.Add(_info, 0, 0);
            infoRow.Controls.Add(_prevButton, 1, 0);
            infoRow.Controls.Add(_nextButton, 2, 0);
            listPanel.Controls.Add(infoRow, 0, 0);

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
                ImageSize = new Size(1, 26),
                ColorDepth = ColorDepth.Depth32Bit,
            };
            _list.OwnerDraw = true;
            _list.DrawItem += DrawRow;
            // Details 视图下系统还会对每个单元格做一次默认绘制，会覆盖 DrawItem 里画的
            // 整行底色（选中行只剩最底部一条蓝线）。挂空处理器即可关掉默认单元格绘制。
            _list.DrawSubItem += (s, e) => { };
            Theme.EnableDarkScrollbars(_list);
            _list.HandleCreated += (s, e) =>
            {
                SendMessage(_list.Handle, 0x1036, new IntPtr(0x00010000), new IntPtr(0x00010000));
                LayoutColumns();
            };
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is SearchItem item)
                    LoadDetails(item);
            };
            _list.SizeChanged += (s, e) => LayoutColumns();
            _list.VisibleChanged += (s, e) => LayoutColumns();
            _list.MouseMove += (s, e) =>
            {
                var hit = _list.GetItemAt(e.X, e.Y);
                var index = hit == null ? -1 : hit.Index;
                if (index == _hotIndex) return;
                var previous = _hotIndex;
                _hotIndex = index;
                InvalidateRow(previous);
                InvalidateRow(index);
            };
            _list.MouseLeave += (s, e) =>
            {
                if (_hotIndex < 0) return;
                var previous = _hotIndex;
                _hotIndex = -1;
                InvalidateRow(previous);
            };
            var columnHeader = BuildColumnHeader();
            Theme.AddBottomBorder(columnHeader, Theme.Border);
            columnHeader.Paint += (s, e) =>
            {
                float x = 0;
                using (var pen = new Pen(Theme.Divider))
                {
                    for (var i = 0; i < 4 && i < _headerTable.ColumnStyles.Count - 1; i++)
                    {
                        var width = _headerTable.ColumnStyles[i].Width;
                        if (width <= 0) continue;
                        x += width;
                        e.Graphics.DrawLine(pen, x - 1, 3, x - 1, columnHeader.Height - 3);
                    }
                }
            };
            // 表头与列表放进同一个表格：表头固定 24px，列表占据剩余空间，
            // 避免之前两者重叠导致第一行被表头盖住、选中高亮只剩一条边。
            var listTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Theme.Surface,
            };
            listTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            listTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            columnHeader.Dock = DockStyle.Fill;
            listTable.Controls.Add(columnHeader, 0, 0);

            // 系统滚动条在 AD 宿主里只把轨道染暗、拇指仍是浅灰；隐藏它，
            // 换成右侧自绘的深色圆角细条。
            var listHost = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                BackColor = Theme.InputBack,
            };
            _list.Dock = DockStyle.Fill;
            // 覆盖在系统滚动条上：不参与停靠布局，避免搜索时列表宽度抖动。
            _listScroll.Dock = DockStyle.None;
            _listScroll.Width = Math.Max(16, SystemInformation.VerticalScrollBarWidth);
            _listScroll.Margin = new Padding(0);
            listHost.Controls.Add(_list);
            listHost.Controls.Add(_listScroll);
            _listScroll.BringToFront();
            listHost.Resize += (s, e) => PlaceListScroll();
            listHost.HandleCreated += (s, e) => PlaceListScroll();
            listTable.Controls.Add(listHost, 0, 1);
            listPanel.Controls.Add(listTable, 0, 1);
            root.Controls.Add(listPanel, 0, 2);

            var preview = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            Theme.StyleCard(preview, new Padding(8));
            _partTitle = new Label
            {
                Text = "未选择器件",
                Dock = DockStyle.Top,
                Height = 26,
                Font = Theme.TitleFont,
                ForeColor = Theme.Text,
                Margin = new Padding(0),
            };
            _partMeta = new Label
            {
                Text = "",
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = Theme.TextMuted,
                Font = Theme.LabelFont,
                AutoEllipsis = true,
            };

            var tabStrip = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                WrapContents = false,
                BackColor = Theme.Surface,
                Margin = new Padding(0),
            };
            // 画布降为 L2 并留 1px 内框，和区块卡形成内嵌关系。
            var previewHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Border,
                Padding = new Padding(1),
            };

            _modelHint.Dock = DockStyle.Fill;
            _modelHint.TextAlign = ContentAlignment.MiddleCenter;
            _modelHint.ForeColor = Theme.TextMuted;
            _modelHint.BackColor = Theme.Canvas;
            _symbolCanvas.Dock = DockStyle.Fill;
            _footprintCanvas.Dock = DockStyle.Fill;
            previewHost.Controls.Add(_modelHint);
            previewHost.Controls.Add(_footprintCanvas);
            previewHost.Controls.Add(_symbolCanvas);

            // 参数页：选中器件后列出解析出的全部字段；值列吃掉剩余宽度，长值用 tooltip 兜住。
            _paramList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = true,
                BorderStyle = BorderStyle.None,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                ShowItemToolTips = true,
                Margin = new Padding(0),
                Visible = false,
            };
            Theme.StyleList(_paramList);
            _paramList.Columns.Add("参数", 104);
            _paramList.Columns.Add("值", 240);
            _paramList.SmallImageList = new ImageList
            {
                ImageSize = new Size(1, 22),
                ColorDepth = ColorDepth.Depth32Bit,
            };
            _paramList.OwnerDraw = true;
            _paramList.DrawItem += DrawParameterRow;
            _paramList.DrawColumnHeader += DrawParameterHeader;
            // Details 视图会对每个单元格再做一次默认绘制，会盖掉整行底色。
            _paramList.DrawSubItem += (s, e) => { };
            Theme.EnableDarkScrollbars(_paramList);
            _paramList.Resize += (s, e) => LayoutParameterColumns();
            previewHost.Controls.Add(_paramList);

            // 预览右下角的缩放条：方形加减按钮，重置按钮画成向外扩张的箭头；
            // 平移直接按住左键拖动。
            _zoomOutButton = Theme.StyleButton(new Button { Text = "－", Margin = new Padding(0, 0, 4, 0) });
            _zoomOutButton.Font = Theme.SmallFont;
            _zoomOutButton.AutoSize = false;
            _zoomOutButton.Size = new Size(26, 26);
            _zoomInButton = Theme.StyleButton(new Button { Text = "＋", Margin = new Padding(0, 0, 4, 0) });
            _zoomInButton.Font = Theme.SmallFont;
            _zoomInButton.AutoSize = false;
            _zoomInButton.Size = new Size(26, 26);
            _resetViewButton = Theme.StyleButton(new Button { Text = "", Margin = new Padding(0) });
            _resetViewButton.Font = Theme.SmallFont;
            _resetViewButton.AutoSize = false;
            _resetViewButton.Size = new Size(26, 26);
            _resetViewButton.Paint += (s, e) => DrawResetIcon(e.Graphics, _resetViewButton.ClientRectangle);
            _zoomOutButton.Click += (s, e) => ZoomActive(-1);
            _zoomInButton.Click += (s, e) => ZoomActive(1);
            _resetViewButton.Click += (s, e) => ResetActiveView();
            _viewBar.Controls.Add(_zoomOutButton);
            _viewBar.Controls.Add(_zoomInButton);
            _viewBar.Controls.Add(_resetViewButton);
            previewHost.Controls.Add(_viewBar);
            _viewBar.BringToFront();
            previewHost.Resize += (s, e) => PlaceViewBar(previewHost);

            var tabNames = new[] { "符号", "封装", "3D", "参数" };
            for (var i = 0; i < tabNames.Length; i++)
            {
                var index = i;
                var button = new Button
                {
                    Text = tabNames[i],
                    Margin = new Padding(0, 3, 6, 3),
                    Font = Theme.SmallFont,
                    FlatStyle = FlatStyle.Flat,
                    UseVisualStyleBackColor = false,
                    Cursor = Cursors.Hand,
                };
                button.FlatAppearance.BorderSize = 1;
                Theme.SizeToText(button, 28, 52);
                Theme.EnableOwnTextPaint(button);
                // 活动页签的底部 2px 强调条，替代原来的整块实心蓝。
                button.Paint += (s, e) =>
                {
                    if (index != _previewTab) return;
                    using (var pen = new Pen(Theme.Accent, 2))
                        e.Graphics.DrawLine(pen, 2, button.Height - 2, button.Width - 3, button.Height - 2);
                };
                button.MouseEnter += (s, e) =>
                {
                    if (index != _previewTab) button.BackColor = Theme.SurfaceHover;
                };
                button.MouseLeave += (s, e) => Theme.SetTabActive(button, index == _previewTab);
                button.Click += (s, e) => SelectPreviewTab(index);
                _tabButtons[index] = button;
                tabStrip.Controls.Add(button);
            }

            preview.Controls.Add(previewHost);
            preview.Controls.Add(tabStrip);
            preview.Controls.Add(_partMeta);
            preview.Controls.Add(_partTitle);
            root.Controls.Add(preview, 0, 3);

            var footer = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            Theme.StyleCard(footer, new Padding(8));
            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Theme.Surface,
                Margin = new Padding(0),
            };
            _importButton = Theme.StyleButton(new Button
            {
                Text = "导入到库", Margin = new Padding(6, 2, 0, 2),
            }, true);
            _placeButton = Theme.StyleButton(new Button
            {
                Text = "放置到原理图", Margin = new Padding(0, 2, 0, 2),
            });
            _cancelImportButton = Theme.StyleButton(new Button
            {
                Text = "取消", Margin = new Padding(6, 2, 0, 2), Visible = false,
            });
            Theme.SizeToText(_cancelImportButton, 30, 70);
            Theme.SizeToText(_importButton, 30, 104);
            Theme.SizeToText(_placeButton, 30, 130);
            _importButton.Click += OnImport;
            _placeButton.Click += OnPlace;
            _cancelImportButton.Click += (s, e) =>
            {
                if (_importCts == null) return;
                _importCts.Cancel();
                _cancelImportButton.Enabled = false;
                Log("正在取消导入…");
            };
            buttonRow.Controls.Add(_importButton);
            buttonRow.Controls.Add(_placeButton);
            buttonRow.Controls.Add(_cancelImportButton);

            // 下载位置单独成行：说明文字弱化、路径用正文色，长路径从中间省略以保留最末一级目录。
            var targetRow = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = Theme.Surface,
                Margin = new Padding(0),
            };
            var targetCaption = new Label
            {
                Text = "下载位置",
                Dock = DockStyle.Left,
                Width = 88,
                ForeColor = Theme.TextMuted,
                Font = Theme.LabelFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0),
                BackColor = Theme.Surface,
            };
            _targetLabel = new Label
            {
                Text = "",
                ForeColor = Theme.Text,
                Font = Theme.SmallFont,
                AutoEllipsis = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0),
                BackColor = Theme.Surface,
            };
            _targetTip.SetToolTip(_targetLabel, _settings.TargetSummary());
            _targetLabel.SizeChanged += (s2, e2) => FitTargetText();
            // 这里的 Dock=Fill 在宿主内不会把宽度铺满，改为跟随容器尺寸显式设置。
            targetRow.Controls.Add(_targetLabel);
            targetRow.Controls.Add(targetCaption);
            targetRow.Resize += (s2, e2) =>
            {
                _targetLabel.SetBounds(88, 0, Math.Max(0, targetRow.Width - 88), targetRow.Height);
                FitTargetText();
            };

            footer.Controls.Add(buttonRow);
            footer.Controls.Add(targetRow);
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
            _logScroll.Dock = DockStyle.None;
            _logScroll.Width = Math.Max(16, SystemInformation.VerticalScrollBarWidth);
            _logScroll.Margin = new Padding(0);
            _logBody.Controls.Add(_log);
            _logBody.Controls.Add(_logScroll);
            _logBody.Controls.Add(_progress);
            _log.Resize += (s, e) => PlaceLogScroll();
            _logBody.Resize += (s, e) => PlaceLogScroll();

            var logHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Margin = new Padding(0),
                BackColor = Theme.Surface,
            };
            logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var logLabel = new Label
            {
                Text = "运行日志",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.TextMuted,
                Font = Theme.LabelFont,
                Margin = new Padding(0),
            };
            _logToggle = new Button
            {
                Text = "收起",
                Font = Theme.SmallFont,
                Margin = new Padding(0, 3, 0, 3),
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
            };
            _logToggle.FlatAppearance.BorderSize = 0;
            Theme.SizeToText(_logToggle, 24, 44);
            Theme.EnableOwnTextPaint(_logToggle);
            _logToggle.Click += (s, e) => ToggleLog();
            logHeader.Controls.Add(logLabel, 0, 0);
            logHeader.Controls.Add(_logToggle, 1, 0);
            root.Controls.Add(logHeader, 0, 5);
            root.Controls.Add(_logBody, 0, 6);
            Theme.AddTopBorder(logHeader, Theme.Edge);

            UpdatePagerButtons();
            SelectPreviewTab(0);
            PlaceViewBar(previewHost);
            LayoutColumns();
            FitTargetText();
            _listScroll.ValueChanged += (s, e) => ScrollListTo(_listScroll.Value);
            _logScroll.ValueChanged += (s, e) => ScrollLogTo(_logScroll.Value);
            _scrollSync.Tick += (s, e) => SyncScrollBars();
            _scrollSync.Start();
            Disposed += (s, e) =>
            {
                _scrollSync.Stop();
                _scrollSync.Dispose();
            };
            HandleCreated += (s, e) => PluginLog.Write("panel dpi=" + DeviceDpi);
        }

        private void SelectPreviewTab(int index)
        {
            _previewTab = index;
            _symbolCanvas.Visible = index == 0;
            _footprintCanvas.Visible = index == 1;
            _modelHint.Visible = index == 2;
            _paramList.Visible = index == 3;
            if (index == 3) LayoutParameterColumns();
            _viewBar.Visible = index < 2;
            for (var i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] == null) continue;
                Theme.SetTabActive(_tabButtons[i], i == index);
                _tabButtons[i].Invalidate();
            }
        }

        private ZoomCanvas ActiveZoomCanvas()
        {
            if (_previewTab == 0) return _symbolCanvas;
            if (_previewTab == 1) return _footprintCanvas;
            return null;
        }

        private void ZoomActive(int direction)
        {
            var canvas = ActiveZoomCanvas();
            if (canvas == null) return;
            if (direction > 0) canvas.ZoomIn(); else canvas.ZoomOut();
        }

        private void ResetActiveView()
        {
            var canvas = ActiveZoomCanvas();
            if (canvas != null) canvas.ResetView();
        }

        // 重置按钮图标：逆时针回转箭头（↺），和常见“恢复视图”图标一致。
        private static void DrawResetIcon(Graphics g, Rectangle bounds)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var cx = bounds.Left + bounds.Width / 2f;
            var cy = bounds.Top + bounds.Height / 2f;
            const float radius = 6.5f;
            using (var pen = new Pen(Theme.Text, 1.4f))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                // 从左上缺口起逆时针画一整圈，缺口留在左上。
                g.DrawArc(pen, cx - radius, cy - radius, radius * 2, radius * 2, 200f, -290f);
            }
            // 箭头在弧线终点（正上方）：实心三角保证左右对称，指向左（逆时针）。
            var tip = new PointF(cx - 2.4f, cy - radius);
            var arrow = new[]
            {
                tip,
                new PointF(cx + 2f, cy - radius - 2.4f),
                new PointF(cx + 2f, cy - radius + 2.4f),
            };
            using (var brush = new SolidBrush(Theme.Text))
                g.FillPolygon(brush, arrow);
        }

        private void PlaceViewBar(Panel host)
        {
            if (host == null) return;
            var barHeight = Math.Max(_viewBar.Height, 28);
            _viewBar.Location = new Point(
                Math.Max(6, host.ClientSize.Width - _viewBar.Width - 8),
                Math.Max(6, host.ClientSize.Height - barHeight - 8));
            _viewBar.BringToFront();
        }

        private void ToggleLog()
        {
            _logExpanded = !_logExpanded;
            _root.RowStyles[6].Height = _logExpanded ? 92f : 0f;
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

        private const int SbHorz = 0;

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

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
            _prevButton.Enabled = _page > 1;
            _nextButton.Enabled = _page * Math.Max(10, _settings.SearchPageSize) < _total;
        }

        private Control BuildColumnHeader()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 26,
                ColumnCount = 5,
                BackColor = Theme.Surface,
                Margin = new Padding(0),
            };
            var widths = new[] { 82, 120, 78, 52, 52 };
            var names = new[] { "编号", "型号", "封装", "库存", "单价" };
            for (var i = 0; i < widths.Length; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, widths[i]));
                var label = new Label
                {
                    Text = names[i],
                    Dock = DockStyle.Fill,
                    // 数值列与数据行统一右对齐，便于竖排比较。
                    TextAlign = i >= 3 ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft,
                    ForeColor = Theme.TextMuted,
                    Font = Theme.LabelFont,
                    // 数值列右侧留白与数据行一致，保证小数点对齐。
                    Margin = i >= 3 ? new Padding(2, 0, 6, 0) : new Padding(2, 0, 2, 0),
                    AutoEllipsis = true,
                };
                _headerLabels[i] = label;
                table.Controls.Add(label, i, 0);
            }
            _headerTable = table;
            return table;
        }

        private int _lastLayoutWidth = -1;
        private int _lastVisibleColumns = -1;

        private void LayoutColumns()
        {
            if (_headerTable == null || _list.Columns.Count < 5) return;
            // 预留竖向滚动条宽度，避免出现横向滚动条。
            var clientWidth = _list.ClientSize.Width;
            var width = Math.Max(80, clientWidth - 20);

            // 窄宽度下先隐藏单价，再隐藏库存，避免“¥0…”“4…”这类无意义截断。
            var visible = width < 220 ? 3 : width < 300 ? 4 : 5;
            if (_lastLayoutWidth != width || _lastVisibleColumns != visible)
            {
                _lastLayoutWidth = width;
                _lastVisibleColumns = visible;
                PluginLog.Write("panel layout width=" + width + " visible=" + visible +
                    " listClient=" + clientWidth + " panel=" + Width + "x" + Height);
            }

            // 压缩列宽时给表头留足空间：“库存”在 34px 下会被截成“库…”。
            var minimum = new[] { 64, 52, 46, 40, 42 };
            var ratio = new[] { 0.20, 0.28, 0.22, 0.14, 0.16 };
            var widths = new int[5];
            var minSum = 0;
            var ratioSum = 0.0;
            for (var i = 0; i < visible; i++)
            {
                minSum += minimum[i];
                ratioSum += ratio[i];
            }

            var used = 0;
            if (width <= minSum)
            {
                for (var i = 0; i < visible; i++)
                {
                    widths[i] = Math.Max(20, (int)(minimum[i] * width / (double)minSum));
                    used += widths[i];
                }
            }
            else
            {
                var extra = width - minSum;
                for (var i = 0; i < visible; i++)
                {
                    widths[i] = minimum[i] + (int)Math.Floor(extra * ratio[i] / ratioSum);
                    used += widths[i];
                }
            }
            widths[visible - 1] += width - used;

            for (var i = 0; i < 5; i++)
            {
                var columnWidth = i < visible ? widths[i] : 0;
                _list.Columns[i].Width = columnWidth;
                if (_headerTable.ColumnStyles.Count > i)
                    _headerTable.ColumnStyles[i].Width = columnWidth;
                if (_headerLabels[i] != null) _headerLabels[i].Visible = i < visible;
            }

            HideHorizontalScroll();
        }

        // 列宽已经压到可视宽度内，横向滚动条没有任何用处，而且系统浅色条在深色面板里很突兀。
        private void HideHorizontalScroll()
        {
            if (!_list.IsHandleCreated) return;
            try { ShowScrollBar(_list.Handle, SbHorz, false); }
            catch
            {
                // 隐藏失败不影响使用。
            }
        }

        // 悬停只重绘发生变化的两行，整块 Invalidate 会在列表里闪一下。
        private void InvalidateRow(int index)
        {
            if (!_list.IsHandleCreated || index < 0 || index >= _list.Items.Count) return;
            var bounds = _list.Items[index].Bounds;
            if (bounds.Width > 0 && bounds.Height > 0) _list.Invalidate(bounds);
        }

        // 自绘滚动条覆盖在系统滚动条上：系统条保持原样（滚轮/键盘照常可用），
        // 只是被不透明的细条盖住，避免出现“一方一圆”两条并存。
        private void PlaceListScroll()
        {
            if (_listScroll.Parent == null) return;
            var host = _listScroll.Parent;
            _listScroll.SetBounds(Math.Max(0, host.ClientSize.Width - _listScroll.Width), 0,
                _listScroll.Width, host.ClientSize.Height);
            _listScroll.BringToFront();
        }

        private void PlaceLogScroll()
        {
            if (_logScroll.Parent == null || _log == null) return;
            _logScroll.SetBounds(Math.Max(0, _log.Right - _logScroll.Width), _log.Top,
                _logScroll.Width, _log.Height);
            _logScroll.BringToFront();
        }

        private void SyncScrollBars()
        {
            if (_list.IsHandleCreated && _list.Visible)
            {
                var count = _list.Items.Count;
                var perPage = (int)SendMessage(_list.Handle, LvmGetCountPerPage, IntPtr.Zero, IntPtr.Zero);
                var top = (int)SendMessage(_list.Handle, LvmGetTopIndex, IntPtr.Zero, IntPtr.Zero);
                _listScroll.Configure(0, count, perPage, top);
                // 列表内容变化后系统可能重新显示横向条，顺手再压一次。
                HideHorizontalScroll();
            }

            if (_log != null && _log.IsHandleCreated && _log.Visible)
            {
                var first = (int)SendMessage(_log.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero);
                var lineHeight = Math.Max(1, TextRenderer.MeasureText("Ag", _log.Font).Height);
                var view = Math.Max(1, _log.ClientSize.Height / lineHeight);
                var total = _log.GetLineFromCharIndex(_log.TextLength) + 1;
                _logScroll.Configure(0, total, view, first);
            }
        }

        private void ScrollListTo(int index)
        {
            if (!_list.IsHandleCreated) return;
            var count = _list.Items.Count;
            if (count == 0) return;
            index = Math.Min(Math.Max(index, 0), count - 1);
            try { _list.TopItem = _list.Items[index]; } catch { }
        }

        private void ScrollLogTo(int line)
        {
            if (!_log.IsHandleCreated) return;
            var first = (int)SendMessage(_log.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero);
            var delta = line - first;
            if (delta != 0) SendMessage(_log.Handle, EmLineScroll, IntPtr.Zero, new IntPtr(delta));
        }

        private static bool _drawLogged;

        // Details rows are drawn in one pass: background, cell text and separators,
        // so the row content cannot depend on DrawSubItem being raised.
        private void DrawRow(object sender, DrawListViewItemEventArgs e)
        {
            var back = e.Item.Selected
                ? Theme.Selection
                : e.ItemIndex == _hotIndex
                    ? Theme.ListHover
                    : (e.ItemIndex % 2 == 0 ? Theme.InputBack : Theme.ListAlt);
            using (var brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, e.Bounds);
            if (e.Item.Selected)
            {
                using (var bar = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(bar, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);
            }
            if (!_drawLogged)
            {
                _drawLogged = true;
                PluginLog.Write("list draw items=" + _list.Items.Count + " bounds=" + e.Bounds);
            }

            var x = e.Bounds.Left;
            for (var column = 0; column < _list.Columns.Count && column < e.Item.SubItems.Count; column++)
            {
                var width = _list.Columns[column].Width;
                if (width <= 0) continue; // 窄宽度下已隐藏的列
                var fore = e.Item.Selected
                    ? Theme.Text
                    : (column >= 3 ? Theme.TextMuted : Theme.Text);
                var cell = new Rectangle(x, e.Bounds.Top, width, e.Bounds.Height);
                var rect = cell;
                rect.Inflate(-6, 0);
                TextRenderer.DrawText(e.Graphics, e.Item.SubItems[column].Text, Theme.UiFont, rect, fore,
                    (column >= 3 ? TextFormatFlags.Right : TextFormatFlags.Left) |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (column < _list.Columns.Count - 1)
                {
                    using (var pen = new Pen(Theme.Divider))
                        e.Graphics.DrawLine(pen, x + width - 1, e.Bounds.Top + 3,
                            x + width - 1, e.Bounds.Bottom - 4);
                }
                x += width;
            }
        }

        private void DrawParameterHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var brush = new SolidBrush(Theme.Surface))
                e.Graphics.FillRectangle(brush, e.Bounds);
            var rect = e.Bounds;
            rect.Inflate(-6, 0);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.SmallFont, rect, Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1,
                    e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        private void DrawParameterRow(object sender, DrawListViewItemEventArgs e)
        {
            var back = e.ItemIndex % 2 == 0 ? Theme.InputBack : Theme.ListAlt;
            using (var brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, e.Bounds);
            var x = e.Bounds.Left;
            for (var column = 0; column < _paramList.Columns.Count && column < e.Item.SubItems.Count; column++)
            {
                var width = _paramList.Columns[column].Width;
                var rect = new Rectangle(x, e.Bounds.Top, width, e.Bounds.Height);
                rect.Inflate(-6, 0);
                TextRenderer.DrawText(e.Graphics, e.Item.SubItems[column].Text, Theme.UiFont, rect,
                    column == 0 ? Theme.TextMuted : Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                x += width;
            }
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
                LayoutColumns();

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
                var reason = EeErrors.Describe(ex);
                _info.Text = "搜索失败：" + reason;
                Log("搜索失败：" + reason);
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
            // 立创商品参数与 EasyEDA 器件数据并行抓取，参数页先拿到哪个都不影响预览。
            var attributesTask = LcscProductInfo.LoadParametersAsync(item.LcscId, CancellationToken.None);
            try
            {
                var details = await PartCache.LoadAsync(item.LcscId, _settings.ImportModel, CancellationToken.None);
                _details = details;
                _partTitle.Text = details.Part.Title;
                _partMeta.Text = details.Part.LcscId + " · " + details.Part.Footprint.Name +
                                 " · " + details.Part.Manufacturer;
                _symbolCanvas.SetSymbols(details.Part.SymbolParts);
                _footprintCanvas.Footprint = details.Part.Footprint;
                FillParameters(details.Part, await attributesTask);
                _modelHint.Text = details.Part.Footprint.Model == null
                    ? "该器件没有 3D 模型\n可在封装页查看 2D 图形"
                    : "3D 预览暂不可用\n导入后可在 AD 中查看模型";
            }
            catch (Exception ex)
            {
                _partMeta.Text = "器件数据加载失败：" + EeErrors.Describe(ex);
                // 符号/封装拿不到时，至少把立创商品参数留在参数页。
                FillParameters(null, await attributesTask);
                PluginLog.Write("panel details failed: " + ex);
            }
        }

        private void ClearDetails(string hint)
        {
            _details = null;
            _symbolCanvas.Symbol = null;
            _footprintCanvas.Footprint = null;
            FillParameters(null, null);
            _partMeta.Text = hint ?? "";
        }

        // 参数页内容：立创商品参数（属性/参数值）在前，其次是 EasyEDA 字段。
        // 这几个字段在标题栏或别处已有，参数页不再重复展示。
        private static readonly HashSet<string> HiddenParameterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Package", "EasyEDA UUID", "Supplier", "Supplier Part",
        };

        private void FillParameters(LcscPart part, List<KeyValuePair<string, string>> attributes)
        {
            _paramList.BeginUpdate();
            try
            {
                _paramList.Items.Clear();
                if (attributes != null)
                {
                    foreach (var pair in attributes)
                        AddParameterRow(pair.Key, pair.Value);
                }
                if (part != null)
                {
                    foreach (var pair in part.Parameters)
                    {
                        if (HiddenParameterKeys.Contains(pair.Key)) continue;
                        AddParameterRow(pair.Key, pair.Value);
                    }
                }
                if (_paramList.Items.Count == 0)
                    AddParameterRow("提示", part == null ? "选中器件后显示参数" : "该器件没有参数记录");
            }
            finally
            {
                _paramList.EndUpdate();
                LayoutParameterColumns();
            }
        }

        private void AddParameterRow(string name, string value)
        {
            var row = new ListViewItem(name) { ToolTipText = value };
            row.SubItems.Add(value);
            _paramList.Items.Add(row);
        }

        private void LayoutParameterColumns()
        {
            if (_paramList == null || _paramList.Columns.Count < 2) return;
            var width = _paramList.ClientSize.Width - 4;
            if (width <= 0) return;
            // 参数名里最长的是 “Manufacturer Part / EasyEDA UUID”，按其宽度定列宽即可，
            // 不在控件创建/绘制期间遍历 Items，避免句柄未就绪时取到空项。
            var name = 120;
            try
            {
                name = TextRenderer.MeasureText("Manufacturer Part", Theme.UiFont).Width + 24;
            }
            catch
            {
                // 量字失败时退回固定宽度，布局宁宽勿崩。
            }
            name = Math.Min(name, Math.Max(120, width * 46 / 100));
            _paramList.Columns[0].Width = name;
            _paramList.Columns[1].Width = Math.Max(120, width - name);
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
            FitTargetText();
            _targetTip.SetToolTip(_targetLabel, _settings.TargetSummary());
        }

        private string TargetFolder()
        {
            var folder = _settings.NewLibraryFolder;
            try
            {
                var sch = _settings.ResolveSchLib();
                if (!string.IsNullOrWhiteSpace(sch))
                {
                    var directory = Path.GetDirectoryName(sch);
                    if (!string.IsNullOrWhiteSpace(directory)) folder = directory;
                }
            }
            catch
            {
                // Fall back to the configured library folder.
            }
            return folder;
        }

        // 面板很窄，完整路径放不下时从中间省略，保证最末一级目录始终可见。
        private void FitTargetText()
        {
            var folder = TargetFolder();
            var available = _targetLabel.ClientSize.Width - 2;
            _targetLabel.Text = available > 24
                ? CompactMiddle(folder, Theme.SmallFont, available)
                : folder;
        }

        private static string CompactMiddle(string text, Font font, int maxWidth)
        {
            if (TextRenderer.MeasureText(text, font).Width <= maxWidth)
            {
                return text;
            }
            var parts = text.Split('\\');
            if (parts.Length < 2)
            {
                return text;
            }
            var head = parts[0];
            for (var keep = parts.Length - 1; keep >= 1; keep--)
            {
                var candidate = head + "\\…\\" + JoinTail(parts, keep);
                if (TextRenderer.MeasureText(candidate, font).Width <= maxWidth)
                {
                    return candidate;
                }
            }
            var tail = parts[parts.Length - 1];
            while (tail.Length > 1 && TextRenderer.MeasureText("…" + tail, font).Width > maxWidth)
            {
                tail = tail.Substring(1);
            }
            return "…" + tail;
        }

        private static string JoinTail(string[] parts, int keep)
        {
            var builder = new System.Text.StringBuilder();
            for (var i = parts.Length - keep; i < parts.Length; i++)
            {
                if (builder.Length > 0) builder.Append('\\');
                builder.Append(parts[i]);
            }
            return builder.ToString();
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
            _placeButton.Enabled = false;
            _progress.Visible = true;
            _progress.Value = 0;
            _cancelImportButton.Visible = true;
            _cancelImportButton.Enabled = true;
            _importCts = new CancellationTokenSource();
            var token = _importCts.Token;
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
                var fetchTotal = selected.Count;
                var fetchFailed = 0;
                for (var index = 0; index < fetchTotal; index++)
                {
                    if (token.IsCancellationRequested)
                    {
                        Log("已取消导入（未写入库）。");
                        return;
                    }

                    var item = selected[index];
                    Log("准备 " + item.LcscId + " …");
                    PartDetails details;
                    try
                    {
                        details = _details != null && _details.Part.LcscId == item.LcscId
                            ? _details
                            : await PartCache.LoadAsync(item.LcscId, _settings.ImportModel, token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // 单个器件抓取失败不影响整批：计失败数后继续。
                        fetchFailed++;
                        Log(item.LcscId + " 抓取失败：" + EeErrors.Describe(ex));
                        _progress.Value = (index + 1) * 40 / Math.Max(1, fetchTotal);
                        continue;
                    }

                    if (_settings.ImportModel && details.Part.Footprint.Model != null)
                    {
                        if (!await PartCache.EnsureStepAsync(details, token))
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
                    _progress.Value = (index + 1) * 40 / Math.Max(1, fetchTotal);
                }

                if (token.IsCancellationRequested)
                {
                    Log("已取消导入（未写入库）。");
                    return;
                }

                var outcome = await ImportService.ExecuteAsync(request, (done, total) =>
                {
                    _progress.Value = 40 + (total == 0 ? 0 : done * 60 / total);
                }, token);

                var text = ImportLog.Text;
                if (!string.IsNullOrWhiteSpace(text)) Log(text.TrimEnd());
                if (outcome.Cancelled)
                    Log(string.Format("已取消：已导入 {0} 个，失败 {1} 个。", outcome.Imported, outcome.Failed + fetchFailed));
                else if (fetchFailed > 0)
                    Log(string.Format("导入完成：成功 {0} 个，失败 {1} 个（其中抓取失败 {2} 个）。",
                        outcome.Imported, outcome.Failed + fetchFailed, fetchFailed));
                else
                    Log(string.Format("导入完成：成功 {0} 个，失败 {1} 个。", outcome.Imported, outcome.Failed));

                if (!outcome.Cancelled && _settings.PlaceAfterImport && request.Parts.Count == 1)
                {
                    var part = request.Parts[0].Part;
                    var reference = string.IsNullOrWhiteSpace(part.Symbol.Name) ? part.LcscId : part.Symbol.Name;
                    if (AdApi.PlaceFromLibrary(reference, _settings.ResolveSchLib()))
                        Log("已进入放置状态，请在原理图中点击放置：" + reference);
                    else
                        Log("自动放置失败，请手动点“放置到原理图”。");
                }
            }
            catch (OperationCanceledException)
            {
                Log("已取消导入。");
            }
            catch (Exception ex)
            {
                Log("导入失败：" + EeErrors.Describe(ex));
                PluginLog.Write("panel import failed: " + ex);
            }
            finally
            {
                _importButton.Enabled = true;
                _placeButton.Enabled = true;
                _progress.Visible = false;
                _cancelImportButton.Visible = false;
                if (_importCts != null)
                {
                    _importCts.Dispose();
                    _importCts = null;
                }
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
                        PluginLog.Write("process fallback: interactive move");
                        if (AdApi.PlaceInteractive(reference, schLib))
                            Log("已进入放置状态，请在原理图中移动并点击放置：" + reference);
                        else if (AdApi.PlaceWithLocationPicker(reference, schLib))
                            Log("请在图纸上点击放置位置：" + reference);
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
            _log.AppendText(line + Environment.NewLine);
        }
    }
}
