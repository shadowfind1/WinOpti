using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinOpti.Fps;
using WinOpti.Sys;

namespace WinOpti.UI
{
    /// <summary>
    /// Карточка результата 1080×1350 (формат поста Instagram): игра, FPS до/после, прирост, железо.
    /// Её хочется скинуть другу — и каждая такая картинка рекламирует WinOpti.
    /// </summary>
    public static class ShareCard
    {
        const int W = 1080, H = 1350;

        public static string PcSpec()
        {
            string Clean(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\((R|TM|tm|r)\)|CPU|@.*$|\d+-Core Processor", "").Replace("  ", " ").Trim();
            var cpu = Clean(SysInfo.Wmi("SELECT Name FROM Win32_Processor").Select(o => o["Name"] as string).FirstOrDefault());
            var gpus = SysInfo.Wmi("SELECT Name FROM Win32_VideoController").Select(o => (o["Name"] as string ?? "").Trim()).ToList();
            var gpu = gpus.FirstOrDefault(g => g.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 || g.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0
                                              || g.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) >= 0) ?? gpus.FirstOrDefault();
            gpu = (gpu ?? "").Replace("NVIDIA ", "").Replace("AMD ", "");
            double ram = SysInfo.Wmi("SELECT Capacity FROM Win32_PhysicalMemory").Sum(o => Convert.ToDouble(o["Capacity"] ?? 0)) / (1L << 30);
            return string.Join("  ·  ", new[] { cpu, gpu, ram > 0 ? $"{ram:0} ГБ RAM" : null }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        static Brush Hex(string h) => (Brush)new BrushConverter().ConvertFrom(h);
        static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI");

        static TextBlock T(string text, double size, string color = "#E8EAED", FontWeight? weight = null) => new TextBlock
        {
            Text = text, FontSize = size, Foreground = Hex(color), FontFamily = Display,
            FontWeight = weight ?? FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        };

        public static BitmapSource Render(FpsRun before, FpsRun after, string spec)
        {
            var root = new Grid { Width = W, Height = H };
            root.Background = new LinearGradientBrush(Color.FromRgb(0x0D, 0x0F, 0x12), Color.FromRgb(0x06, 0x07, 0x08), 90);
            // фирменное свечение сверху
            root.Children.Add(new Border
            {
                Background = new RadialGradientBrush(Color.FromArgb(0x38, 0xC6, 0xFF, 0x3D), Colors.Transparent)
                { Center = new Point(0.5, 0), GradientOrigin = new Point(0.5, 0), RadiusX = 0.9, RadiusY = 0.55 },
            });

            var stack = new StackPanel { Margin = new Thickness(80, 80, 80, 70) };
            root.Children.Add(stack);

            var brand = new StackPanel { Orientation = Orientation.Horizontal };
            brand.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")), Width = 64, Height = 64 });
            var bt = T("WinOpti", 40, weight: FontWeights.Bold);
            bt.Margin = new Thickness(20, 0, 0, 0);
            bt.VerticalAlignment = VerticalAlignment.Center;
            brand.Children.Add(bt);
            stack.Children.Add(brand);

            var game = T(before.Game.ToUpperInvariant(), 88, weight: FontWeights.Bold);
            game.Margin = new Thickness(0, 70, 0, 0);
            stack.Children.Add(game);
            var sub = T("Замер FPS до и после оптимизации", 34, "#8B9099", FontWeights.Normal);
            sub.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(sub);

            stack.Children.Add(Row("Средний FPS", before.Stats.AvgFps, after.Stats.AvgFps, true, 60));
            stack.Children.Add(Row("1% low", before.Stats.Low1, after.Stats.Low1, true, 34));
            stack.Children.Add(Row("Фризы", before.Stats.Stutters, after.Stats.Stutters, false, 34));

            var foot = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(80, 0, 80, 70) };
            foot.Children.Add(T(spec, 28, "#8B9099", FontWeights.Normal));
            var link = T("github.com/shadowfind1/WinOpti", 28, "#C6FF3D");
            link.Margin = new Thickness(0, 10, 0, 0);
            foot.Children.Add(link);
            root.Children.Add(foot);

            root.Measure(new Size(W, H));
            root.Arrange(new Rect(0, 0, W, H));
            root.UpdateLayout();
            var bmp = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(root);
            bmp.Freeze();
            return bmp;
        }

        static UIElement Row(string name, double a, double b, bool higherIsBetter, double top)
        {
            bool count = !higherIsBetter;
            int v = FpsPage.Verdict(a, b, higherIsBetter, count);
            var g = new Grid { Margin = new Thickness(0, top, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(T(name, 30, "#8B9099", FontWeights.Normal));
            var nums = new TextBlock { FontFamily = Display, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0) };
            nums.Inlines.Add(new System.Windows.Documents.Run($"{a:0}") { FontSize = 64, Foreground = Hex("#6B717B") });
            nums.Inlines.Add(new System.Windows.Documents.Run("  →  ") { FontSize = 50, Foreground = Hex("#6B717B") });
            nums.Inlines.Add(new System.Windows.Documents.Run($"{b:0}") { FontSize = 92, Foreground = Hex("#E8EAED") });
            left.Children.Add(nums);
            g.Children.Add(left);
            var pill = new Border
            {
                Background = Hex(v == 0 ? "#14171B" : v > 0 ? "#10231A" : "#2A1519"),
                CornerRadius = new CornerRadius(22), Padding = new Thickness(28, 12, 28, 14), VerticalAlignment = VerticalAlignment.Bottom,
                Child = T(FpsPage.DeltaText(a, b, count), count && v == 0 ? 36 : 52, v == 0 ? "#8B9099" : v > 0 ? "#3ECF8E" : "#FF5D6C", FontWeights.Bold),
            };
            Grid.SetColumn(pill, 1);
            g.Children.Add(pill);
            return g;
        }

        public static void SavePng(BitmapSource bmp, string file)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(file)) enc.Save(f);
        }
    }
}
