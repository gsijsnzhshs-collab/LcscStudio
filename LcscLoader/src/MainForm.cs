using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DXP;
using EDP;
using Point = System.Drawing.Point;

namespace LcscLoader
{
    internal sealed class MainForm : Form
    {
        private sealed class PlanItem
        {
            public string Id = "";
            public LcscPart Part;
            public Model3DData Model;
            public string Error = "";
        }

        private readonly TextBox _ids;
        private readonly CheckBox _symbol;
        private readonly CheckBox _footprint;
        private readonly CheckBox _model;
        private readonly CheckBox _place;
        private readonly CheckBox _overwrite;
        private readonly Button _import;
        private readonly Button _cancel;
        private readonly ProgressBar _progress;
        private readonly Label _status;
        private readonly TextBox _log;

        private CancellationTokenSource _cts;
        private bool _busy;

        public MainForm()
        {
            Text = "立创商城元件导入";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ClientSize = new Size(660, 560);
            MinimumSize = new Size(560, 460);
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(new Label
            {
                Text = "立创商城编号（多个用空格、逗号或分号分隔）：",
                Location = new Point(12, 12),
                AutoSize = true,
            });

            _ids = new TextBox
            {
                Location = new Point(12, 36),
                Size = new Size(636, 56),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            Controls.Add(_ids);

            var group = new GroupBox
            {
                Text = "导入内容",
                Location = new Point(12, 100),
                Size = new Size(636, 78),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            Controls.Add(group);

            _symbol = new CheckBox
            {
                Text = "原理图符号",
                Checked = true,
                Location = new Point(12, 24),
                AutoSize = true,
            };
            _footprint = new CheckBox
            {
                Text = "PCB 封装",
                Checked = true,
                Location = new Point(150, 24),
                AutoSize = true,
            };
            _model = new CheckBox
            {
                Text = "3D 模型",
                Checked = true,
                Location = new Point(260, 24),
                AutoSize = true,
            };
            _overwrite = new CheckBox
            {
                Text = "覆盖库中已存在的元件",
                Location = new Point(12, 50),
                AutoSize = true,
            };
            _place = new CheckBox
            {
                Text = "导入后放置到当前原理图",
                Location = new Point(260, 50),
                AutoSize = true,
            };
            group.Controls.Add(_symbol);
            group.Controls.Add(_footprint);
            group.Controls.Add(_model);
            group.Controls.Add(_overwrite);
            group.Controls.Add(_place);

            _footprint.CheckedChanged += (s, e) =>
            {
                if (!_footprint.Checked) _model.Checked = false;
                _model.Enabled = _footprint.Checked;
            };
            _model.CheckedChanged += (s, e) =>
            {
                if (_model.Checked) _footprint.Checked = true;
            };
            _symbol.CheckedChanged += (s, e) =>
            {
                if (!_symbol.Checked) _place.Checked = false;
                _place.Enabled = _symbol.Checked;
            };

            _import = new Button
            {
                Text = "开始导入",
                Location = new Point(12, 188),
                Size = new Size(120, 32),
            };
            _import.Click += OnImport;
            Controls.Add(_import);

            _cancel = new Button
            {
                Text = "关闭",
                Location = new Point(144, 188),
                Size = new Size(90, 32),
            };
            _cancel.Click += OnCancel;
            Controls.Add(_cancel);

            _status = new Label
            {
                Location = new Point(248, 196),
                AutoSize = true,
                Text = "",
            };
            Controls.Add(_status);

            _progress = new ProgressBar
            {
                Location = new Point(12, 230),
                Size = new Size(636, 16),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            Controls.Add(_progress);

            _log = new TextBox
            {
                Location = new Point(12, 256),
                Size = new Size(636, 292),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = SystemColors.Window,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };
            Controls.Add(_log);

            var paths = new Label
            {
                Text = "库文件目录：" + AdApi.LibraryDir,
                Location = new Point(12, ClientSize.Height - 20),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            };
            Controls.Add(paths);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                _cts?.Cancel();
                _status.Text = "正在取消…";
                return;
            }
            base.OnFormClosing(e);
        }

        private async void OnImport(object sender, EventArgs e)
        {
            if (_busy) return;

            var ids = ParseIds(_ids.Text);
            if (ids.Count == 0)
            {
                MessageBox.Show(this, "请输入立创商城编号，例如 C2040。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!_symbol.Checked && !_footprint.Checked)
            {
                MessageBox.Show(this, "请至少勾选“原理图符号”或“PCB 封装”。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _busy = true;
            _import.Enabled = false;
            _cancel.Text = "取消";
            _log.Text = "";
            ImportLog.Clear();
            _cts = new CancellationTokenSource();

            try
            {
                var withModel = _model.Checked && _footprint.Checked;
                var plan = await BuildPlanAsync(ids, withModel, _cts.Token);

                if (_cts.IsCancellationRequested) return;

                ApplyPlan(plan);
            }
            catch (OperationCanceledException)
            {
                ImportLog.Add("导入已取消。");
            }
            catch (Exception ex)
            {
                ImportLog.Add("导入失败：" + ex.Message);
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
            finally
            {
                if (!IsDisposed)
                {
                    _log.Text = ImportLog.Text;
                    _import.Enabled = true;
                    _cancel.Text = "关闭";
                    _progress.Style = ProgressBarStyle.Blocks;
                    _status.Text = "";
                }
                _cts?.Dispose();
                _cts = null;
                _busy = false;
            }
        }

        private void OnCancel(object sender, EventArgs e)
        {
            if (_busy)
            {
                _cts?.Cancel();
                _status.Text = "正在取消…";
                return;
            }
            Close();
        }

        private async Task<List<PlanItem>> BuildPlanAsync(
            List<string> ids, bool withModel, CancellationToken cancellationToken)
        {
            var plan = new List<PlanItem>();
            var index = 0;
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;
                PostStatus(string.Format("正在获取 {0}（{1}/{2}）…", id, index, ids.Count));

                var item = new PlanItem { Id = id };
                try
                {
                    var part = await EasyEdaApi.GetPartAsync(id, cancellationToken).ConfigureAwait(false);
                    item.Part = part;
                    ImportLog.Add(string.Format("已获取 {0}：{1}", id, part.Title));

                    if (withModel && part.Footprint.Model != null)
                    {
                        PostStatus(string.Format("正在下载 3D 模型 {0}…", id));
                        item.Model = await Model3DImporter.PrepareAsync(part.Footprint.Model, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    item.Error = ex.Message;
                    ImportLog.Add(id + " 获取失败：" + ex.Message);
                }

                plan.Add(item);
            }
            return plan;
        }

        private void ApplyPlan(List<PlanItem> plan)
        {
            var request = new ImportRequest
            {
                Symbol = _symbol.Checked,
                Footprint = _footprint.Checked,
                Overwrite = _overwrite.Checked,
                Place = _place.Checked,
            };
            foreach (var item in plan)
            {
                if (item.Part != null)
                    request.Parts.Add(new ImportPart { Part = item.Part, Model = item.Model });
            }

            _progress.Maximum = Math.Max(1, plan.Count);
            _progress.Value = 0;

            Cursor = Cursors.WaitCursor;
            ImportOutcome outcome;
            try
            {
                outcome = ImportService.Execute(request, (done, total) =>
                {
                    _progress.Value = Math.Min(_progress.Maximum, Math.Max(_progress.Value, done));
                    _log.Text = ImportLog.Text;
                });
            }
            finally
            {
                Cursor = Cursors.Default;
                _log.Text = ImportLog.Text;
            }

            var failed = outcome.Failed + plan.Count(p => p.Part == null);
            if (failed > 0)
            {
                MessageBox.Show(this,
                    string.Format("导入完成：成功 {0} 个，失败 {1} 个。\r\n详情见下方日志。",
                        outcome.Imported, failed),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void PostStatus(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(PostStatus), text); }
                catch (InvalidOperationException) { }
                return;
            }
            _status.Text = text;
            _status.Refresh();
        }

        private static List<string> ParseIds(string text)
        {
            return (text ?? "")
                .Split(new[] { ' ', '\t', '\r', '\n', ',', '，', ';', '；' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim().ToUpperInvariant())
                .Where(value => value.Length > 0)
                .Distinct()
                .ToList();
        }
    }
}
