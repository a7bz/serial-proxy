using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SerialProxy.Ui
{
    /// <summary>统一的颜色、字体、圆角与按钮风格。</summary>
    internal static class UiTheme
    {
        public static readonly Color Background = Color.FromArgb(243, 245, 249);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(227, 231, 238);
        public static readonly Color Text = Color.FromArgb(31, 35, 41);
        public static readonly Color Muted = Color.FromArgb(107, 114, 128);
        public static readonly Color Faint = Color.FromArgb(156, 163, 175);
        public static readonly Color Primary = Color.FromArgb(37, 99, 235);
        public static readonly Color PrimaryDark = Color.FromArgb(29, 78, 216);
        public static readonly Color PrimaryPressed = Color.FromArgb(30, 64, 175);
        public static readonly Color Hover = Color.FromArgb(241, 245, 249);
        public static readonly Color Rx = Color.FromArgb(29, 78, 216);
        public static readonly Color Tx = Color.FromArgb(5, 150, 105);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color Warning = Color.FromArgb(217, 119, 6);
        public static readonly Color InfoBackground = Color.FromArgb(238, 244, 255);
        public static readonly Color StatusBackground = Color.FromArgb(249, 250, 251);

        public static readonly Font Title = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
        public static readonly Font Subtitle = new Font("Microsoft YaHei UI", 8.5F);
        public static readonly Font Ui = new Font("Microsoft YaHei UI", 9F);
        public static readonly Font UiBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        public static readonly Font Small = new Font("Microsoft YaHei UI", 8.5F);
        public static readonly Font Mono = new Font("Consolas", 9.5F);
        public static readonly Font MonoLarge = new Font("Consolas", 10F);

        public static GraphicsPath RoundedPath(Rectangle rect, int radius)
        {
            return RoundedPath(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), radius);
        }

        public static GraphicsPath RoundedPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();

            if (radius <= 0 || rect.Width <= 1 || rect.Height <= 1)
            {
                path.AddRectangle(rect);
                return path;
            }

            float d = Math.Min(radius * 2F, Math.Min(rect.Width, rect.Height));

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));

            return Color.FromArgb(
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }

        public static void RoundRegion(Control control, int radius)
        {
            if (control.Width <= 0 || control.Height <= 0) return;

            // 半径按 DPI 缩放，否则高缩放比下 Region 会比绘制出的圆角小，边缘又会被切出锯齿
            int scaledRadius = Math.Max(0, (int)Math.Round(radius * (control.DeviceDpi / 96F)));

            using (var path = RoundedPath(new Rectangle(0, 0, control.Width, control.Height), scaledRadius))
            {
                var old = control.Region;
                control.Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        public static Button CreatePrimaryButton(string text, int width)
        {
            var button = new RoundedButton
            {
                Text = text,
                Width = width,
                Height = 30,
                BackColor = Primary,
                ForeColor = Color.White,
                Font = Ui,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0)
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = PrimaryDark;
            button.FlatAppearance.MouseDownBackColor = PrimaryPressed;

            return button;
        }

        public static Button CreateSecondaryButton(string text, int width)
        {
            var button = new RoundedButton
            {
                Text = text,
                Width = width,
                Height = 30,
                BackColor = Surface,
                ForeColor = Text,
                Font = Ui,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0)
            };

            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Hover;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(226, 232, 240);

            return button;
        }

        public static Button CreateDangerButton(string text, int width)
        {
            var button = CreateSecondaryButton(text, width);
            button.ForeColor = Danger;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(254, 242, 242);
            return button;
        }

        /// <summary>主色按钮的可用/禁用外观（WinForms 禁用时不会自动换底色）。</summary>
        public static void SetPrimaryEnabled(Button button, bool enabled)
        {
            if (button == null) return;

            button.Enabled = enabled;
            button.BackColor = enabled ? Primary : Color.FromArgb(203, 213, 225);
            button.ForeColor = enabled ? Color.White : Color.FromArgb(100, 116, 139);
            button.FlatAppearance.MouseOverBackColor = enabled
                ? PrimaryDark
                : Color.FromArgb(203, 213, 225);
            button.Cursor = enabled ? Cursors.Hand : Cursors.Default;
        }

        public static Label CreateLabel(string text, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = true
            };
        }

        public static Panel CreateSeparator()
        {
            return new Panel
            {
                Width = 1,
                Height = 22,
                BackColor = Border,
                Margin = new Padding(6, 4, 12, 0)
            };
        }

        /// <summary>给卡片标题栏加一条很淡的分隔线，让标题与内容层次更清楚。</summary>
        public static void AddBottomBorder(Control control)
        {
            if (control == null) return;

            control.Paint += (s, e) =>
            {
                using (var pen = new Pen(Border))
                    e.Graphics.DrawLine(pen, 0, control.Height - 1, control.Width, control.Height - 1);
            };
        }

        private const int EmSetCueBanner = 0x1501;

        [StructLayout(LayoutKind.Sequential)]
        private struct ComboBoxInfo
        {
            public int cbSize;
            public Rectangle rcItem;
            public Rectangle rcButton;
            public int stateButton;
            public IntPtr hwndCombo;
            public IntPtr hwndItem;
            public IntPtr hwndList;
        }

        [DllImport("user32.dll")]
        private static extern bool GetComboBoxInfo(IntPtr hWnd, ref ComboBoxInfo info);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public static void SetCueBanner(Control control, string text)
        {
            if (control == null) return;

            Action apply = () =>
            {
                if (control.IsDisposed || !control.IsHandleCreated) return;
                SendMessage(control.Handle, EmSetCueBanner, (IntPtr)1, text);
            };

            if (control.IsHandleCreated)
                apply();
            else
                control.HandleCreated += (s, e) => apply();
        }

        public static void SetComboCue(ComboBox combo, string text)
        {
            if (combo == null) return;

            Action apply = () =>
            {
                if (combo.IsDisposed || !combo.IsHandleCreated) return;

                var info = new ComboBoxInfo();
                info.cbSize = Marshal.SizeOf(typeof(ComboBoxInfo));

                if (GetComboBoxInfo(combo.Handle, ref info) && info.hwndItem != IntPtr.Zero)
                    SendMessage(info.hwndItem, EmSetCueBanner, (IntPtr)1, text);
            };

            if (combo.IsHandleCreated)
                apply();
            else
                combo.HandleCreated += (s, e) => apply();
        }
    }

    /// <summary>
    /// 自绘圆角按钮。
    /// 旧实现用 <see cref="Control.Region"/> 裁切圆角，Region 只有 1 位掩码、不做抗锯齿，
    /// 圆角处是硬边阶梯，看起来发虚发毛。这里改为 GDI+ 抗锯齿绘制，边缘才干净。
    /// </summary>
    internal sealed class RoundedButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
        }

        /// <summary>圆角半径，按 96 DPI 的逻辑像素给出，绘制时随 DPI 缩放。</summary>
        public int Radius { get; set; } = 8;

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float scale = DeviceDpi / 96F;
            float radius = Math.Max(1F, Radius * scale);

            // 圆角外露出的四角要用父容器底色，先铺满整块
            using (var corners = new SolidBrush(CornerColor()))
                g.FillRectangle(corners, ClientRectangle);

            var shape = new RectangleF(0F, 0F, Width, Height);
            using (var path = UiTheme.RoundedPath(shape, radius))
            using (var brush = new SolidBrush(FillColor()))
                g.FillPath(brush, path);

            float borderWidth = FlatAppearance.BorderSize > 0
                ? Math.Max(1F, FlatAppearance.BorderSize * scale)
                : 0F;

            if (borderWidth > 0F)
            {
                // 内缩半个线宽，让 1px 描边正好落在像素上，不发虚
                var stroke = new RectangleF(borderWidth / 2F, borderWidth / 2F,
                    Width - borderWidth, Height - borderWidth);

                using (var path = UiTheme.RoundedPath(stroke, Math.Max(1F, radius - borderWidth / 2F)))
                using (var pen = new Pen(BorderColor(), borderWidth) { Alignment = PenAlignment.Center })
                    g.DrawPath(pen, path);
            }

            int inset = (int)Math.Ceiling(6F * scale);
            var textRect = new Rectangle(inset, 0, Math.Max(1, Width - inset * 2), Height);

            var flags = TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine |
                        TextFormatFlags.EndEllipsis;
            if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;

            TextRenderer.DrawText(g, Text, Font, textRect, TextColor(), flags);

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g,
                    Rectangle.Inflate(ClientRectangle, -3 * (int)Math.Ceiling(scale), -3 * (int)Math.Ceiling(scale)));
        }

        private Color FillColor()
        {
            Color fill = BackColor;
            if (fill.A < 255) fill = UiTheme.Surface;
            if (!Enabled) return fill;

            var flat = FlatAppearance;
            if (_pressed && flat.MouseDownBackColor != Color.Empty) return flat.MouseDownBackColor;
            if (_hover && flat.MouseOverBackColor != Color.Empty) return flat.MouseOverBackColor;
            return fill;
        }

        private Color BorderColor()
        {
            Color color = FlatAppearance.BorderColor;
            if (color == Color.Empty) color = UiTheme.Border;

            return Enabled ? color : UiTheme.Blend(color, FillColor(), 0.55F);
        }

        private Color TextColor()
        {
            Color color = ForeColor;
            if (color == Color.Empty) color = UiTheme.Text;

            return Enabled ? color : UiTheme.Blend(color, FillColor(), 0.5F);
        }

        /// <summary>逐级向上找不透明的父容器底色，用于填充四个圆角。</summary>
        private Color CornerColor()
        {
            for (Control parent = Parent; parent != null; parent = parent.Parent)
            {
                Color color = parent.BackColor;
                if (color.A == 255) return color;
            }

            return UiTheme.Background;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);

            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);

            if (_pressed)
            {
                _pressed = false;
                Invalidate();
            }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);

            if (_pressed && !Capture)
            {
                _pressed = false;
                Invalidate();
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);

            if (!Enabled)
            {
                _hover = false;
                _pressed = false;
            }

            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }
    }

    /// <summary>白色卡片：1px 边框 + 圆角。</summary>
    internal sealed class CardPanel : Panel
    {
        public int Radius = 10;
        public Color BorderColor = UiTheme.Border;

        public CardPanel()
        {
            BackColor = UiTheme.Surface;
            DoubleBuffered = true;
            Padding = new Padding(1);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UiTheme.RoundRegion(this, Radius);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (Width <= 1 || Height <= 1) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float scale = DeviceDpi / 96F;
            float radius = Math.Max(1F, Radius * scale);
            float thickness = Math.Max(1F, scale);

            // 先按整个圆角区域铺边框色，再用内缩 1px 的圆角矩形盖回底色。
            // 直接用 1px 画笔描边的话，外侧半个像素会被 Region 裁掉，边框会显得发虚。
            using (var path = UiTheme.RoundedPath(new RectangleF(0F, 0F, Width, Height), radius))
            using (var brush = new SolidBrush(BorderColor))
                g.FillPath(brush, path);

            using (var path = UiTheme.RoundedPath(
                       new RectangleF(thickness, thickness,
                           Width - thickness * 2F, Height - thickness * 2F),
                       Math.Max(1F, radius - thickness)))
            using (var brush = new SolidBrush(BackColor))
                g.FillPath(brush, path);
        }
    }

    /// <summary>顶部/状态栏的状态胶囊。</summary>
    internal sealed class StatusPill : Control
    {
        private string _caption = "未运行";
        private Color _dotColor = UiTheme.Faint;

        public StatusPill()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            DoubleBuffered = true;
            Font = UiTheme.Ui;
            Size = new Size(130, 28);
            BackColor = Color.Transparent;
        }

        public void SetState(string caption, Color dotColor)
        {
            _caption = caption ?? "";
            _dotColor = dotColor;

            int textWidth = TextRenderer.MeasureText(_caption, Font).Width;
            int width = Math.Max(88, 12 + 8 + 8 + textWidth + 14);
            if (Width != width) Width = width;

            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UiTheme.RoundRegion(this, Height / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UiTheme.RoundedPath(rect, Height / 2))
            using (var brush = new SolidBrush(UiTheme.StatusBackground))
            using (var pen = new Pen(UiTheme.Border))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            int dotSize = 8;
            int dotX = 12;
            int dotY = (Height - dotSize) / 2;

            using (var brush = new SolidBrush(_dotColor))
                e.Graphics.FillEllipse(brush, dotX, dotY, dotSize, dotSize);

            var textRect = new Rectangle(dotX + dotSize + 8, 0, Width - dotX - dotSize - 14, Height);
            TextRenderer.DrawText(e.Graphics, _caption, Font, textRect, UiTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>分段选择器：选中项为实心主色，整体共享一个圆角边框。</summary>
    internal sealed class SegmentedControl : Control
    {
        private readonly string[] _items;
        private readonly int _itemWidth;
        private int _selectedIndex;
        private int _hoverIndex = -1;

        public event EventHandler SelectedIndexChanged;

        public SegmentedControl(int itemWidth, params string[] items)
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);

            _itemWidth = Math.Max(28, itemWidth);
            _items = items ?? new string[0];

            Height = 30;
            Width = _itemWidth * Math.Max(1, _items.Length);
            Font = UiTheme.Ui;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                int index = Math.Max(0, Math.Min(_items.Length - 1, value));
                if (index == _selectedIndex) return;

                _selectedIndex = index;
                Invalidate();
                var handler = SelectedIndexChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public string SelectedText
        {
            get
            {
                return _items.Length == 0 || _selectedIndex < 0 || _selectedIndex >= _items.Length
                    ? ""
                    : _items[_selectedIndex];
            }
        }

        public int IndexOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;

            for (int i = 0; i < _items.Length; i++)
            {
                if (string.Equals(_items[i], text, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left)
            {
                SelectedIndex--;
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Right)
            {
                SelectedIndex++;
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int index = e.X / _itemWidth;
            if (index < 0 || index >= _items.Length) index = -1;
            if (index == _hoverIndex) return;

            _hoverIndex = index;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIndex < 0) return;

            _hoverIndex = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            Focus();
            int index = e.X / _itemWidth;
            if (index >= 0 && index < _items.Length)
                SelectedIndex = index;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var outer = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UiTheme.RoundedPath(outer, 8))
            {
                using (var brush = new SolidBrush(UiTheme.Surface))
                    e.Graphics.FillPath(brush, path);

                var state = e.Graphics.Save();
                e.Graphics.SetClip(path);

                if (_hoverIndex >= 0 && _hoverIndex != _selectedIndex)
                {
                    using (var brush = new SolidBrush(UiTheme.Hover))
                        e.Graphics.FillRectangle(brush, _hoverIndex * _itemWidth, 0, _itemWidth, Height);
                }

                using (var pen = new Pen(UiTheme.Border))
                {
                    for (int i = 1; i < _items.Length; i++)
                    {
                        int x = i * _itemWidth;
                        e.Graphics.DrawLine(pen, x, 6, x, Height - 7);
                    }
                }

                if (_selectedIndex >= 0 && _selectedIndex < _items.Length)
                {
                    // 选中态用淡蓝底 + 主色文字，避免与主操作按钮争夺注意力
                    var selected = new Rectangle(_selectedIndex * _itemWidth + 2, 2,
                        _itemWidth - 4, Height - 4);
                    using (var selectedPath = UiTheme.RoundedPath(selected, 6))
                    using (var brush = new SolidBrush(Color.FromArgb(219, 234, 254)))
                    using (var pen = new Pen(Color.FromArgb(191, 219, 254)))
                    {
                        e.Graphics.FillPath(brush, selectedPath);
                        e.Graphics.DrawPath(pen, selectedPath);
                    }
                }

                e.Graphics.Restore(state);
            }

            for (int i = 0; i < _items.Length; i++)
            {
                var rect = new Rectangle(i * _itemWidth, 0, _itemWidth, Height);
                bool active = i == _selectedIndex;

                TextRenderer.DrawText(e.Graphics, _items[i],
                    active ? UiTheme.UiBold : UiTheme.Ui,
                    rect,
                    active ? UiTheme.PrimaryDark : UiTheme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            using (var borderPath = UiTheme.RoundedPath(outer, 8))
            using (var pen = new Pen(Focused ? UiTheme.Primary : UiTheme.Border))
                e.Graphics.DrawPath(pen, borderPath);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }
    }

    /// <summary>圆角输入框：置入无边框 TextBox，可带右侧按钮（清空 / 历史）。</summary>
    internal sealed class InputHost : Control
    {
        private static readonly Font GlyphFont = new Font("Segoe UI", 9F);

        private readonly TextBox _box;
        private string _trailingText = "";
        private bool _focused;
        private bool _trailingHover;

        public event EventHandler TrailingClicked;

        public InputHost(TextBox box)
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);

            if (box == null) throw new ArgumentNullException("box");

            _box = box;
            _box.BorderStyle = BorderStyle.None;
            _box.BackColor = UiTheme.Surface;

            Controls.Add(_box);

            Height = 30;
            BackColor = UiTheme.Surface;
            Cursor = Cursors.IBeam;

            _box.GotFocus += (s, e) => SetFocused(true);
            _box.LostFocus += (s, e) => SetFocused(false);
        }

        public TextBox Box
        {
            get { return _box; }
        }

        public string TrailingText
        {
            get { return _trailingText; }
            set
            {
                string text = value ?? "";
                if (_trailingText == text) return;

                _trailingText = text;
                PerformLayout();
                Invalidate();
            }
        }

        public int TrailingWidth { get; set; }

        private int EffectiveTrailingWidth
        {
            get { return string.IsNullOrEmpty(_trailingText) ? 0 : (TrailingWidth > 0 ? TrailingWidth : 28); }
        }

        private Rectangle TrailingRect
        {
            get
            {
                int width = EffectiveTrailingWidth;
                return width <= 0
                    ? Rectangle.Empty
                    : new Rectangle(Width - width, 0, width, Height);
            }
        }

        private void SetFocused(bool focused)
        {
            if (_focused == focused) return;

            _focused = focused;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);

            int trailing = EffectiveTrailingWidth;
            int boxHeight = Math.Max(16, _box.PreferredHeight);
            _box.SetBounds(10, (Height - boxHeight) / 2,
                Math.Max(20, Width - 20 - trailing), boxHeight);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PerformLayout();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            bool hover = TrailingRect.Contains(e.Location);
            Cursor = hover ? Cursors.Hand : Cursors.IBeam;

            if (hover == _trailingHover) return;
            _trailingHover = hover;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Cursor = Cursors.IBeam;

            if (!_trailingHover) return;
            _trailingHover = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Left && TrailingRect.Contains(e.Location))
            {
                var handler = TrailingClicked;
                if (handler != null) handler(this, EventArgs.Empty);
                return;
            }

            _box.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UiTheme.RoundedPath(rect, 8))
            {
                using (var brush = new SolidBrush(UiTheme.Surface))
                    e.Graphics.FillPath(brush, path);

                using (var pen = new Pen(_focused ? Color.FromArgb(147, 197, 253) : UiTheme.Border,
                           _focused ? 1.4F : 1F))
                    e.Graphics.DrawPath(pen, path);
            }

            int trailing = EffectiveTrailingWidth;
            if (trailing <= 0) return;

            int lineX = Width - trailing;
            using (var pen = new Pen(UiTheme.Border))
                e.Graphics.DrawLine(pen, lineX, 7, lineX, Height - 8);

            var glyphRect = new Rectangle(lineX, 0, trailing, Height);
            TextRenderer.DrawText(e.Graphics, _trailingText, GlyphFont, glyphRect,
                _trailingHover ? UiTheme.Text : UiTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>轻量开关芯片，替代原生小方框 CheckBox。</summary>
    internal sealed class ToggleChip : CheckBox
    {
        public ToggleChip(string text, int width)
        {
            Text = text;
            Width = width;
            Height = 26;
            Appearance = Appearance.Button;
            FlatStyle = FlatStyle.Flat;
            TextAlign = ContentAlignment.MiddleCenter;
            Font = UiTheme.Small;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            Margin = new Padding(0, 2, 8, 0);

            CheckedChanged += (s, e) => ApplyState();
            ApplyState();
        }

        private void ApplyState()
        {
            BackColor = Checked ? Color.FromArgb(219, 234, 254) : UiTheme.Surface;
            ForeColor = Checked ? UiTheme.PrimaryDark : UiTheme.Muted;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.BorderColor = Checked
                ? Color.FromArgb(191, 219, 254)
                : UiTheme.Border;
            FlatAppearance.CheckedBackColor = Color.FromArgb(219, 234, 254);
            FlatAppearance.MouseOverBackColor = Checked
                ? Color.FromArgb(203, 221, 254)
                : UiTheme.Hover;
            FlatAppearance.MouseDownBackColor = Checked
                ? Color.FromArgb(191, 219, 254)
                : Color.FromArgb(226, 232, 240);
        }
    }
}
