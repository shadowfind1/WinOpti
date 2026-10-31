using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinOpti.Tests;

namespace WinOpti.UI
{
    public partial class ChecksPage : UserControl
    {
        MouseRate _mouse;
        CancellationTokenSource _pingCancel;

        public ChecksPage()
        {
            InitializeComponent();
            ((FrameworkElement)Scroll.Content).RequestBringIntoView += (s, e) => e.Handled = true;
            Unloaded += (s, e) => { _mouse?.Dispose(); _mouse = null; _pingCancel?.Cancel(); };
            IsVisibleChanged += async (s, e) =>
            {
                if (!IsVisible || _gpuLoaded) return;
                _gpuLoaded = true;
                await Task.WhenAll(LoadGpuAsync(), LoadNvidiaAsync(), LoadGameModeAsync());
            };
        }

        // ---------- авто-игровой режим ----------

        bool _gameModeOn;

        async Task LoadGameModeAsync()
        {
            bool running;
            (_gameModeOn, running) = await Task.Run(() => (Game.GameMode.Enabled, Game.GameMode.Running));
            GameModeState.Text = _gameModeOn ? (running ? "включён, работает" : "включён, запустится при входе в Windows") : "выключен";
            GameModeState.Foreground = (Brush)FindResource(_gameModeOn ? "Green" : "Muted");
            GameModeChip.Background = (Brush)FindResource(_gameModeOn ? "GreenSoft" : "CardAlt");
            GameModeButton.Style = (Style)FindResource(_gameModeOn ? "GhostButton" : "AccentButton");
            GameModeButtonText.Text = _gameModeOn ? "Выключить" : "Включить";
            GameModeButtonText.Foreground = (Brush)FindResource(_gameModeOn ? "Text" : "OnAccent");
            GameModeButton.IsEnabled = true;
        }

        async void OnGameMode(object sender, RoutedEventArgs e)
        {
            if (!Sys.SysInfo.IsAdmin)
            {
                ShowGameMode("Чтобы включить режим, запусти WinOpti от имени администратора: режиму нужно менять план питания и приоритеты программ.", "Orange");
                return;
            }
            GameModeButton.IsEnabled = false;
            bool turnOn = !_gameModeOn;
            try
            {
                await Task.Run(() => { if (turnOn) Game.GameMode.Enable(); else Game.GameMode.Disable(); });
                ShowGameMode(turnOn ? "Готово. Запусти любую игру — режим включится сам. Что он делал, видно в «Восстановлении» → «Журнал»."
                                    : "Выключено. Всё, что режим менял во время игры, возвращено.", "Green");
            }
            catch (Exception ex)
            {
                App.Log(ex);
                ShowGameMode("Не получилось: " + ex.Message, "Red");
            }
            await LoadGameModeAsync();
        }

        void ShowGameMode(string text, string color)
        {
            GameModeMessage.Text = text;
            GameModeMessage.Foreground = (Brush)FindResource(color);
            GameModeMessage.Visibility = Visibility.Visible;
        }

        // ---------- драйвер AMD ----------

        // Переключать настройки Radeon напрямую (через ADLX) не берёмся, пока не проверим на живой карте AMD:
        // показываем, что поставить и где, и открываем AMD Software.
        static readonly (string title, string value, bool on, string why)[] AmdTips =
        {
            ("Radeon Anti-Lag", "Вкл", true,
             "Аналог режима низкой задержки у NVIDIA: процессор не готовит кадры впрок, и выстрел появляется на экране раньше. " +
             "Если в самой игре есть AMD Anti-Lag 2 (например, в CS2) — включай его в настройках игры, он работает точнее."),
            ("Radeon Chill", "Выкл", false,
             "Снижает FPS, когда ты не двигаешься, ради тишины и меньшего нагрева. В шутерах это даёт просадку как раз в момент, когда начинаешь двигаться."),
            ("Radeon Boost", "Выкл", false,
             "Понижает разрешение при резком движении мыши — FPS растёт, но картинка мылится именно тогда, когда нужно разглядеть противника."),
            ("Enhanced Sync", "Выкл", false,
             "Убирает разрывы картинки без обычной вертикальной синхронизации, но у многих даёт рывки и мерцание. Если монитор с FreeSync — лучше он."),
            ("AMD FreeSync", "Вкл, если монитор поддерживает", true,
             "Монитор подстраивается под FPS игры: нет разрывов картинки и почти нет добавочной задержки. Включается в разделе «Дисплей»."),
        };

