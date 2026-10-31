using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinOpti.Fps;

namespace WinOpti.UI
{
    public sealed class RunViewModel
    {
        public FpsRun Run { get; }
        public RunViewModel(FpsRun run, Action<FpsRun, string> mark, Action<FpsRun> delete)
        {
            Run = run;
            MarkBefore = new RelayCommand(() => mark(run, "До"));
            MarkAfter = new RelayCommand(() => mark(run, "После"));
            Delete = new RelayCommand(() => delete(run));
        }
        public string Game => Run.Game;
        public string When => Run.When.ToString("dd.MM.yyyy HH:mm") + $" · {Run.Stats.Seconds:0} с" + (Run.Passes?.Count > 1 ? $" · {Run.Passes.Count} прохода" : "");
        public string Avg => $"{Run.Stats.AvgFps:0}";
        public string Low1 => $"{Run.Stats.Low1:0}";
        public string Stutters => Run.Stats.Stutters.ToString();
        public string Label => Run.Label;
        public bool HasLabel => !string.IsNullOrEmpty(Run.Label);
        public Brush LabelBrush => (Brush)Application.Current.FindResource(Run.Label == "После" ? "Accent" : "CardAlt");
        public Brush LabelForeground => (Brush)Application.Current.FindResource(Run.Label == "После" ? "OnAccent" : "Text");
        public ICommand MarkBefore { get; }
        public ICommand MarkAfter { get; }
        public ICommand Delete { get; }
    }

    public partial class FpsPage : UserControl
    {
        /// <summary>Источник кадров (PresentMon или свой ETW). Пока не подключён — замер честно говорит об этом.</summary>
        public static IFrameSource Source;

        const int Settle = 6; // проверок по 0.5 с: игра 3 секунды подряд на весь экран — начинаем
        List<FpsRun> _runs = FpsStore.Load();
        readonly ObservableCollection<RunViewModel> _rows = new ObservableCollection<RunViewModel>();
        CancellationTokenSource _cancel;

        public FpsPage()
        {
            InitializeComponent();
            History.ItemsSource = _rows;
#if DEBUG
            if (Environment.GetCommandLineArgs().Contains("--demo-fps")) _runs = DemoRuns();
            var cardArg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--card="));
            if (cardArg != null) // проверка вёрстки карточки: рисуем по демо-данным и выходим
                Loaded += (s, e) =>
                {
                    var runs = DemoRuns();
                    ShareCard.SavePng(ShareCard.Render(runs[1], runs[0], ShareCard.PcSpec()), cardArg.Substring(7));
                    Application.Current.Shutdown();
                };
#endif
            Refresh();
        }

        int DurationSeconds => D30.IsChecked == true ? 30 : D120.IsChecked == true ? 120 : 60;

        // ---------- замер ----------

