using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RealtimeTranslator.UI;

internal sealed class RegionSelectorForm : Form
{
    private Point _start;
    private Point _current;
    private bool _selecting;
    private bool _completed;

    public Rectangle SelectedRegion { get; private set; }

    public RegionSelectorForm(Rectangle monitorBounds)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = monitorBounds;
        Opacity = 0.28;
        BackColor = Color.FromArgb(18, 28, 46);
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        Paint += OnPaint;
        Deactivate += (_, _) =>
        {
            if (!_completed)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _selecting = true;
            _start = e.Location;
            _current = e.Location;
            Invalidate();
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_selecting)
        {
            return;
        }

        _current = new Point(
            Math.Clamp(e.X, 0, ClientSize.Width - 1),
            Math.Clamp(e.Y, 0, ClientSize.Height - 1));
        Invalidate();
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_selecting || e.Button != MouseButtons.Left)
        {
            return;
        }

        _selecting = false;
        _completed = true;
        var rect = Rectangle.FromLTRB(
            Math.Min(_start.X, _current.X),
            Math.Min(_start.Y, _current.Y),
            Math.Max(_start.X, _current.X),
            Math.Max(_start.Y, _current.Y));

        if (rect.Width < 20 || rect.Height < 20)
        {
            DialogResult = DialogResult.Cancel;
        }
        else
        {
            rect.Offset(Bounds.Left, Bounds.Top);
            SelectedRegion = rect;
            DialogResult = DialogResult.OK;
        }
        Close();
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = Rectangle.FromLTRB(
            Math.Min(_start.X, _current.X),
            Math.Min(_start.Y, _current.Y),
            Math.Max(_start.X, _current.X),
            Math.Max(_start.Y, _current.Y));

        if (_selecting && rect.Width > 0 && rect.Height > 0)
        {
            using var bg = new SolidBrush(Color.FromArgb(255, 41, 121, 255));
            e.Graphics.FillRectangle(bg, rect);
            using var pen = new Pen(Color.White, 2f);
            e.Graphics.DrawRectangle(pen, rect);

            using var font = new Font("Microsoft YaHei UI", 10f);
            var label = $"{rect.Width} x {rect.Height}";
            var size = e.Graphics.MeasureString(label, font);
            var labelRect = new RectangleF(rect.Left, Math.Max(2, rect.Top - size.Height - 8), size.Width + 12, size.Height + 6);
            e.Graphics.FillRectangle(Brushes.White, labelRect);
            using var textBrush = new SolidBrush(Color.FromArgb(18, 28, 46));
            e.Graphics.DrawString(label, font, textBrush, labelRect.X + 6, labelRect.Y + 3);
        }
        else if (!_selecting)
        {
            var text = "按住左键并拖动，框出需要识别的字幕区域；Esc 取消";
            using var font = new Font("Microsoft YaHei UI", 11f);
            var size = e.Graphics.MeasureString(text, font);
            var x = Math.Max(8, (ClientSize.Width - (int)size.Width) / 2);
            var y = Math.Max(8, 24);
            using var bg = new SolidBrush(Color.FromArgb(170, 18, 28, 46));
            using var textBrush = new SolidBrush(Color.White);
            e.Graphics.FillRectangle(bg, x - 10, y - 8, size.Width + 20, size.Height + 16);
            e.Graphics.DrawString(text, font, textBrush, x, y);
        }
    }
}
