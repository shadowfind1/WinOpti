using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using WinOpti.Diagnostics;
using WinOpti.Sys;
using WinOpti.UI;

namespace WinOpti
{
    public partial class MainWindow : Window
    {
        readonly ObservableCollection<FindingViewModel> _problems = new ObservableCollection<FindingViewModel>();
        readonly ObservableCollection<FindingViewModel> _ok = new ObservableCollection<FindingViewModel>();
        bool _scanning;

        public MainWindow()
        {
            InitializeComponent();
            ProblemsList.ItemsSource = _problems;
            OkList.ItemsSource = _ok;
            // новые карточки и кнопки не должны сами прокручивать страницу
            ProblemsList.RequestBringIntoView += (s, e) => e.Handled = true;
            OkList.RequestBringIntoView += (s, e) => e.Handled = true;
            StateChanged += (s, e) =>
            {
                // развёрнутое окно без рамки вылезает за экран на толщину рамки
                Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
                MaxGlyph.Text = WindowState == WindowState.Maximized ? "" : "";
            };
            bool admin = SysInfo.IsAdmin;
            AdminText.Text = admin ? "Права администратора" : "Без прав администратора";
            AdminDot.Fill = (System.Windows.Media.Brush)FindResource(admin ? "Green" : "Orange");
            SystemText.Text = Environment.OSVersion.Version.Build >= 22000 ? $"Windows 11 · сборка {Environment.OSVersion.Version.Build}"
                                                                           : $"Windows 10 · сборка {Environment.OSVersion.Version.Build}";
            Loaded += async (s, e) => await ScanAsync();
#if DEBUG
            if (Environment.GetCommandLineArgs().Contains("--page=fps")) NavFps.IsChecked = true; // для скриншотов
            if (Environment.GetCommandLineArgs().Contains("--page=tweaks")) NavTweaks.IsChecked = true;
            if (Environment.GetCommandLineArgs().Contains("--page=clean")) NavClean.IsChecked = true;
            if (Environment.GetCommandLineArgs().Contains("--page=startup")) NavStartup.IsChecked = true;
            if (Environment.GetCommandLineArgs().Contains("--page=restore")) NavRestore.IsChecked = true;
            if (Environment.GetCommandLineArgs().Contains("--page=checks")) NavChecks.IsChecked = true;
#endif
        }

        // ---------- диагностика ----------

        async void OnScan(object sender, RoutedEventArgs e) => await ScanAsync();

        async Task ScanAsync()
        {
            if (_scanning) return;
            _scanning = true;
            ScanButton.IsEnabled = false;
            _problems.Clear();
            _ok.Clear();
            DiagPage.ScrollToTop();
            ProblemsHeader.Visibility = OkHeader.Visibility = Visibility.Collapsed;
            Verdict.Text = "Проверяем ПК…";
            Summary.Text = "Это займёт пару секунд. Ничего не меняем, только смотрим.";
            AnimateRing(0, 0.3);
            SetProgress(0);

            // все проверки параллельно в фоне: окно не замирает ни на кадр
            var pending = Checks.All.ToDictionary(c => Task.Run(() =>
            {
                try { return c.Run().ToList(); }
                catch (Exception ex) { App.Log(ex); return new List<Finding>(); }
            }), c => c);
            int total = pending.Count, done = 0;
            ProgressText.Text = $"Проверено 0 из {total}";

            while (pending.Count > 0)
            {
                var t = await Task.WhenAny(pending.Keys);
                pending.Remove(t);
                done++;
                foreach (var f in t.Result) Add(f);
                SetProgress((double)done / total);
                ProgressText.Text = done < total
                    ? $"Проверено {done} из {total} · сейчас: {string.Join(", ", pending.Values.Take(2).Select(c => c.Name.ToLowerInvariant()))}"
                    : $"Проверено {total} из {total} · {DateTime.Now:HH:mm}";
                UpdateScore();
            }

            UpdateScore(final: true);
            ScanButton.IsEnabled = true;
            _scanning = false;
        }

