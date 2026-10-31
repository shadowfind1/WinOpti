using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WinOpti.UI
{
    /// <summary>Оценка «готовность к играм» 0–100: большая цифра и шкала из 20 делений.
    /// Рисуется одним OnRender без вложенных элементов — дёшево анимировать.</summary>
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

        const int Segments = 20;
        const double BarHeight = 26, Gap = 4;

        static readonly Brush Track = Frozen(new SolidColorBrush(Color.FromRgb(0x1A, 0x22, 0x33)));
        static readonly Brush MutedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA6)));
        static readonly Typeface Display = new Typeface(new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Unbounded"),
            FontStyles.Normal, FontWeights.ExtraBold, FontStretches.Normal);
        static readonly Typeface DisplayMedium = new Typeface(new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Unbounded"),
            FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

        static Brush Frozen(Brush b) { b.Freeze(); return b; }

        /// <summary>Цвет по оценке: красный → оранжевый → голубой акцент.</summary>
        static Color ScoreColor(double v)
        {
            Color a = Color.FromRgb(0xFF, 0x5D, 0x6C), b = Color.FromRgb(0xFF, 0xB3, 0x47), c = Color.FromRgb(0x3B, 0xE8, 0xFF);
            if (v < 60) return Lerp(a, b, Math.Max(0, (v - 30) / 30));
            return Lerp(b, c, Math.Min(1, (v - 60) / 30));
        }

        static Color Lerp(Color x, Color y, double t) => Color.FromRgb(
            (byte)(x.R + (y.R - x.R) * t), (byte)(x.G + (y.G - x.G) * t), (byte)(x.B + (y.B - x.B) * t));

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double v = Math.Max(0, Math.Min(100, Value));
            var brush = new SolidColorBrush(ScoreColor(v));
            brush.Freeze();

            // шкала внизу: деления заполняются по очереди, последнее — частично (плавно при анимации)
            double seg = (w - Gap * (Segments - 1)) / Segments, y = h - BarHeight, filled = v / 100 * Segments;
            for (int i = 0; i < Segments; i++)
            {
                var rect = new Rect(i * (seg + Gap), y, seg, BarHeight);
                dc.DrawRoundedRectangle(Track, null, rect, 3, 3);
                double part = Math.Max(0, Math.Min(1, filled - i));
                if (part <= 0) continue;
                dc.PushOpacity(part);
                dc.DrawRoundedRectangle(brush, null, rect, 3, 3);
                dc.Pop();
            }

            if (!ShowNumber) return;
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double size = Math.Min(96, Math.Min(w * 0.34, (h - BarHeight - 16) * 0.85));
            var num = new FormattedText(Math.Round(v).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, Display, size, brush, dpi);
            var cap = new FormattedText("/100", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, DisplayMedium, size * 0.22, MutedBrush, dpi);
            double top = y - 16 - num.Height;
            dc.DrawText(num, new Point(0, top));
            dc.DrawText(cap, new Point(num.Width + 8, top + num.Baseline - cap.Baseline));
        }
    }
}