        async void OnStart(object sender, RoutedEventArgs e)
        {
            Scroll.ScrollToTop(); // кнопка «Замерить снова» внизу страницы — показываем ход замера
            Message.Visibility = Visibility.Collapsed;
            if (Source == null)
            {
                ShowMessage("Модуль замера кадров ещё подключается — эта часть появится в следующей сборке.");
                return;
            }
            if (!Sys.SysInfo.IsAdmin)
            {
                ShowMessage("Для замера FPS нужны права администратора: Windows отдаёт данные о кадрах только им. Запусти WinOpti от имени администратора.");
                return;
            }
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            SetupPanel.Visibility = Visibility.Collapsed;
            RunPanel.Visibility = Visibility.Visible;
            int duration = DurationSeconds;
            try
            {
                Phase.Text = "Запускай игру";
                PhaseText.Text = "Не торопись: замер начнётся сам, когда игра откроется на весь экран. Окно WinOpti можно свернуть.";
                RunFill.BeginAnimation(WidthProperty, null);
                RunFill.Width = 0;
                Timer.Text = "…";
                // ждём, пока игра окажется на весь экран и пробудет там пару секунд (загрузочные заставки проскакивают)
                string game = null;
                int stable = 0;
                while (stable < Settle)
                {
                    await Task.Delay(500, token);
                    var now = ForegroundGame.Detect();
                    if (now != null && now == game) stable++;
                    else { game = now; stable = now != null ? 1 : 0; }
                    if (game != null)
                    {
                        Phase.Text = $"Нашли игру: {game}";
                        PhaseText.Text = "Замер начнётся через пару секунд.";
                    }
                    else
                    {
                        Phase.Text = "Запускай игру";
                        PhaseText.Text = "Не торопись: замер начнётся сам, когда игра откроется на весь экран. Окно WinOpti можно свернуть.";
                    }
                }
                SystemSounds.Asterisk.Play();
                int passes = P3.IsChecked == true ? 3 : 1;
                int perPass = duration + PresentMonSource.WarmupSeconds; // первые секунды каждого прохода в статистику не идут
                int total = perPass * passes;
                RunFill.BeginAnimation(WidthProperty, new DoubleAnimation(0, RunTrack.ActualWidth, TimeSpan.FromSeconds(total)));
                var clock = DispatcherTimerFor(total);

                // пока идёт замер, смотрим, кто ещё тратит процессор и видеокарту
                var monitor = new BackgroundMonitor();
                monitor.Start();
                var caps = new List<(FrameCapture cap, double offset)>();
                List<LoadSample> samples;
                try
                {
                    for (int i = 0; i < passes; i++)
                    {
                        Phase.Text = passes > 1 ? $"Идёт замер · проход {i + 1} из {passes}" : "Идёт замер";
                        PhaseText.Text = $"Играй как обычно. Через {total} с прозвучит сигнал.";
                        double offset = monitor.Now;
                        caps.Add((await Task.Run(() => Source.Capture(TimeSpan.FromSeconds(duration), token), token), offset));
                    }
                }
                finally
                {
                    clock.Stop();
                    samples = monitor.Stop();
                }
                SystemSounds.Exclamation.Play();

                // все проходы в одну запись; моменты кадров — в секундах мониторинга
                var merged = new FrameCapture();
                foreach (var (cap, offset) in caps)
                    foreach (var kv in cap.Frames)
                    {
                        if (!merged.Frames.TryGetValue(kv.Key, out var f)) { merged.Frames[kv.Key] = f = new List<double>(); merged.Times[kv.Key] = new List<double>(); }
                        f.AddRange(kv.Value);
                        merged.Times[kv.Key].AddRange(cap.Times[kv.Key].Select(t => t < 0 ? -1 : t + offset));
                    }
                game = GamePicker.Pick(merged.Frames, game);
                if (game == null) throw new InvalidOperationException("Не нашли игру: ни одна программа не выдавала кадры. Игра должна быть открыта на экране во время замера.");
                var run = new FpsRun { Game = game, When = DateTime.Now, Stats = FrameStats.From(merged.Frames[game].ToArray()) };
                if (passes > 1)
                    run.Passes = caps.Where(c => c.cap.Frames.TryGetValue(game, out var f) && f.Count >= 30)
                                     .Select(c => Math.Round(FrameStats.From(c.cap.Frames[game].ToArray()).AvgFps, 1)).ToList();

                Phase.Text = "Смотрим, что мешало игре…";
                PhaseText.Text = "Пара секунд.";
                Timer.Text = "…";
                var g = game;
                try { run.Thieves = await Task.Run(() => FpsThieves.Analyze(samples, g, merged.Frames[g], merged.Times[g], caps[0].offset + PresentMonSource.WarmupSeconds)); }
                catch (Exception ex) { App.Log(ex); }
                // первый замер игры — «До», следующий — «После»
                bool hasBefore = _runs.Any(r => r.Game == game && r.Label == "До");
                _runs.Insert(0, run);
                Mark(run, hasBefore ? "После" : "До");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                App.Log(ex);
                ShowMessage(ex.Message);
            }
            finally
            {
                RunPanel.Visibility = Visibility.Collapsed;
                SetupPanel.Visibility = Visibility.Visible;
            }
        }

