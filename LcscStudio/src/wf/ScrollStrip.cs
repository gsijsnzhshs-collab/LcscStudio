using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LcscStudio
{
    // 细长深色滚动条：隐藏系统滚动条后由它显示位置、拖动和翻页。
    internal sealed class ScrollStrip : Control
    {
        private int _min;
        private int _max;
        private int _view;
        private int _value;
        private bool _dragging;
        private bool _hover;
        private int _dragOffset;

        public event EventHandler ValueChanged;

        public ScrollStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.InputBack;
            Width = 10;
            Visible = false;
        }

        public int Value => _value;

        // 由宿主定时刷新：范围、可见量、当前位置；不需要滚动时自动隐藏。
        public void Configure(int min, int max, int view, int value)
        {
            view = Math.Max(1, view);
            var need = max - min > view;
            if (need != Visible) Visible = need;
            if (!need) return;

            value = Math.Min(Math.Max(value, min), Math.Max(min, max - view));
            if (min == _min && max == _max && view == _view && value == _value) return;
            _min = min;
            _max = max;
            _view = view;
            _value = value;
            Invalidate();
        }

        private float TrackHeight => Math.Max(1f, ClientSize.Height - 4f);

        private float ThumbHeight()
        {
            var span = Math.Max(1, _max - _min);
            var page = Math.Min(_view, span);
            return Math.Min(TrackHeight, Math.Max(18f, TrackHeight * page / span));
        }

        private float ThumbTop()
        {
            var span = Math.Max(1, _max - _min);
            var page = Math.Min(_view, span);
            var range = span - page;
            if (range <= 0) return 2f;
            return 2f + (TrackHeight - ThumbHeight()) * (_value - _min) / range;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.InputBack);
            if (_max - _min <= _view) return;

            // 悬停/拖动时略微加宽，形成“放大 + 圆角”的层次感。
            var thumbWidth = _hover || _dragging ? 10f : 8f;
            var rect = new RectangleF(ClientSize.Width - thumbWidth - 1, ThumbTop(),
                thumbWidth, ThumbHeight());
            var color = _dragging ? Theme.Accent
                : _hover ? Color.FromArgb(0x6A, 0x6C, 0x70)
                : Color.FromArgb(0x4A, 0x4B, 0x4F);
            using (var path = Rounded(rect, rect.Width / 2))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        private static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, r * 2, r * 2, 180, 90);
            path.AddArc(rect.Right - r * 2, rect.Top, r * 2, r * 2, 270, 90);
            path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            var top = ThumbTop();
            var height = ThumbHeight();
            if (e.Y >= top && e.Y <= top + height)
            {
                _dragging = true;
                _dragOffset = (int)(e.Y - top);
                Capture = true;
            }
            else
            {
                SetFromPoint(e.Y - (int)(height / 2));
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) SetFromPoint(e.Y - _dragOffset);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !_dragging) return;
            _dragging = false;
            Capture = false;
            Invalidate();
        }

        private void SetFromPoint(int y)
        {
            var travel = TrackHeight - ThumbHeight();
            var span = Math.Max(1, _max - _min);
            var range = Math.Max(0, span - _view);
            if (travel <= 0 || range <= 0) return;

            var value = _min + (int)Math.Round((y - 2f) / travel * range);
            value = Math.Min(Math.Max(value, _min), _min + range);
            if (value == _value) return;
            _value = value;
            Invalidate();
            var handler = ValueChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
