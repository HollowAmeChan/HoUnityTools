using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.FaceTracking.Core;

namespace HoFace.Studio
{
    public sealed class ParameterPlot : FrameworkElement
    {
        public HoFaceOutput Rule { get; set; }
        public HoFaceIrNode Node { get; set; }
        public int SelectedIndex { get; private set; } = -1;
        public double CursorX { get; set; }
        public double CursorY { get; set; }
        public object Viewport => new { minX, maxX, minY, maxY };
        public event Action EditBeginning, EditChanged, EditCommitted, SelectionChanged;
        public event Action<double, double> PreviewChanged;
        private double minX = -.1, maxX = 1.1, minY = -.1, maxY = 1.1;
        private bool editing, panning;
        private int tangent;
        private Point last;
        private readonly Brush ink = new SolidColorBrush(Color.FromRgb(187, 204, 219));
        private readonly Brush accent = new SolidColorBrush(Color.FromRgb(104, 183, 168));
        private readonly Brush hot = new SolidColorBrush(Color.FromRgb(221, 168, 112));
        private readonly Typeface typeface = new Typeface("Microsoft YaHei UI");
        private Rect Plot => new Rect(48, 22, Math.Max(10, ActualWidth - 72), Math.Max(10, ActualHeight - 64));
        public ParameterPlot()
        {
            Focusable = true;
            MinHeight = 160;
            ClipToBounds = true;
            Cursor = Cursors.Cross;
        }
        public void Fit()
        {
            var points = DataPoints();
            if (points.Count == 0) points.AddRange(new[] { new Point(0, 0), new Point(1, 1) });
            minX = points.Min(p => p.X); maxX = points.Max(p => p.X);
            minY = points.Min(p => p.Y); maxY = points.Max(p => p.Y);
            if (maxX - minX < .01) { minX -= .5; maxX += .5; }
            if (maxY - minY < .01) { minY -= .5; maxY += .5; }
            double dx = (maxX - minX) * .14, dy = (maxY - minY) * .14;
            minX -= dx; maxX += dx; minY -= dy; maxY += dy;
            InvalidateVisual();
        }
        public void ClearSelection() { SelectedIndex = -1; InvalidateVisual(); }
        public void SelectIndex(int index)
        {
            if (index < -1 || index >= DataPoints().Count) throw new ArgumentOutOfRangeException(nameof(index));
            SelectedIndex = index;
            SelectionChanged?.Invoke();
            InvalidateVisual();
        }
        public void ValidateSelection()
        {
            if (SelectedIndex >= DataPoints().Count) SelectedIndex = DataPoints().Count - 1;
        }
        private List<Point> DataPoints()
        {
            if (Rule != null)
                return (Rule.curve?.keys ?? new HoFaceCurveKey[0]).Select(k => new Point(k.time, k.value)).ToList();
            if (Node != null && (Node.type == "blend1d" || Node.type == "blend2d"))
                return Node.children.Select(c => Node.type == "blend1d" ? new Point(c.threshold, 0) : new Point(c.x, c.y)).ToList();
            return new List<Point>();
        }
        private Point Screen(Point p) => new Point(Plot.Left + (p.X - minX) / (maxX - minX) * Plot.Width,
            Plot.Bottom - (p.Y - minY) / (maxY - minY) * Plot.Height);
        private Point Data(Point p) => new Point(minX + (p.X - Plot.Left) / Plot.Width * (maxX - minX),
            minY + (Plot.Bottom - p.Y) / Plot.Height * (maxY - minY));
        private void Text(DrawingContext dc, string text, Point position, Brush brush = null)
        {
            dc.DrawText(new FormattedText(text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, 11, brush ?? ink, VisualTreeHelper.GetDpi(this).PixelsPerDip), position);
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(21, 27, 33)), null, new Rect(RenderSize));
            if (Rule == null && Node?.type != "blend1d" && Node?.type != "blend2d")
            {
                string title = Node == null ? "选择参数或混合节点" : Node.name ?? Node.id;
                Text(dc, title, new Point(26, 26), accent);
                if (Node != null)
                {
                    Text(dc, Node.type == "pose" ? "POSE" : "DIRECT", new Point(26, 54));
                    var entries = Node.type == "pose"
                        ? Node.weights.Select(w => (w.target, w.value)).ToArray()
                        : Node.children.Select(c => (c.node, c.weight)).ToArray();
                    for (int i = 0; i < entries.Length && 98 + i * 28 < ActualHeight - 20; i++)
                    {
                        Text(dc, entries[i].Item1, new Point(26, 94 + i * 28));
                        Text(dc, entries[i].Item2.ToString("0.###", CultureInfo.InvariantCulture),
                            new Point(Math.Max(160, ActualWidth - 70), 94 + i * 28), accent);
                    }
                }
                return;
            }
            var grid = new Pen(new SolidColorBrush(Color.FromRgb(48, 59, 69)), 1);
            for (int i = 0; i <= 5; i++)
            {
                double x = Plot.Left + Plot.Width * i / 5, y = Plot.Top + Plot.Height * i / 5;
                dc.DrawLine(grid, new Point(x, Plot.Top), new Point(x, Plot.Bottom));
                dc.DrawLine(grid, new Point(Plot.Left, y), new Point(Plot.Right, y));
                Text(dc, (minX + (maxX - minX) * i / 5).ToString("0.###"), new Point(x - 12, Plot.Bottom + 9));
                Text(dc, (maxY - (maxY - minY) * i / 5).ToString("0.###"), new Point(2, y - 7));
            }
            if (Rule != null)
            {
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                    for (int i = 0; i <= 256; i++)
                    {
                        float x = (float)(minX + (maxX - minX) * i / 256);
                        float y = HoFaceCurve.Transfer(Rule.curve, x);
                        Point p = Screen(new Point(x, y));
                        if (i == 0) context.BeginFigure(p, false, false); else context.LineTo(p, true, false);
                    }
                geometry.Freeze();
                dc.DrawGeometry(null, new Pen(accent, 2), geometry);
            }
            var points = DataPoints();
            if (Rule != null && SelectedIndex >= 0 && SelectedIndex < points.Count)
            {
                foreach (int sign in new[] { -1, 1 })
                {
                    Point? handle = TangentHandle(sign);
                    if (handle == null) continue;
                    dc.DrawLine(new Pen(hot, 1), Screen(points[SelectedIndex]), handle.Value);
                    dc.DrawRectangle(hot, null, new Rect(handle.Value.X - 4, handle.Value.Y - 4, 8, 8));
                }
            }
            for (int i = 0; i < points.Count; i++)
            {
                Point p = Screen(points[i]);
                dc.DrawEllipse(i == SelectedIndex ? hot : accent, new Pen(new SolidColorBrush(Color.FromRgb(169, 181, 191)), .7), p, 6, 6);
                string label = Rule != null ? i.ToString() : Node.children[i].node;
                Text(dc, label, new Point(p.X + 10, p.Y - 18));
            }
            Point cursor = Screen(new Point(CursorX, Rule != null ? HoFaceCurve.Transfer(Rule.curve, (float)CursorX) : CursorY));
            dc.DrawLine(new Pen(hot, 1), new Point(cursor.X - 7, cursor.Y), new Point(cursor.X + 7, cursor.Y));
            dc.DrawLine(new Pen(hot, 1), new Point(cursor.X, cursor.Y - 7), new Point(cursor.X, cursor.Y + 7));
            Text(dc, Rule != null ? "输入 → 输出" : Node.parameterX +
                (Node.type == "blend2d" ? " / " + Node.parameterY : ""), new Point(Plot.Left, 2));
        }
        private Point? TangentHandle(int sign)
        {
            var keys = Rule?.curve?.keys;
            if (keys == null || SelectedIndex < 0 || SelectedIndex >= keys.Length) return null;
            var key = keys[SelectedIndex];
            float slope = sign < 0 ? key.inTangent : key.outTangent;
            if (float.IsInfinity(slope)) return null;
            double dx = sign * (maxX - minX) * .12;
            return Screen(new Point(key.time + dx, key.value + slope * dx));
        }
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            last = e.GetPosition(this);
            if (!Plot.Contains(last)) return;
            if (Rule == null && Node?.type != "blend1d" && Node?.type != "blend2d") return;
            if (e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle)
            { panning = true; CaptureMouse(); e.Handled = true; return; }
            if (e.ChangedButton != MouseButton.Left) return;
            var points = DataPoints();
            tangent = 0;
            if (Rule != null)
                foreach (int sign in new[] { -1, 1 })
                {
                    var p = TangentHandle(sign);
                    if (p != null && (p.Value - last).Length < 10) { tangent = sign; break; }
                }
            if (tangent == 0)
            {
                SelectedIndex = -1;
                for (int i = 0; i < points.Count; i++)
                    if ((Screen(points[i]) - last).Length < 11) { SelectedIndex = i; break; }
            }
            if (Rule != null && e.ClickCount == 2 && SelectedIndex < 0)
            {
                EditBeginning?.Invoke();
                Point p = Data(last);
                var keys = (Rule.curve?.keys ?? HoFaceCurveData.Linear(0, 0, 1, 1).keys).ToList();
                if (keys.All(k => Math.Abs(k.time - p.X) > 1e-5))
                {
                    keys.Add(new HoFaceCurveKey((float)p.X, (float)p.Y, 1, 1));
                    Rule.curve = new HoFaceCurveData(keys.ToArray());
                    EditChanged?.Invoke();
                }
                EditCommitted?.Invoke(); InvalidateVisual(); return;
            }
            SelectionChanged?.Invoke();
            if (SelectedIndex >= 0)
            {
                EditBeginning?.Invoke(); editing = true; CaptureMouse();
            }
            else if (Rule == null && (Node?.type == "blend1d" || Node?.type == "blend2d"))
            {
                Point p = Data(last); CursorX = p.X; CursorY = Node.type == "blend1d" ? 0 : p.Y;
                PreviewChanged?.Invoke(CursorX, CursorY);
            }
            InvalidateVisual(); e.Handled = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point current = e.GetPosition(this);
            if (panning)
            {
                double dx = (current.X - last.X) / Plot.Width * (maxX - minX);
                double dy = (current.Y - last.Y) / Plot.Height * (maxY - minY);
                minX -= dx; maxX -= dx; minY += dy; maxY += dy; last = current;
                InvalidateVisual(); return;
            }
            if (!editing) return;
            Point point = Data(current);
            if (Rule?.curve != null)
            {
                var keys = Rule.curve.keys;
                if (SelectedIndex < 0 || SelectedIndex >= keys.Length) return;
                var key = keys[SelectedIndex];
                if (tangent != 0)
                {
                    double dx = point.X - key.time;
                    if (Math.Abs(dx) > 1e-5)
                    {
                        float slope = (float)((point.Y - key.value) / dx);
                        if (tangent < 0) key.inTangent = slope; else key.outTangent = slope;
                    }
                }
                else
                {
                    float time = (float)point.X;
                    if (keys.Where((k, i) => i != SelectedIndex).Any(k => Math.Abs(k.time - time) < 1e-5)) return;
                    key.time = time; key.value = (float)point.Y;
                }
                keys[SelectedIndex] = key;
                Rule.curve.keys = keys;
                SelectedIndex = Array.FindIndex(Rule.curve.keys, k => k.time == key.time);
            }
            else if (Node != null && SelectedIndex < Node.children.Count)
            {
                var child = Node.children[SelectedIndex];
                if (Node.type == "blend1d") child.threshold = (float)point.X;
                else { child.x = (float)point.X; child.y = (float)point.Y; }
            }
            EditChanged?.Invoke(); InvalidateVisual();
        }
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            FinishEdit(); panning = false; ReleaseMouseCapture();
        }
        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e); FinishEdit(); panning = false;
        }
        private void FinishEdit()
        {
            if (!editing) return;
            editing = false; EditCommitted?.Invoke();
        }
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            Point center = Data(e.GetPosition(this));
            double factor = e.Delta > 0 ? .85 : 1 / .85;
            if ((maxX - minX) * factor < 1e-6 || (maxX - minX) * factor > 1e8) return;
            minX = center.X + (minX - center.X) * factor; maxX = center.X + (maxX - center.X) * factor;
            minY = center.Y + (minY - center.Y) * factor; maxY = center.Y + (maxY - center.Y) * factor;
            InvalidateVisual(); e.Handled = true;
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.F) { Fit(); e.Handled = true; }
            if (e.Key == Key.Delete && Rule?.curve != null && SelectedIndex >= 0 && Rule.curve.keys.Length > 2)
            {
                EditBeginning?.Invoke();
                Rule.curve.keys = Rule.curve.keys.Where((_, i) => i != SelectedIndex).ToArray();
                SelectedIndex = Math.Min(SelectedIndex, Rule.curve.keys.Length - 1);
                EditChanged?.Invoke();
                EditCommitted?.Invoke();
                SelectionChanged?.Invoke();
                InvalidateVisual();
                e.Handled = true;
            }
        }
    }
}