        DispatcherTimer DispatcherTimerFor(int seconds)
        {
            var end = DateTime.Now.AddSeconds(seconds);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            t.Tick += (s, e) => Timer.Text = Math.Max(0, (int)Math.Ceiling((end - DateTime.Now).TotalSeconds)).ToString();
            t.Start();
            return t;
        }

        void OnCancel(object sender, RoutedEventArgs e) => _cancel?.Cancel();

        void ShowMessage(string text)
        {
            Message.Text = text;
            Message.Visibility = Visibility.Visible;
        }

        // ---------- история и сравнение ----------

        void Mark(FpsRun run, string label)
        {
            foreach (var r in _runs.Where(r => r.Game == run.Game && r.Label == label)) r.Label = null; // у игры одно «До» и одно «После»
            run.Label = label;
            Persist();
        }

        void DeleteRun(FpsRun run)
        {
            _runs.Remove(run);
            Persist();
        }

        void Persist()
        {
            SaveRuns();
            Refresh();
        }

        /// <summary>Единственное место, где пишется история замеров: демо-данные (--demo-fps) в файл не попадают никогда.</summary>
        void SaveRuns()
        {
#if DEBUG
            if (Environment.GetCommandLineArgs().Contains("--demo-fps")) return;
#endif
            try { FpsStore.Save(_runs); } catch (Exception ex) { App.Log(ex); }
        }

        (FpsRun before, FpsRun after) Pair()
        {
            var latest = _runs.FirstOrDefault(r => r.Label == "После") ?? _runs.FirstOrDefault();
            if (latest == null) return (null, null);
            return (_runs.FirstOrDefault(r => r.Game == latest.Game && r.Label == "До"),
                    _runs.FirstOrDefault(r => r.Game == latest.Game && r.Label == "После"));
        }

        void Refresh()
        {
            _rows.Clear();
            foreach (var r in _runs) _rows.Add(new RunViewModel(r, Mark, DeleteRun));
            HistoryEmpty.Visibility = _runs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RenderThieves(_runs.FirstOrDefault());
            _ = RenderCommunityAsync(_runs.FirstOrDefault());

            var (before, after) = Pair();
            if (before == null || after == null)
            {
                ComparePanel.Visibility = Visibility.Collapsed;
                return;
            }
            ComparePanel.Visibility = Visibility.Visible;
            ShareResult.Visibility = Visibility.Collapsed;
            CompareTitle.Text = $"{before.Game}: до и после";
            CompareSub.Text = $"{before.When:dd.MM HH:mm} → {after.When:dd.MM HH:mm}";
            CompareGrid.Children.Clear();
            CompareGrid.Children.Add(Tile("Средний FPS", before.Stats.AvgFps, after.Stats.AvgFps, true));
            CompareGrid.Children.Add(Tile("1% low", before.Stats.Low1, after.Stats.Low1, true));
            CompareGrid.Children.Add(Tile("0.1% low", before.Stats.Low01, after.Stats.Low01, true));
            CompareGrid.Children.Add(Tile("Фризы", before.Stats.Stutters, after.Stats.Stutters, false));
        }

        // ---------- твой ПК против похожих ----------