        void Add(Finding f)
        {
            var vm = new FindingViewModel(f);
            if (vm.IsOk)
            {
                _ok.Add(vm);
                OkHeader.Visibility = Visibility.Visible;
                return;
            }
            vm.Fixed += () => UpdateScore(final: true);
            int i = 0;
            while (i < _problems.Count && _problems[i].Order <= vm.Order) i++; // важные сверху
            _problems.Insert(i, vm);
            ProblemsHeader.Visibility = Visibility.Visible;
        }

        static int Penalty(Severity s) => s switch { Severity.Critical => 25, Severity.Warn => 12, Severity.Info => 4, _ => 0 };

        void UpdateScore(bool final = false)
        {
            var open = _problems.Where(p => !p.Done).ToList();
            int score = Math.Max(5, 100 - open.Sum(p => Penalty(p.F.Severity)));
            AnimateRing(score, 0.9);
            if (!final) return;

            int fixable = open.Count(p => p.CanFix);
            int serious = open.Count(p => p.F.Severity >= Severity.Warn);
            Verdict.Text = score >= 90 ? "ПК готов к играм" : score >= 70 ? "Хорошо, но есть что подтянуть"
                         : score >= 45 ? "Есть что исправить" : "ПК теряет много производительности";
            if (open.Count == 0)
                Summary.Text = "Ни одной проблемы, которая съедает FPS. Можно идти играть.";
            else
                Summary.Text = $"Нашли {Plural(open.Count, "пункт", "пункта", "пунктов")}" +
                               (serious > 0 ? $", из них важных: {serious}" : "") +
                               (fixable > 0 ? $". {Plural(fixable, "исправляется", "исправляются", "исправляются", false)} в один клик." : ".");
        }

        static string Plural(int n, string one, string few, string many, bool withNumber = true)
        {
            int a = Math.Abs(n) % 100, b = a % 10;
            string w = a > 10 && a < 20 ? many : b == 1 ? one : b >= 2 && b <= 4 ? few : many;
            return withNumber ? $"{n} {w}" : $"{n} {w}";
        }

        void AnimateRing(double to, double seconds)
        {
            var anim = new DoubleAnimation(to, TimeSpan.FromSeconds(seconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Ring.BeginAnimation(ScoreRing.ValueProperty, anim);
        }

        void SetProgress(double share)
        {
            double width = ProgressTrack.ActualWidth * share;
            var anim = new DoubleAnimation(width, TimeSpan.FromSeconds(0.35)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            ProgressFill.BeginAnimation(WidthProperty, anim);
        }

        // ---------- меню ----------

        void OnNav(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded && DiagPage == null) return;
            var tag = (string)((RadioButton)sender).Tag;
            // тяжёлые страницы создаём при первом открытии, а не при запуске: окно открывается быстрее
            if (tag == "tweaks" && TweaksView.Content == null) TweaksView.Content = new TweaksPage();
            if (tag == "clean" && CleanView.Content == null) CleanView.Content = new CleanupPage();
            if (tag == "startup" && StartupView.Content == null) StartupView.Content = new StartupPage();
            if (tag == "restore" && RestoreView.Content == null) RestoreView.Content = new RestorePage();
            if (tag == "checks" && ChecksView.Content == null) ChecksView.Content = new ChecksPage();
            var pages = new Dictionary<string, FrameworkElement>
            {
                ["diag"] = DiagPage, ["fps"] = FpsView, ["tweaks"] = TweaksView, ["clean"] = CleanView, ["startup"] = StartupView, ["restore"] = RestoreView, ["checks"] = ChecksView,
            };
            var show = pages[tag];
            foreach (var p in pages.Values)
                p.Visibility = p == show ? Visibility.Visible : Visibility.Collapsed;
            if (show != DiagPage)
            {
                show.Opacity = 0;
                show.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.25)));
            }
        }

        // ---------- рамка окна ----------

        void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void OnMaximize(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
