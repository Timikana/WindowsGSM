using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace WindowsGSM.Controls
{
    /// <summary>
    /// Lightweight mini-graph (sparkline) drawn directly (no external dependency).
    /// Displays a series of values (CPU%, RAM MB, ...) as a curve with a light fill.
    /// </summary>
    public class Sparkline : FrameworkElement
    {
        public static readonly DependencyProperty ValuesProperty =
            DependencyProperty.Register(nameof(Values), typeof(IEnumerable<double>), typeof(Sparkline),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeProperty =
            DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Sparkline),
                new FrameworkPropertyMetadata(Brushes.RoyalBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        // Optional fixed ceiling (e.g. 100 for %). 0 = auto-scale on the data.
        public static readonly DependencyProperty MaxProperty =
            DependencyProperty.Register(nameof(Max), typeof(double), typeof(Sparkline),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public IEnumerable<double> Values
        {
            get => (IEnumerable<double>)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public double Max
        {
            get => (double)GetValue(MaxProperty);
            set => SetValue(MaxProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth, h = ActualHeight;
            if (w <= 1 || h <= 1) { return; }

            var data = Values?.ToList();
            if (data == null || data.Count < 2) { return; }

            double max = Max > 0 ? Max : Math.Max(1.0, data.Max());
            double min = 0;
            double range = Math.Max(1e-6, max - min);

            const double pad = 2.0;
            double usableH = Math.Max(1.0, h - 2 * pad);
            double stepX = w / (data.Count - 1);

            var pts = new Point[data.Count];
            for (int i = 0; i < data.Count; i++)
            {
                double v = data[i];
                if (v < min) { v = min; }
                if (v > max) { v = max; }
                double y = pad + (1.0 - (v - min) / range) * usableH;
                pts[i] = new Point(i * stepX, y);
            }

            // Curve
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                ctx.PolyLineTo(pts.Skip(1).ToArray(), true, true);
            }
            geo.Freeze();

            var pen = new Pen(Stroke, 1.5) { LineJoin = PenLineJoin.Round };
            pen.Freeze();

            // Light fill below the curve
            var fillGeo = new StreamGeometry();
            using (var ctx = fillGeo.Open())
            {
                ctx.BeginFigure(new Point(pts[0].X, h), false, true);
                ctx.LineTo(pts[0], true, false);
                ctx.PolyLineTo(pts.Skip(1).ToArray(), true, true);
                ctx.LineTo(new Point(pts[pts.Length - 1].X, h), true, false);
            }
            fillGeo.Freeze();

            Brush fill = Stroke is SolidColorBrush scb
                ? new SolidColorBrush(Color.FromArgb(40, scb.Color.R, scb.Color.G, scb.Color.B))
                : null;
            if (fill != null) { fill.Freeze(); dc.DrawGeometry(fill, null, fillGeo); }

            dc.DrawGeometry(null, pen, geo);
        }
    }
}