        async Task RenderCommunityAsync(FpsRun run)
        {
            if (!Community.Available || run == null) { CommunityPanel.Visibility = Visibility.Collapsed; return; }
            CommunityPanel.Visibility = Visibility.Visible;
            CommunityNote.Text = "Отправляем только игру, модели процессора и видеокарты, объём памяти, разрешение экрана и FPS — ничего личного. " +
                                 "Настройки графики у всех разные, поэтому сравнение примерное.";
            if (!Community.Consent)
            {
                CommunityText.Text = $"Узнай, сколько FPS в {run.Game} выдают такие же ПК, как твой, — и есть ли куда расти.";
                CommunityText.Foreground = (Brush)FindResource("Muted");
                CommunityButton.Visibility = Visibility.Visible;
                return;
            }
            CommunityButton.Visibility = Visibility.Collapsed;
            CommunityText.Text = "Сравниваем…";
            CommunityText.Foreground = (Brush)FindResource("Muted");
            try
            {
                var (spec, same, sameGpu) = await Task.Run(() =>
                {
                    var hw = Community.Hardware();
                    if (!run.Shared) { Community.Submit(run, hw); run.Shared = true; }
                    var (a, b) = Community.Compare(run.Game, hw);
                    return (hw, a, b);
                });
                SaveRuns();
                var sim = same.Avg.HasValue ? same : sameGpu.Avg.HasValue ? sameGpu : null;
                if (sim == null)
                {
                    CommunityText.Text = $"Похожих замеров {run.Game} пока мало ({Math.Max(same.Count, sameGpu.Count)}) — сравнение появится, когда их наберётся хотя бы 5. Твой замер уже в базе.";
                    return;
                }
                var label = sim == same ? $"{spec.Gpu} + {spec.Cpu}, {spec.Res.Replace("x", "×")}" : $"{spec.Gpu} с любым процессором, {spec.Res.Replace("x", "×")}";
                double diff = Delta(sim.Avg.Value, run.Stats.AvgFps);
                var verdict = diff >= -5
                    ? "Твой ПК выдаёт столько же или больше, чем похожие, — он в отличной форме."
                    : $"Твой ПК отстаёт на {Math.Abs(diff):0}% — значит, есть что улучшать. Посмотри «Что мешало игре» выше и «Диагностику».";
                CommunityText.Text = $"Похожие ПК ({label}), {sim.Count} {Plural(sim.Count, "замер", "замера", "замеров")}: в среднем {sim.Avg:0} FPS, 1% low {sim.Low1:0}.\n" +
                                     $"Твой: {run.Stats.AvgFps:0} FPS, 1% low {run.Stats.Low1:0}. {verdict}";
                CommunityText.Foreground = (Brush)FindResource(diff >= -5 ? "Text" : "Orange");
            }
            catch (Exception ex)
            {
                App.Log(ex);
                CommunityText.Text = "Сервер сравнения сейчас недоступен. Попробуем при следующем замере.";
            }
        }

        async void OnCommunityConsent(object sender, RoutedEventArgs e)
        {
            Community.Consent = true;
            await RenderCommunityAsync(_runs.FirstOrDefault());
        }

        // ---------- кто мешал игре ----------

        void RenderThieves(FpsRun run)
        {
            ThievesList.Children.Clear();
            if (run?.Thieves == null) { ThievesPanel.Visibility = Visibility.Collapsed; return; }
            ThievesPanel.Visibility = Visibility.Visible;
            FixAllResult.Visibility = Visibility.Collapsed;
            UpdateFixAll(run);
            var sub = $"{run.Game}, замер {run.When:dd.MM HH:mm}.";
            if (run.Passes?.Count > 1)
            {
                var spread = run.Spread;
                sub += $" Проходы: {string.Join(" · ", run.Passes.Select(p => p.ToString("0")))} FPS, разброс {spread:0}% — "
                       + (spread <= 5 ? "цифрам можно верить." : "результаты скачут, сравнивай осторожно: замеряй в одном и том же месте игры.");
            }
            foreach (var t in run.Thieves.Where(x => x.Good)) ThievesList.Children.Add(ThiefRow(t));
            if (run.Thieves.All(x => x.Good))
            {
                ThievesTitle.Text = "Игре ничего не мешало";
                ThievesSub.Text = sub + " Фоновые программы почти не тратили процессор и видеокарту, а во время фризов никто не просыпался.";
                return;
            }
            ThievesTitle.Text = "Что мешало игре";
            ThievesSub.Text = sub;
            ThievesList.Children.Clear();
            foreach (var t in run.Thieves) ThievesList.Children.Add(ThiefRow(t));
            UpdateFixAll(run);
        }