        static string AmdSoftwarePath()
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var p in new[] { @"AMD\CNext\CNext\RadeonSoftware.exe", @"AMD\CNext\CNext\AMDRSServ.exe" })
            {
                var full = System.IO.Path.Combine(pf, p);
                if (System.IO.File.Exists(full)) return full;
            }
            return null;
        }

        void ShowAmd(List<Gpu> gpus)
        {
            var amd = gpus.FirstOrDefault(g => g.Name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0 || g.Name.StartsWith("AMD", StringComparison.OrdinalIgnoreCase));
#if DEBUG
            if (Environment.GetCommandLineArgs().Contains("--fake-amd")) amd = new Gpu { Name = "AMD Radeon RX 7800 XT" }; // проверка вёрстки
#endif
            if (amd == null) return;
            AmdCard.Visibility = Visibility.Visible;
            AmdRows.Children.Clear();
            foreach (var (title, value, on, why) in AmdTips)
            {
                var row = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
                var head = new WrapPanel();
                head.Children.Add(new TextBlock { Text = title, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                var chip = new Border { Background = (Brush)FindResource(on ? "GreenSoft" : "CardAlt"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };
                chip.Child = new TextBlock { Text = "Поставь: " + value, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource(on ? "Green" : "Muted") };
                head.Children.Add(chip);
                row.Children.Add(head);
                row.Children.Add(new TextBlock { Text = why, FontSize = 12.5, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 18 });
                AmdRows.Children.Add(row);
            }
            if (AmdSoftwarePath() == null)
            {
                AmdOpen.Visibility = Visibility.Collapsed;
                AmdMessage.Text = "AMD Software не найден. Скачай драйвер с сайта amd.com — вместе с ним ставится и AMD Software: Adrenalin Edition.";
                AmdMessage.Visibility = Visibility.Visible;
            }
        }

        void OnAmdOpen(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AmdSoftwarePath()) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                AmdMessage.Text = "Не удалось открыть AMD Software: " + ex.Message;
                AmdMessage.Visibility = Visibility.Visible;
            }
        }

        // ---------- драйвер NVIDIA ----------

        sealed class NvOption
        {
            public string Title, Explain, ButtonText, LogName;
            public uint[] Ids;
            public uint[] Values;
            public Func<uint?[], string> Describe;   // текущее состояние словами
            public Func<uint?[], bool> IsGood;
        }

        static readonly NvOption[] NvOptions =
        {
            new NvOption
            {
                Title = "Режим управления электропитанием", ButtonText = "Максимальная производительность", LogName = "NVIDIA: максимальная производительность",
                Explain = "Видеокарта держит рабочие частоты и не сбрасывает их между сценами — меньше микрофризов при резкой смене нагрузки. Минус: в простое чуть больше потребление и нагрев.",
                Ids = new[] { Tests.NvidiaSettings.PowerMode }, Values = new uint[] { 1 },
                Describe = v => v[0] == 1 ? "Максимальная производительность" : v[0] == 0 ? "Адаптивный" : v[0] == 5 ? "Оптимальное энергопотребление" : "По умолчанию драйвера",
                IsGood = v => v[0] == 1,
            },
            new NvOption
            {
                Title = "Режим низкой задержки", ButtonText = "Включить", LogName = "NVIDIA: режим низкой задержки",
                Explain = "Процессор не готовит кадры впрок, поэтому выстрел и движение мыши появляются на экране раньше. Сильнее всего заметно, когда видеокарта загружена на 100%. " +
                          "Если в игре есть NVIDIA Reflex (CS2, Valorant, Fortnite) — включи его в настройках игры, он работает ещё лучше.",
                Ids = new[] { Tests.NvidiaSettings.MaxPrerendered }, Values = new uint[] { 1 },
                Describe = v => v[0] == 1 ? "Вкл" : "Выкл (решает игра)",
                IsGood = v => v[0] == 1,
            },
        };

        async Task LoadNvidiaAsync()
        {
            uint?[][] values;
            try
            {
                values = await Task.Run(() =>
                {
                    using (var nv = Tests.NvidiaSettings.Open())
                        return nv == null ? null : NvOptions.Select(o => o.Ids.Select(id => nv.Get(id).value).ToArray()).ToArray();
                });
            }
            catch (Exception ex) { App.Log(ex); return; }
            if (values == null) return; // не NVIDIA
            NvCard.Visibility = Visibility.Visible;
            NvRows.Children.Clear();
            for (int i = 0; i < NvOptions.Length; i++) NvRows.Children.Add(NvRow(NvOptions[i], values[i]));
        }

        UIElement NvRow(NvOption o, uint?[] current)
        {
            var row = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
            text.Children.Add(new TextBlock { Text = o.Title, FontSize = 14.5, FontWeight = FontWeights.SemiBold });
            bool good = o.IsGood(current);
            text.Children.Add(new TextBlock { Text = "Сейчас: " + o.Describe(current), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource(good ? "Green" : "Orange"), Margin = new Thickness(0, 3, 0, 0) });
            text.Children.Add(new TextBlock { Text = o.Explain, FontSize = 12.5, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 18 });
            row.Children.Add(text);
            var button = new Button
            {
                Style = (Style)FindResource(good ? "GhostButton" : "SmallAccentButton"), Padding = new Thickness(14, 7, 14, 7), VerticalAlignment = VerticalAlignment.Center,
                Content = good ? "Сбросить по умолчанию" : o.ButtonText,
            };
            Grid.SetColumn(button, 1);
            button.Click += async (s, e) =>
            {
                if (!Sys.SysInfo.IsAdmin) { ShowNv("Чтобы менять настройки драйвера, запусти WinOpti от имени администратора.", "Orange"); return; }
                button.IsEnabled = false;
                try
                {
                    await Task.Run(() =>
                    {
                        using (var nv = Tests.NvidiaSettings.Open() ?? throw new InvalidOperationException("драйвер NVIDIA не ответил"))
                        {
                            for (int k = 0; k < o.Ids.Length; k++)
                                if (good) nv.Reset(o.Ids[k]); else nv.Set(o.Ids[k], o.Values[k]);
                            nv.Save();
                        }
                    });
                    Tweaks.TweakEngine.LogEntry("nvidia:" + o.Ids[0].ToString("X8"), o.LogName, good ? "off" : "on");
                    ShowNv(good ? "Вернули значение драйвера по умолчанию." : "Готово. Перезапусти игру, если она открыта: драйвер применяет настройки при запуске.", "Green");
                }
                catch (Exception ex)
                {
                    App.Log(ex);
                    ShowNv("Не получилось: " + ex.Message, "Red");
                }
                await LoadNvidiaAsync();
            };
            row.Children.Add(button);
            return row;
        }

        void ShowNv(string text, string color)
        {
            NvMessage.Text = text;
            NvMessage.Foreground = (Brush)FindResource(color);
            NvMessage.Visibility = Visibility.Visible;
        }

        // ---------- видеокарта для игр ----------

        bool _gpuLoaded;
        List<InstalledGame> _games = new List<InstalledGame>();

        async Task LoadGpuAsync()
        {
            List<Gpu> gpus;
            string monitorOnIgpu;
            try
            {
                (gpus, _games, monitorOnIgpu) = await Task.Run(() =>
                {
                    var g = GpuCheck.Gpus();
                    return (g, GpuCheck.Games(), GpuCheck.MonitorOnIntegrated(g));
                });
            }
            catch (Exception ex)
            {
                App.Log(ex);
                GpuSummary.Text = "Не удалось определить видеокарты: " + ex.Message;
                return;
            }
            var discrete = gpus.FirstOrDefault(g => !g.Integrated);
            var integrated = gpus.FirstOrDefault(g => g.Integrated);
            ShowAmd(gpus);

            if (monitorOnIgpu != null)
            {
                MonitorWarningText.Text = $"Монитор подключён к разъёму материнской платы ({monitorOnIgpu}), а не к видеокарте {discrete.Name}. " +
                                          "Так игры теряют FPS: кадры идут через встроенную графику. Переткни кабель монитора в разъём видеокарты — " +
                                          "он на задней стенке ниже, в горизонтальной планке.";
                MonitorWarning.Visibility = Visibility.Visible;
            }

            if (gpus.Count == 0) { GpuSummary.Text = "Не удалось определить видеокарту."; return; }
            if (discrete == null)
            {
                GpuSummary.Text = $"Только встроенная графика — {integrated.Name}. Выбирать нечего: для игр это самое слабое место ПК, игровая видеокарта дала бы прирост в разы.";
                return;
            }
            if (integrated == null)
            {
                GpuSummary.Text = $"Одна видеокарта — {discrete.Name}. Все игры идут через неё, выбирать нечего." +
                                  (monitorOnIgpu == null ? " Монитор подключён к ней — всё правильно." : "");
                return;
            }
            GpuSummary.Text = $"Две видеокарты: мощная {discrete.Name} и встроенная {integrated.Name}. Windows иногда запускает игру на встроенной — " +
                              "и FPS падает в 2–3 раза. Ниже видно, к какой привязана каждая игра.";
            RenderGames();
        }

        void RenderGames()
        {
            GpuGames.Children.Clear();
            if (_games.Count == 0)
            {
                GpuGames.Children.Add(new TextBlock { Text = "Установленных игр не нашли (ищем в Steam, Epic Games и Riot).", FontSize = 13, Foreground = (Brush)FindResource("Dim"), Margin = new Thickness(0, 8, 0, 0) });
                GpuAllButton.Visibility = Visibility.Collapsed;
                return;
            }
            foreach (var g in _games)
            {
                var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = g.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
                text.Children.Add(new TextBlock { Text = g.Exe, FontSize = 11.5, Foreground = (Brush)FindResource("Dim"), TextTrimming = TextTrimming.CharacterEllipsis });
                row.Children.Add(text);
                var (label, color) = g.Preference == 2 ? ("мощная", "Green") : g.Preference == 1 ? ("встроенная", "Red") : ("решает Windows", "Orange");
                var chip = new Border { Background = (Brush)FindResource(color + "Soft"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                chip.Child = new TextBlock { Text = label, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource(color) };
                Grid.SetColumn(chip, 1);
                row.Children.Add(chip);
                if (g.Preference != 2)
                {
                    var b = new Button { Style = (Style)FindResource("GhostButton"), Content = "На мощную", Padding = new Thickness(14, 6, 14, 6), VerticalAlignment = VerticalAlignment.Center };
                    var game = g;
                    b.Click += (s, e) => { SetGame(game); RenderGames(); };
                    Grid.SetColumn(b, 2);
                    row.Children.Add(b);
                }
                GpuGames.Children.Add(row);
            }
            int left = _games.Count(g => g.Preference != 2);
            GpuAllButton.Visibility = left > 0 ? Visibility.Visible : Visibility.Collapsed;
            GpuAllText.Text = $"Все игры — на мощную ({left})";
        }

        void SetGame(InstalledGame g)
        {
            try
            {
                GpuCheck.SetHighPerformance(g);
                Tweaks.TweakEngine.LogEntry("gpu:" + g.Exe, "Игра на мощной видеокарте: " + g.Name, "gpu_pref");
            }
            catch (Exception ex) { App.Log(ex); }
        }

        void OnGpuAll(object sender, RoutedEventArgs e)
        {
            foreach (var g in _games.Where(g => g.Preference != 2)) SetGame(g);
            RenderGames();
        }

        // ---------- мышь ----------

        const double MouseSeconds = 3;

        async void OnMouseTest(object sender, RoutedEventArgs e)
        {
            if (_mouse == null)
            {
                var src = PresentationSource.FromVisual(Window.GetWindow(this)) as HwndSource;
                if (src == null) return;
                _mouse = new MouseRate(src);
            }
            MouseButton.IsEnabled = false;
            MouseButtonText.Text = "Идёт проверка…";
            MouseResult.Visibility = Visibility.Collapsed;
            MousePad.Visibility = Visibility.Visible;
            MouseLive.Text = "Води мышью быстро кругами";
            MouseHint.Text = "Без остановок, 3 секунды движения. Кнопки нажимать не нужно.";
            MouseFillScale.ScaleX = 0;
            try
            {
                _mouse.Start();
                var started = DateTime.UtcNow;
                // 4 раза в секунду обновляем прогресс и текущую оценку; ждём 3 с чистого движения (паузы не в счёт)
                while (true)
                {
                    await Task.Delay(250);
                    double moving = _mouse.MovingSeconds;
                    MouseFillScale.ScaleX = Math.Min(1, moving / MouseSeconds);
                    var (live, _) = _mouse.Result();
                    if (live > 0) { MouseLive.Text = $"≈ {live:0} Гц"; MouseHint.Text = "Продолжай двигать…"; }
                    if (moving >= MouseSeconds) break;
                    if (DateTime.UtcNow - started > TimeSpan.FromSeconds(30))
                        throw new TimeoutException("Мышь почти не двигалась. Нажми «Проверить» и води мышью кругами, не останавливаясь.");
                }
                _mouse.Stop();
                ShowMouseResult(_mouse.Result());
            }
            catch (Exception ex)
            {
                _mouse.Stop();
                MouseResult.Visibility = Visibility.Visible;
                MouseHz.Text = "";
                MouseVerdict.Text = ex.Message;
                MouseVerdict.Foreground = (Brush)FindResource("Red");
            }
            MousePad.Visibility = Visibility.Collapsed;
            MouseButton.IsEnabled = true;
            MouseButtonText.Text = "Проверить ещё раз";
        }

        void ShowMouseResult((double measured, int standard) r)
        {
            MouseResult.Visibility = Visibility.Visible;
            MouseHz.Text = $"{r.standard} Гц";
            string text, color;
            if (r.standard <= 125)
            {
                color = "Red";
                text = "Мало для игр: мышь сообщает о движении раз в 8 мс, прицел запаздывает. Если мышь игровая — включи 1000 Гц в её программе " +
                       "(Logitech G HUB, Razer Synapse, SteelSeries GG и т. п.) или кнопкой на дне мыши. Обычные офисные мыши быстрее 125 Гц не умеют.";
            }
            else if (r.standard <= 500)
            {
                color = "Orange";
                text = $"Терпимо, но для игр лучше 1000 Гц: сейчас мышь обновляется раз в {1000.0 / r.standard:0.#} мс. " +
                       "Если мышь умеет 1000 Гц — включи в её программе или кнопкой на дне мыши.";
            }
            else if (r.standard == 1000)
            {
                color = "Green";
                text = "Отлично — это стандарт для игр: мышь сообщает о движении каждую миллисекунду.";
            }
            else
            {
                color = "Green";
                text = "Очень высокая частота — прицел максимально отзывчивый. Если на слабом процессоре в играх бывают просадки FPS, попробуй 1000 Гц: " +
                       "высокая частота опроса заметно нагружает процессор.";
            }
            if (r.measured < r.standard * 0.85)
                text += $" Намерили {r.measured:0} Гц: похоже, мышь двигалась медленно. Для точности повтори и води быстрее.";
            MouseVerdict.Text = text;
            MouseVerdict.Foreground = (Brush)FindResource(color == "Green" ? "Text" : color);
            MouseHz.Foreground = (Brush)FindResource(color);
        }

        // ---------- пинг ----------

        const int PingCount = 150, PingInterval = 100; // 15 секунд

        async void OnPingTest(object sender, RoutedEventArgs e)
        {
            if (_pingCancel != null) { _pingCancel.Cancel(); return; }
            _pingCancel = new CancellationTokenSource();
            var token = _pingCancel.Token;
            PingButtonText.Text = "Остановить";
            PingProgress.Visibility = Visibility.Visible;
            PingTable.Visibility = Visibility.Collapsed;
            PingVerdict.Visibility = Visibility.Collapsed;
            PingFillScale.ScaleX = 0;
            try
            {
                PingPhase.Text = "Ищем ближайшие серверы Valve…";
                var (gateway, wifi, adapter) = await Task.Run(PingTest.Connection);
                List<PingTarget> nearest;
                try { nearest = await Task.Run(() => PingTest.Nearest(PingTest.ValveRelays(), 3)); }
                catch (Exception ex)
                {
                    App.Log(ex);
                    nearest = new List<PingTarget>();
                }
                token.ThrowIfCancellationRequested();
                var targets = new List<PingTarget>();
                if (gateway != null) targets.Add(new PingTarget { Name = "Твой роутер", Address = gateway });
                targets.AddRange(nearest);
                if (nearest.Count == 0) targets.Add(new PingTarget { Name = "Cloudflare (общий интернет)", Address = "1.1.1.1" });

                PingPhase.Text = "Пингуем 15 секунд. Не запускай ничего тяжёлого, чтобы замер был честным.";
                var stats = await Task.Run(() => PingTest.Series(targets, PingCount, PingInterval,
                    p => Dispatcher.BeginInvoke(new Action(() => PingFillScale.ScaleX = p)), token));
                ShowPing(stats, gateway != null, wifi, nearest.Count == 0);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                App.Log(ex);
                PingVerdict.Visibility = Visibility.Visible;
                PingVerdict.Text = "Не получилось: " + ex.Message;
                PingVerdict.Foreground = (Brush)FindResource("Red");
            }
            PingProgress.Visibility = Visibility.Collapsed;
            PingButtonText.Text = "Проверить ещё раз";
            _pingCancel = null;
        }

        void ShowPing(List<PingStats> stats, bool hasGateway, bool wifi, bool noValve)
        {
            PingTable.Children.Clear();
            PingTable.RowDefinitions.Clear();
            PingTable.ColumnDefinitions.Clear();
            foreach (var w in new[] { 2.2, 1, 1, 1 }) PingTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
            void Cell(int row, int col, string text, string brush, double size = 13, bool bold = false)
            {
                var tb = new TextBlock { Text = text, FontSize = size, Foreground = (Brush)FindResource(brush), Margin = new Thickness(0, 6, 0, 6), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
                Grid.SetRow(tb, row);
                Grid.SetColumn(tb, col);
                PingTable.Children.Add(tb);
            }
            PingTable.RowDefinitions.Add(new RowDefinition());
            Cell(0, 0, "Куда", "Dim", 12);
            Cell(0, 1, "Пинг", "Dim", 12);
            Cell(0, 2, "Скачки", "Dim", 12);
            Cell(0, 3, "Потери", "Dim", 12);
            for (int i = 0; i < stats.Count; i++)
            {
                var s = stats[i];
                PingTable.RowDefinitions.Add(new RowDefinition());
                bool router = hasGateway && i == 0;
                Cell(i + 1, 0, s.Target.Name, "Text", 14, true);
                if (s.Sent == s.Lost) { Cell(i + 1, 1, "не отвечает", "Red", 14); continue; }
                Cell(i + 1, 1, $"{s.Avg:0} мс", router ? (s.Avg > 10 ? "Orange" : "Green") : s.Avg < 40 ? "Green" : s.Avg < 80 ? "Orange" : "Red", 14, true);
                Cell(i + 1, 2, $"±{s.Jitter:0} мс", s.Jitter < 5 ? "Green" : s.Jitter < 15 ? "Orange" : "Red", 14);
                Cell(i + 1, 3, s.Lost == 0 ? "нет" : $"{s.LossPercent:0.#}%", s.Lost == 0 ? "Green" : s.LossPercent < 2 ? "Orange" : "Red", 14);
            }
            PingTable.Visibility = Visibility.Visible;

            var gw = hasGateway ? stats[0] : null;
            var servers = stats.Skip(hasGateway ? 1 : 0).Where(s => s.Sent > s.Lost).ToList();
            var best = servers.OrderBy(s => s.Avg).FirstOrDefault();
            string text, color;
            if (best != null && !noValve && best.Avg < 2)
            {
                color = "Orange";
                text = "Пинг до далёких серверов почти нулевой — так не бывает. Похоже, включён VPN или прокси, и отвечает он, а не серверы. Выключи его и повтори проверку.";
            }
            else if (gw != null && (gw.Jitter > 5 || gw.Lost > 0 || gw.Avg > 10))
            {
                color = "Red";
                text = wifi
                    ? "Скачки начинаются уже на пути до роутера — виноват Wi-Fi, а не интернет. Подключи ПК кабелем или поставь его ближе к роутеру, без стен между ними. Если нельзя — переключись на Wi-Fi 5 ГГц."
                    : "Скачки начинаются уже на пути до роутера: виновата домашняя сеть. Проверь кабель, перезагрузи роутер и посмотри, не качает ли кто-то в сети торренты или обновления.";
            }
            else if (best != null && (best.Jitter > 10 || best.LossPercent >= 1))
            {
                color = "Orange";
                text = "До роутера всё ровно, а скачки дальше — на стороне провайдера. Если так бывает по вечерам, это перегрузка сети провайдера: напиши в поддержку и приложи эти цифры.";
            }
            else if (best != null && best.Avg >= 80)
            {
                color = "Orange";
                text = $"Сеть стабильная, но ближайший сервер Valve далеко ({best.Target.Name}, {best.Avg:0} мс). Это расстояние, а не неисправность — исправить его настройками Windows нельзя.";
            }
            else
            {
                color = "Green";
                text = "Сеть в порядке: пинг ровный, потерь нет." + (wifi ? " Ты подключён по Wi-Fi: сейчас стабильно, но для игр кабель всегда надёжнее." : "");
            }
            if (noValve) text += " Список серверов Valve не загрузился, поэтому мерили до Cloudflare — это общий пинг в интернет.";
            PingVerdict.Text = text;
            PingVerdict.Foreground = (Brush)FindResource(color == "Green" ? "Text" : color);
            PingVerdict.Visibility = Visibility.Visible;
        }
    }
}
