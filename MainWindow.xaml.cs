using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
            AdminText.Text = admin ? L.T("Права администратора", "Administrator rights") : L.T("Без прав администратора", "No administrator rights");
            AdminDot.Fill = (System.Windows.Media.Brush)FindResource(admin ? "Green" : "Orange");
            SystemText.Text = Environment.OSVersion.Version.Build >= 22000 ? L.T($"Windows 11 · сборка {Environment.OSVersion.Version.Build}", $"Windows 11 · build {Environment.OSVersion.Version.Build}")
                                                                           : L.T($"Windows 10 · сборка {Environment.OSVersion.Version.Build}", $"Windows 10 · build {Environment.OSVersion.Version.Build}");
            (L.En ? LangEn : LangRu).IsChecked = true;
            _langReady = true;
            Loaded += async (s, e) => await ScanAsync();
            Loaded += async (s, e) => await CheckUpdateAsync();
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
            DiagScroll.ScrollToTop();
            ProblemsHeader.Visibility = OkHeader.Visibility = Visibility.Collapsed;
            Verdict.Text = L.T("Проверяем ПК…", "Checking your PC…");
            Summary.Text = L.T("Это займёт пару секунд. Ничего не меняем, только смотрим.", "Takes a couple of seconds. Nothing is changed, we only look.");
            AnimateRing(0, 0.3);
            SetProgress(0);

            // все проверки параллельно в фоне: окно не замирает ни на кадр
            var pending = Checks.All.ToDictionary(c => Task.Run(() =>
            {
                try { return c.Run().ToList(); }
                catch (Exception ex) { App.Log(ex); return new List<Finding>(); }
            }), c => c);
            int total = pending.Count, done = 0;
            ProgressText.Text = L.T($"Проверено 0 из {total}", $"Checked 0 of {total}");

            while (pending.Count > 0)
            {
                var t = await Task.WhenAny(pending.Keys);
                pending.Remove(t);
                done++;
                foreach (var f in t.Result) Add(f);
                SetProgress((double)done / total);
                ProgressText.Text = done < total
                    ? L.T($"Проверено {done} из {total} · сейчас: {string.Join(", ", pending.Values.Take(2).Select(c => c.Name.ToLowerInvariant()))}", $"Checked {done} of {total} · now: {string.Join(", ", pending.Values.Take(2).Select(c => c.Name.ToLowerInvariant()))}")
                    : L.T($"Проверено {total} из {total} · {DateTime.Now:HH:mm}", $"Checked {total} of {total} · {DateTime.Now:HH:mm}");
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
            Verdict.Text = score >= 90 ? L.T("ПК готов к играм", "Your PC is ready for games") : score >= 70 ? L.T("Хорошо, но есть что подтянуть", "Good, but a few things can be better")
                         : score >= 45 ? L.T("Есть что исправить", "Some things need fixing") : L.T("ПК теряет много производительности", "Your PC is losing a lot of performance");
            if (open.Count == 0)
                Summary.Text = L.T("Ни одной проблемы, которая съедает FPS. Можно идти играть.", "Nothing that costs FPS. Go play.");
            else
                Summary.Text = L.T("Нашли ", "Found ") + L.N(open.Count, "пункт", "пункта", "пунктов", "issue", "issues") +
                               (serious > 0 ? L.T($", из них важных: {serious}", $", {serious} important") : "") +
                               (fixable > 0 ? ". " + L.N(fixable, "исправляется", "исправляются", "исправляются", "can be fixed", "can be fixed") + L.T(" в один клик.", " in one click.") : ".");
        }

        bool _langReady;

        // смена языка перезапускает программу: так все страницы гарантированно перерисуются
        void OnLang(object sender, RoutedEventArgs e)
        {
            if (_langReady) L.Switch(sender == LangEn);
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

        void OnGoFps(object sender, RoutedEventArgs e) => NavFps.IsChecked = true;

        // ---------- обновление ----------

        Update.Updater.Release _update;
        bool _updating;

        async Task CheckUpdateAsync()
        {
            if (Environment.GetCommandLineArgs().Contains("--updated"))
            {
                await Task.Run(Update.Updater.AfterUpdate);
                UpdateButton.ToolTip = null;
                UpdateButton.Content = L.T($"Обновлено до {Update.Updater.Current}", $"Updated to {Update.Updater.Current}");
                UpdateButton.Visibility = Visibility.Visible;
                await Task.Delay(6000);
                UpdateButton.Visibility = Visibility.Collapsed;
                return;
            }
            await Task.Delay(1500); // сначала диагностика, сеть потом
            _update = await Update.Updater.CheckAsync();
            if (_update == null) return;
            var notes = _update.Notes.Replace("\r", "").Trim();
            if (notes.Length > 600) notes = notes.Substring(0, 600).TrimEnd() + "…";
            UpdateTitle.Text = L.T($"Вышла версия {_update.Name}", $"{_update.Name} is out");
            UpdateNotes.Text = notes;
            UpdateButton.Content = L.T("Обновить", "Update"); // коротко: в шапке мало места, версия — в подсказке
            UpdateButton.Visibility = Visibility.Visible;
        }

        async void OnUpdate(object sender, RoutedEventArgs e)
        {
            if (_update == null || _updating) return;
            _updating = true;
            try
            {
                var progress = new Progress<double>(p => UpdateButton.Content = L.T($"Скачиваю {p:P0}", $"Downloading {p:P0}"));
                var file = await Update.Updater.DownloadAsync(_update, progress);
                UpdateButton.Content = L.T("Перезапускаю…", "Restarting…");
                Update.Updater.InstallAndRestart(file);
            }
            catch (Exception ex)
            {
                App.Log(ex);
                // скачать вручную со страницы релиза
                UpdateButton.Content = L.T("Не вышло — открыть страницу", "Failed — open the page");
                UpdateButton.Click -= OnUpdate;
                UpdateButton.Click += (s, a) => Process.Start(new ProcessStartInfo(_update.Url) { UseShellExecute = true });
            }
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