        void UpdateFixAll(FpsRun run)
        {
            // «открыть Центр обновления» — не исправление, его в «Исправить всё» не берём
            int n = run?.Thieves?.Count(t => t.Action != null && !t.Action.StartsWith("open:")) ?? 0;
            FixAllButton.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            FixAllText.Text = $"Исправить всё ({n}) и замерить снова";
        }

        /// <summary>Все исправления из карточки разом, затем сразу новый замер — он станет «После», и видно разницу.</summary>
        async void OnFixAllAndRetest(object sender, RoutedEventArgs e)
        {
            var run = _runs.FirstOrDefault();
            var todo = run?.Thieves?.Where(t => t.Action != null && !t.Action.StartsWith("open:")).ToList();
            if (todo == null || todo.Count == 0) return;
            var closing = todo.Where(t => t.Action.StartsWith("close:")).Select(t => "«" + t.Title + "»").ToList();
            if (closing.Count > 0 &&
                MessageBox.Show(Window.GetWindow(this), $"Будут закрыты: {string.Join(", ", closing)}. Несохранённые данные в них пропадут. Продолжить?", "WinOpti",
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            FixAllButton.IsEnabled = RetestButton.IsEnabled = false;
            FixAllText.Text = "Исправляю…";
            var done = new List<string>();
            var failed = new List<string>();
            foreach (var t in todo)
            {
                try
                {
                    var th = t;
                    var result = await Task.Run(() => FpsThieves.Run(th));
                    t.Action = null;
                    if (result != null) t.Advice = result;
                    done.Add(t.Title);
                }
                catch (Exception ex)
                {
                    App.Log(ex);
                    failed.Add($"{t.Title}: {ex.Message}");
                }
            }
            // этот замер — «До», следующий станет «После»: сравнение покажет эффект именно этих исправлений
            Mark(run, "До");
            FixAllButton.IsEnabled = RetestButton.IsEnabled = true;
            FixAllResult.Text = $"Исправлено: {string.Join(", ", done)}." + (failed.Count > 0 ? " Не получилось — " + string.Join("; ", failed) + "." : "") +
                                " Начинаем новый замер — зайди в игру в то же место.";
            FixAllResult.Foreground = (Brush)FindResource(failed.Count == 0 ? "Green" : "Orange");
            FixAllResult.Visibility = Visibility.Visible;
            RenderThieves(run);
            FixAllResult.Visibility = Visibility.Visible;
            OnStart(sender, e);
        }

        UIElement ThiefRow(Thief t)
        {
            var row = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
            text.Children.Add(new TextBlock { Text = t.Title, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });

            var load = new List<string>();
            if (t.Cpu >= 0.5) load.Add($"{t.Cpu:0.#}% процессора");
            if (t.Gpu >= 0.5) load.Add($"{t.Gpu:0.#}% видеокарты");
            if (load.Count > 0)
                text.Children.Add(new TextBlock { Text = "В среднем занимал " + string.Join(" и ", load), FontSize = 12.5, Foreground = (Brush)FindResource("Text"), Margin = new Thickness(0, 3, 0, 0) });
            if (t.Detail != null)
                text.Children.Add(new TextBlock { Text = t.Detail, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource(t.Good ? "Green" : "Red"), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
            if (t.StutterHits > 0)
                text.Children.Add(new TextBlock
                {
                    Text = t.Hardware ? $"Совпало с {t.StutterHits} {Plural(t.StutterHits, "фризом", "фризами", "фризами")}"
                         : t.StutterHits == 1 ? $"Проснулся ровно в момент фриза на {t.FirstStutterAt:0}-й секунде"
                                              : $"Просыпался в моменты {t.StutterHits} {Plural(t.StutterHits, "фриза", "фризов", "фризов")}, первый — на {t.FirstStutterAt:0}-й секунде",
                    FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Orange"), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap,
                });
            var advice = new TextBlock { Text = t.Advice, FontSize = 12.5, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap };
            text.Children.Add(advice);
            row.Children.Add(text);

            if (t.Action != null)
            {
                var button = new Button { Style = (Style)FindResource("GhostButton"), Content = t.ActionText, Padding = new Thickness(14, 7, 14, 7), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(button, 1);
                button.Click += async (s, e) =>
                {
                    if (t.Action.StartsWith("close:") &&
                        MessageBox.Show(Window.GetWindow(this), $"Закрыть «{t.Title}»? Несохранённые данные в этой программе пропадут.", "WinOpti",
                                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    button.IsEnabled = false;
                    try
                    {
                        var th = t;
                        var result = await Task.Run(() => FpsThieves.Run(th));
                        if (t.Action.StartsWith("open:")) { button.IsEnabled = true; return; }
                        t.Action = null; // сделано — кнопку больше не показываем
                        if (result != null) t.Advice = result;
                        advice.Text = t.Advice;
                        advice.Foreground = (Brush)FindResource("Green");
                        row.Children.Remove(button);
                        SaveRuns();
                    }
                    catch (Exception ex)
                    {
                        App.Log(ex);
                        advice.Text = "Не получилось: " + ex.Message;
                        advice.Foreground = (Brush)FindResource("Red");
                        button.IsEnabled = true;
                    }
                };
                row.Children.Add(button);
            }
            return row;
        }

        static string Plural(int n, string one, string few, string many)
        {
            int m10 = n % 10, m100 = n % 100;
            return m10 == 1 && m100 != 11 ? one : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? few : many;
        }

        public static double Delta(double a, double b) => a <= 0 ? 0 : (b / a - 1) * 100;

        /// <summary>Подпись изменения: для FPS — проценты, для фризов — разница в штуках (0 → 5 это не «+0%»).</summary>
        public static string DeltaText(double a, double b, bool count)
        {
            if (count)
            {
                int diff = (int)Math.Round(b - a);
                return diff == 0 ? "без изменений" : (diff > 0 ? "+" : "−") + Math.Abs(diff);
            }
            double d = Delta(a, b);
            return Math.Abs(d) < 0.5 ? "0%" : $"{(d > 0 ? "+" : "−")}{Math.Abs(d):0}%";
        }

        /// <summary>Знак изменения с учётом того, что лучше: больше (FPS) или меньше (фризы). 0 — без изменений.</summary>
        public static int Verdict(double a, double b, bool higherIsBetter, bool count)
        {
            double change = count ? b - a : Delta(a, b);
            if (Math.Abs(change) < (count ? 0.5 : 1)) return 0;
            return (change > 0) == higherIsBetter ? 1 : -1;
        }

        UIElement Tile(string name, double a, double b, bool higherIsBetter)
        {
            bool count = !higherIsBetter;
            int v = Verdict(a, b, higherIsBetter, count);
            var tint = (Brush)FindResource(v == 0 ? "Muted" : v > 0 ? "Green" : "Red");
            var soft = (Brush)FindResource(v == 0 ? "CardAlt" : v > 0 ? "GreenSoft" : "RedSoft");
            var panel = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            panel.Children.Add(new TextBlock { Text = name, FontSize = 12.5, Foreground = (Brush)FindResource("Dim") });
            var nums = new TextBlock { FontSize = 26, FontWeight = FontWeights.Bold, FontFamily = (FontFamily)FindResource("NumFont"), Margin = new Thickness(0, 4, 0, 0) };
            nums.Inlines.Add(new System.Windows.Documents.Run($"{a:0}") { Foreground = (Brush)FindResource("Muted"), FontSize = 18 });
            nums.Inlines.Add(new System.Windows.Documents.Run("  →  ") { Foreground = (Brush)FindResource("Dim"), FontSize = 16 });
            nums.Inlines.Add(new System.Windows.Documents.Run($"{b:0}"));
            panel.Children.Add(nums);
            var pill = new Border { Background = soft, CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 3, 9, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            pill.Child = new TextBlock { Text = DeltaText(a, b, count), Foreground = tint, FontSize = 12.5, FontWeight = FontWeights.SemiBold };
            panel.Children.Add(pill);
            return panel;
        }

        // ---------- картинка результата ----------

        async void OnShare(object sender, RoutedEventArgs e)
        {
            var (before, after) = Pair();
            if (before == null || after == null) return;
            ShareButton.IsEnabled = false;
            try
            {
                string spec = await Task.Run(ShareCard.PcSpec);
                var bmp = ShareCard.Render(before, after, spec);
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WinOpti");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"winopti_{before.Game}_{DateTime.Now:yyyyMMdd_HHmm}.png");
                ShareCard.SavePng(bmp, file);
                Clipboard.SetImage(bmp);
                ShareResult.Text = $"Картинка скопирована — вставь в Telegram или Discord (Ctrl+V). Файл: Изображения\\WinOpti\\{Path.GetFileName(file)}";
                ShareResult.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                App.Log(ex);
                ShowMessage("Не получилось сохранить картинку: " + ex.Message);
            }
            finally { ShareButton.IsEnabled = true; }
        }

#if DEBUG
        static List<FpsRun> DemoRuns()
        {
            // только для проверки вёрстки (запуск с --demo-fps), в файл не пишется
            var rnd = new Random(7);
            double[] Frames(double baseMs, double jitter, int spikes)
            {
                var f = Enumerable.Range(0, 9000).Select(_ => baseMs + rnd.NextDouble() * jitter).ToArray();
                for (int i = 0; i < spikes; i++) f[rnd.Next(f.Length)] = baseMs * 4;
                return f;
            }
            return new List<FpsRun>
            {
                new FpsRun
                {
                    Game = "cs2", When = DateTime.Now.AddMinutes(-3), Label = "После", Stats = FrameStats.From(Frames(4.0, 0.8, 20)),
                    Passes = new List<double> { 271, 277, 274 },
                    Thieves = new List<Thief>
                    {
                        new Thief { Process = "hw:gpu-heat", Title = "Видеокарта перегревалась и сбрасывала частоту", StutterHits = 3,
                                    Detail = "До 87°, частота падала с 1911 до 1607 МГц (−16%), впервые на 41-й секунде",
                                    Advice = "Почисти видеокарту и корпус от пыли, проверь, что корпусные вентиляторы выдувают горячий воздух, или подними обороты вентиляторов видеокарты (MSI Afterburner). Нормально в игре — до 80–83°." },
                        new Thief { Process = "MsMpEng", Title = "Защитник Windows", Cpu = 3.2, StutterHits = 4, FirstStutterAt = 23,
                                    Advice = "Проверял файлы, которые читала игра. Можно добавить папку игры в исключения: Защитник перестанет сканировать её файлы, а остальной компьютер будет защищать как раньше.",
                                    Action = @"defender:D:\Steam\steamapps\common\Counter-Strike Global Offensive", ActionText = "Добавить игру в исключения" },
                        new Thief { Process = "chrome", Title = "Google Chrome", Cpu = 14.1, Gpu = 6.3,
                                    Advice = "Вкладки браузера (особенно видео и стримы) тратят процессор и видеокарту, даже когда окно свёрнуто.", Action = "close:chrome", ActionText = "Закрыть" },
                        new Thief { Process = "SearchIndexer", Title = "Индексация поиска", Cpu = 2.1, StutterHits = 1, FirstStutterAt = 41,
                                    Advice = "Windows перебирала файлы, чтобы поиск работал быстрее. Если пользуешься поиском редко, индексацию можно выключить.",
                                    Action = "tweak:disable_search_indexing", ActionText = "Включить твик" },
                        new Thief { Process = "hw:cpu-ok", Title = "Процессор держал частоту", Detail = "Под нагрузкой — 118% от базовой частоты (работал турбо-режим)", Advice = "Перегрева и ограничения питания не было." },
                    },
                },
                new FpsRun { Game = "cs2", When = DateTime.Now.AddMinutes(-25), Label = "До", Stats = FrameStats.From(Frames(4.3, 1.4, 60)) },
            };
        }
#endif
    }
}
