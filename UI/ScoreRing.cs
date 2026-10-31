using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WinOpti.UI
{
    /// <summary>Кольцо «готовность к играм» 0–100. Рисуется одним OnRender без вложенных элементов — дёшево анимировать.</summary>
    public sealed class ScoreRing : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(ScoreRing),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowNumberProperty = DependencyProperty.Register(
            nameof(ShowNumber), typeof(bool), typeof(ScoreRing),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public bool ShowNumber { get => (bool)GetValue(ShowNumberProperty); set => SetValue(ShowNumberProperty, value); }

        static readonly Brush Track = Frozen(new SolidColorBrush(Color.FromRgb(0x1C, 0x20, 0x26)));
        static readonly Pen TrackPen = FrozenPen(Track, 12);

        static Brush Frozen(Brush b) { b.Freeze(); return b; }
        static Pen FrozenPen(Brush b, double w) { var p = new Pen(b, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; p.Freeze(); return p; }

        /// <summary>Цвет по оценке: красный → оранжевый → зелёный.</summary>
        static Color ScoreColor(double v)
        {
            Color a = Color.FromRgb(0xFF, 0x5D, 0x6C), b = Color.FromRgb(0xF5, 0xA6, 0x23), c = Color.FromRgb(0xC6, 0xFF, 0x3D);
            if (v < 60) return Lerp(a, b, Math.Max(0, (v - 30) / 30));
            return Lerp(b, c, Math.Min(1, (v - 60) / 30));
        }

        static Color Lerp(Color x, Color y, double t) => Color.FromRgb(
            (byte)(x.R + (y.R - x.R) * t), (byte)(x.G + (y.G - x.G) * t), (byte)(x.B + (y.B - x.B) * t));

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) return;
            double thick = 12, r = size / 2 - thick / 2;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            dc.DrawEllipse(null, TrackPen, center, r, r);

            double v = Math.Max(0, Math.Min(100, Value));
            var color = ScoreColor(v);
            if (v > 0.2)
            {
                var pen = new Pen(new SolidColorBrush(color), thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                double angle = 359.99 * v / 100;
                double a0 = -90 * Math.PI / 180, a1 = (angle - 90) * Math.PI / 180;
                var p0 = new Point(center.X + r * Math.Cos(a0), center.Y + r * Math.Sin(a0));
                var p1 = new Point(center.X + r * Math.Cos(a1), center.Y + r * Math.Sin(a1));
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(p0, false, false);
                    ctx.ArcTo(p1, new Size(r, r), 0, angle > 180, SweepDirection.Clockwise, true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }

            if (!ShowNumber) return;
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var num = new FormattedText(Math.Round(v).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface(new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#JetBrains Mono"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                size * 0.3, new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xED)), dpi);
            dc.DrawText(num, new Point(center.X - num.Width / 2, center.Y - num.Height / 2 - size * 0.04));
            var cap = new FormattedText("из 100", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Inter Tight"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size * 0.085, new SolidColorBrush(Color.FromRgb(0x8B, 0x90, 0x99)), dpi);
            dc.DrawText(cap, new Point(center.X - cap.Width / 2, center.Y + num.Height / 2 - size * 0.08));
        }
    }
}
