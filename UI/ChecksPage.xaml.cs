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
            GameModeState.Text = _gameModeOn ? (running ? L.T("включён, работает", "on, running") : L.T("включён, запустится при входе в Windows", "on, starts when you sign in to Windows")) : L.T("выключен", "off");
            GameModeState.Foreground = (Brush)FindResource(_gameModeOn ? "Green" : "Muted");
            GameModeChip.Background = (Brush)FindResource(_gameModeOn ? "GreenSoft" : "CardAlt");
            GameModeButton.Style = (Style)FindResource(_gameModeOn ? "GhostButton" : "AccentButton");
            GameModeButtonText.Text = _gameModeOn ? L.T("Выключить", "Turn off") : L.T("Включить", "Turn on");
            GameModeButtonText.Foreground = (Brush)FindResource(_gameModeOn ? "Text" : "OnAccent");
            GameModeButton.IsEnabled = true;
        }

        async void OnGameMode(object sender, RoutedEventArgs e)
        {
            if (!Sys.SysInfo.IsAdmin)
            {
                ShowGameMode(L.T("Чтобы включить режим, запусти WinOpti от имени администратора: режиму нужно менять план питания и приоритеты программ.", "To turn on the mode, run WinOpti as administrator: it needs to change the power plan and app priorities."), "Orange");
                return;
            }
            GameModeButton.IsEnabled = false;
            bool turnOn = !_gameModeOn;
            try
            {
                await Task.Run(() => { if (turnOn) Game.GameMode.Enable(); else Game.GameMode.Disable(); });
                ShowGameMode(turnOn ? L.T("Готово. Запусти любую игру — режим включится сам. Что он делал, видно в «Восстановлении» → «Журнал».", "Done. Launch any game and the mode kicks in by itself. What it did is shown in Restore → Journal.")
                                    : L.T("Выключено. Всё, что режим менял во время игры, возвращено.", "Turned off. Everything the mode changed during games has been restored."), "Green");
            }
            catch (Exception ex)
            {
                App.Log(ex);
                ShowGameMode(L.T("Не получилось: ", "Failed: ") + ex.Message, "Red");
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
            ("Radeon Anti-Lag", L.T("Вкл", "On"), true,
             L.T("Аналог режима низкой задержки у NVIDIA: процессор не готовит кадры впрок, и выстрел появляется на экране раньше. ", "Like NVIDIA’s low latency mode: the CPU doesn’t queue frames ahead, so your shot shows up on screen sooner. ") +
             L.T("Если в самой игре есть AMD Anti-Lag 2 (например, в CS2) — включай его в настройках игры, он работает точнее.", "If the game itself has AMD Anti-Lag 2 (CS2, for example), turn that on in the game settings: it works more precisely.")),
            ("Radeon Chill", L.T("Выкл", "Off"), false,
             L.T("Снижает FPS, когда ты не двигаешься, ради тишины и меньшего нагрева. В шутерах это даёт просадку как раз в момент, когда начинаешь двигаться.", "Lowers FPS when you’re not moving, for less noise and heat. In shooters that means a dip right when you start moving.")),
            ("Radeon Boost", L.T("Выкл", "Off"), false,
             L.T("Понижает разрешение при резком движении мыши — FPS растёт, но картинка мылится именно тогда, когда нужно разглядеть противника.", "Lowers the resolution on fast mouse movement: FPS goes up, but the picture blurs exactly when you need to spot an enemy.")),
            ("Enhanced Sync", L.T("Выкл", "Off"), false,
             L.T("Убирает разрывы картинки без обычной вертикальной синхронизации, но у многих даёт рывки и мерцание. Если монитор с FreeSync — лучше он.", "Removes tearing without regular V-Sync, but for many people it causes hitches and flicker. If your monitor has FreeSync, use that instead.")),
            ("AMD FreeSync", L.T("Вкл, если монитор поддерживает", "On, if the monitor supports it"), true,
             L.T("Монитор подстраивается под FPS игры: нет разрывов картинки и почти нет добавочной задержки. Включается в разделе «Дисплей».", "The monitor matches the game’s FPS: no tearing and almost no added latency. Turn it on in the Display section.")),
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
                chip.Child = new TextBlock { Text = L.T("Поставь: ", "Set to: ") + value, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource(on ? "Green" : "Muted") };
                head.Children.Add(chip);
                row.Children.Add(head);
                row.Children.Add(new TextBlock { Text = why, FontSize = 12.5, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 18 });
                AmdRows.Children.Add(row);
            }
            if (AmdSoftwarePath() == null)
            {
                AmdOpen.Visibility = Visibility.Collapsed;
                AmdMessage.Text = L.T("AMD Software не найден. Скачай драйвер с сайта amd.com — вместе с ним ставится и AMD Software: Adrenalin Edition.", "AMD Software not found. Download the driver from amd.com: AMD Software: Adrenalin Edition installs with it.");
                AmdMessage.Visibility = Visibility.Visible;
            }
        }

        void OnAmdOpen(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AmdSoftwarePath()) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                AmdMessage.Text = L.T("Не удалось открыть AMD Software: ", "Couldn’t open AMD Software: ") + ex.Message;
                AmdMessage.Visibility = Visibility.Visible;
            }
        }

        int _amdShots;

        async void OnAmdProbe(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            button.IsEnabled = false;
            try
            {
                var file = await Task.Run(Tests.AmdProbe.Snapshot);
                _amdShots++;
                AmdProbeText.Text = _amdShots == 1
                    ? L.T($"Снимок 1 на рабочем столе ({System.IO.Path.GetFileName(file)}). Теперь поменяй Anti-Lag в AMD Software и сделай второй.",
                          $"Snapshot 1 is on the desktop ({System.IO.Path.GetFileName(file)}). Now change Anti-Lag in AMD Software and take the second one.")
                    : L.T($"Снимок {_amdShots} готов. Пришли файлы winopti-amd-*.txt с рабочего стола разработчику — спасибо!",
                          $"Snapshot {_amdShots} is ready. Send the winopti-amd-*.txt files from the desktop to the developer — thanks!");
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{file}\"");
            }
            catch (Exception ex)
            {
                App.Log(ex);
                AmdProbeText.Text = L.T("Не получилось сделать снимок: ", "Couldn’t take the snapshot: ") + ex.Message;
            }
            finally { button.IsEnabled = true; }
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
                Title = L.T("Режим управления электропитанием", "Power management mode"), ButtonText = L.T("Максимальная производительность", "Prefer maximum performance"), LogName = L.T("NVIDIA: максимальная производительность", "NVIDIA: maximum performance"),
                Explain = L.T("Видеокарта держит рабочие частоты и не сбрасывает их между сценами — меньше микрофризов при резкой смене нагрузки. Минус: в простое чуть больше потребление и нагрев.", "The GPU keeps its working clocks and doesn’t drop them between scenes: less micro-stutter on sudden load changes. Downside: slightly more power and heat at idle."),
                Ids = new[] { Tests.NvidiaSettings.PowerMode }, Values = new uint[] { 1 },
                Describe = v => v[0] == 1 ? L.T("Максимальная производительность", "Prefer maximum performance") : v[0] == 0 ? L.T("Адаптивный", "Adaptive") : v[0] == 5 ? L.T("Оптимальное энергопотребление", "Optimal power") : L.T("По умолчанию драйвера", "Driver default"),
                IsGood = v => v[0] == 1,
            },
            new NvOption
            {
                Title = L.T("Режим низкой задержки", "Low latency mode"), ButtonText = L.T("Включить", "Turn on"), LogName = L.T("NVIDIA: режим низкой задержки", "NVIDIA: low latency mode"),
                Explain = L.T("Процессор не готовит кадры впрок, поэтому выстрел и движение мыши появляются на экране раньше. Сильнее всего заметно, когда видеокарта загружена на 100%. ", "The CPU doesn’t queue frames ahead, so shots and mouse movement show up on screen sooner. Most noticeable when the GPU is at 100%. ") +
                          L.T("Если в игре есть NVIDIA Reflex (CS2, Valorant, Fortnite) — включи его в настройках игры, он работает ещё лучше.", "If the game has NVIDIA Reflex (CS2, Valorant, Fortnite), turn it on in the game settings: it works even better."),
                Ids = new[] { Tests.NvidiaSettings.MaxPrerendered }, Values = new uint[] { 1 },
                Describe = v => v[0] == 1 ? L.T("Вкл", "On") : L.T("Выкл (решает игра)", "Off (game decides)"),
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
            text.Children.Add(new TextBlock { Text = L.T("Сейчас: ", "Now: ") + o.Describe(current), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource(good ? "Green" : "Orange"), Margin = new Thickness(0, 3, 0, 0) });
            text.Children.Add(new TextBlock { Text = o.Explain, FontSize = 12.5, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 18 });
            row.Children.Add(text);
            var button = new Button
            {
                Style = (Style)FindResource(good ? "GhostButton" : "SmallAccentButton"), Padding = new Thickness(14, 7, 14, 7), VerticalAlignment = VerticalAlignment.Center,
                Content = good ? L.T("Сбросить по умолчанию", "Reset to default") : o.ButtonText,
            };
            Grid.SetColumn(button, 1);
            button.Click += async (s, e) =>
            {
                if (!Sys.SysInfo.IsAdmin) { ShowNv(L.T("Чтобы менять настройки драйвера, запусти WinOpti от имени администратора.", "Run WinOpti as administrator to change driver settings."), "Orange"); return; }
                button.IsEnabled = false;
                try
                {
                    await Task.Run(() =>
                    {
                        using (var nv = Tests.NvidiaSettings.Open() ?? throw new InvalidOperationException(L.T("драйвер NVIDIA не ответил", "the NVIDIA driver didn’t respond")))
                        {
                            for (int k = 0; k < o.Ids.Length; k++)
                                if (good) nv.Reset(o.Ids[k]); else nv.Set(o.Ids[k], o.Values[k]);
                            nv.Save();
                        }
                    });
                    Tweaks.TweakEngine.LogEntry("nvidia:" + o.Ids[0].ToString("X8"), o.LogName, good ? "off" : "on");
                    ShowNv(good ? L.T("Вернули значение драйвера по умолчанию.", "Restored the driver default.") : L.T("Готово. Перезапусти игру, если она открыта: драйвер применяет настройки при запуске.", "Done. Restart the game if it’s open: the driver applies settings at launch."), "Green");
                }
                catch (Exception ex)
                {
                    App.Log(ex);
                    ShowNv(L.T("Не получилось: ", "Failed: ") + ex.Message, "Red");
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
                GpuSummary.Text = L.T("Не удалось определить видеокарты: ", "Couldn’t detect GPUs: ") + ex.Message;
                return;
            }
            var discrete = gpus.FirstOrDefault(g => !g.Integrated);
            var integrated = gpus.FirstOrDefault(g => g.Integrated);
            ShowAmd(gpus);

            if (monitorOnIgpu != null)
            {
                MonitorWarningText.Text = L.T($"Монитор подключён к разъёму материнской платы ({monitorOnIgpu}), а не к видеокарте {discrete.Name}. ", $"Your monitor is plugged into the motherboard port ({monitorOnIgpu}), not the {discrete.Name} graphics card. ") +
                                          L.T("Так игры теряют FPS: кадры идут через встроенную графику. Переткни кабель монитора в разъём видеокарты — ", "Games lose FPS this way: frames go through the integrated graphics. Move the monitor cable to the graphics card port: ") +
                                          L.T("он на задней стенке ниже, в горизонтальной планке.", "it’s lower on the back panel, in the horizontal bracket.");
                MonitorWarning.Visibility = Visibility.Visible;
            }

            if (gpus.Count == 0) { GpuSummary.Text = L.T("Не удалось определить видеокарту.", "Couldn’t detect the GPU."); return; }
            if (discrete == null)
            {
                GpuSummary.Text = L.T($"Только встроенная графика — {integrated.Name}. Выбирать нечего: для игр это самое слабое место ПК, игровая видеокарта дала бы прирост в разы.", $"Only integrated graphics: {integrated.Name}. Nothing to choose: for games this is the PC’s weakest spot, a gaming GPU would be several times faster.");
                return;
            }
            if (integrated == null)
            {
                GpuSummary.Text = L.T($"Одна видеокарта — {discrete.Name}. Все игры идут через неё, выбирать нечего.", $"One GPU: {discrete.Name}. All games run on it, nothing to choose.") +
                                  (monitorOnIgpu == null ? L.T(" Монитор подключён к ней — всё правильно.", " The monitor is plugged into it, all correct.") : "");
                return;
            }
            GpuSummary.Text = L.T($"Две видеокарты: мощная {discrete.Name} и встроенная {integrated.Name}. Windows иногда запускает игру на встроенной — ", $"Two GPUs: the powerful {discrete.Name} and the integrated {integrated.Name}. Windows sometimes runs a game on the integrated one, ") +
                              L.T("и FPS падает в 2–3 раза. Ниже видно, к какой привязана каждая игра.", "and FPS drops 2–3 times. Below you can see which one each game uses.");
            RenderGames();
        }

        void RenderGames()
        {
            GpuGames.Children.Clear();
            if (_games.Count == 0)
            {
                GpuGames.Children.Add(new TextBlock { Text = L.T("Установленных игр не нашли (ищем в Steam, Epic Games и Riot).", "No installed games found (we look in Steam, Epic Games and Riot)."), FontSize = 13, Foreground = (Brush)FindResource("Dim"), Margin = new Thickness(0, 8, 0, 0) });
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
                var (label, color) = g.Preference == 2 ? (L.T("мощная", "powerful"), "Green") : g.Preference == 1 ? (L.T("встроенная", "integrated"), "Red") : (L.T("решает Windows", "Windows decides"), "Orange");
                var chip = new Border { Background = (Brush)FindResource(color + "Soft"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                chip.Child = new TextBlock { Text = label, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource(color) };
                Grid.SetColumn(chip, 1);
                row.Children.Add(chip);
                if (g.Preference != 2)
                {
                    var b = new Button { Style = (Style)FindResource("GhostButton"), Content = L.T("На мощную", "Use powerful"), Padding = new Thickness(14, 6, 14, 6), VerticalAlignment = VerticalAlignment.Center };
                    var game = g;
                    b.Click += (s, e) => { SetGame(game); RenderGames(); };
                    Grid.SetColumn(b, 2);
                    row.Children.Add(b);
                }
                GpuGames.Children.Add(row);
            }
            int left = _games.Count(g => g.Preference != 2);
            GpuAllButton.Visibility = left > 0 ? Visibility.Visible : Visibility.Collapsed;
            GpuAllText.Text = L.T($"Все игры — на мощную ({left})", $"All games on powerful GPU ({left})");
        }

        void SetGame(InstalledGame g)
        {
            try
            {
                GpuCheck.SetHighPerformance(g);
                Tweaks.TweakEngine.LogEntry("gpu:" + g.Exe, L.T("Игра на мощной видеокарте: ", "Game on powerful GPU: ") + g.Name, "gpu_pref");
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
            MouseButtonText.Text = L.T("Идёт проверка…", "Testing…");
            MouseResult.Visibility = Visibility.Collapsed;
            MousePad.Visibility = Visibility.Visible;
            MouseLive.Text = L.T("Води мышью быстро кругами", "Move your mouse fast in circles");
            MouseHint.Text = L.T("Без остановок, 3 секунды движения. Кнопки нажимать не нужно.", "Don’t stop, 3 seconds of movement. No need to click.");
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
                    if (live > 0) { MouseLive.Text = L.T($"≈ {live:0} Гц", $"≈ {live:0} Hz"); MouseHint.Text = L.T("Продолжай двигать…", "Keep moving…"); }
                    if (moving >= MouseSeconds) break;
                    if (DateTime.UtcNow - started > TimeSpan.FromSeconds(30))
                        throw new TimeoutException(L.T("Мышь почти не двигалась. Нажми «Проверить» и води мышью кругами, не останавливаясь.", "The mouse barely moved. Press Test and move it in circles without stopping."));
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
            MouseButtonText.Text = L.T("Проверить ещё раз", "Test again");
        }

        void ShowMouseResult((double measured, int standard) r)
        {
            MouseResult.Visibility = Visibility.Visible;
            MouseHz.Text = L.T($"{r.standard} Гц", $"{r.standard} Hz");
            string text, color;
            if (r.standard <= 125)
            {
                color = "Red";
                text = L.T("Мало для игр: мышь сообщает о движении раз в 8 мс, прицел запаздывает. Если мышь игровая — включи 1000 Гц в её программе ", "Too low for games: the mouse reports every 8 ms, so your crosshair lags. If it’s a gaming mouse, set 1000 Hz in its software ") +
                       L.T("(Logitech G HUB, Razer Synapse, SteelSeries GG и т. п.) или кнопкой на дне мыши. Обычные офисные мыши быстрее 125 Гц не умеют.", "(Logitech G HUB, Razer Synapse, SteelSeries GG, etc.) or with the button on the bottom. Regular office mice can’t go above 125 Hz.");
            }
            else if (r.standard <= 500)
            {
                color = "Orange";
                text = L.T($"Терпимо, но для игр лучше 1000 Гц: сейчас мышь обновляется раз в {1000.0 / r.standard:0.#} мс. ", $"Acceptable, but 1000 Hz is better for games: right now the mouse updates every {1000.0 / r.standard:0.#} ms. ") +
                       L.T("Если мышь умеет 1000 Гц — включи в её программе или кнопкой на дне мыши.", "If your mouse supports 1000 Hz, set it in its software or with the button on the bottom.");
            }
            else if (r.standard == 1000)
            {
                color = "Green";
                text = L.T("Отлично — это стандарт для игр: мышь сообщает о движении каждую миллисекунду.", "Great, that’s the gaming standard: the mouse reports every millisecond.");
            }
            else
            {
                color = "Green";
                text = L.T("Очень высокая частота — прицел максимально отзывчивый. Если на слабом процессоре в играх бывают просадки FPS, попробуй 1000 Гц: ", "Very high rate: the most responsive aim. If you get FPS dips on a weaker CPU, try 1000 Hz: ") +
                       L.T("высокая частота опроса заметно нагружает процессор.", "a high polling rate puts noticeable load on the CPU.");
            }
            if (r.measured < r.standard * 0.85)
                text += L.T($" Намерили {r.measured:0} Гц: похоже, мышь двигалась медленно. Для точности повтори и води быстрее.", $" Measured {r.measured:0} Hz: looks like the mouse moved slowly. Repeat and move faster for accuracy.");
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
            PingButtonText.Text = L.T("Остановить", "Stop");
            PingProgress.Visibility = Visibility.Visible;
            PingTable.Visibility = Visibility.Collapsed;
            PingVerdict.Visibility = Visibility.Collapsed;
            PingFillScale.ScaleX = 0;
            try
            {
                PingPhase.Text = L.T("Ищем ближайшие серверы Valve…", "Finding the nearest Valve servers…");
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
                if (gateway != null) targets.Add(new PingTarget { Name = L.T("Твой роутер", "Your router"), Address = gateway });
                targets.AddRange(nearest);
                if (nearest.Count == 0) targets.Add(new PingTarget { Name = L.T("Cloudflare (общий интернет)", "Cloudflare (general internet)"), Address = "1.1.1.1" });

                PingPhase.Text = L.T("Пингуем 15 секунд. Не запускай ничего тяжёлого, чтобы замер был честным.", "Pinging for 15 seconds. Don’t start anything heavy so the test is fair.");
                var stats = await Task.Run(() => PingTest.Series(targets, PingCount, PingInterval,
                    p => Dispatcher.BeginInvoke(new Action(() => PingFillScale.ScaleX = p)), token));
                ShowPing(stats, gateway != null, wifi, nearest.Count == 0);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                App.Log(ex);
                PingVerdict.Visibility = Visibility.Visible;
                PingVerdict.Text = L.T("Не получилось: ", "Failed: ") + ex.Message;
                PingVerdict.Foreground = (Brush)FindResource("Red");
            }
            PingProgress.Visibility = Visibility.Collapsed;
            PingButtonText.Text = L.T("Проверить ещё раз", "Test again");
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
            Cell(0, 0, L.T("Куда", "Target"), "Dim", 12);
            Cell(0, 1, L.T("Пинг", "Ping"), "Dim", 12);
            Cell(0, 2, L.T("Скачки", "Jitter"), "Dim", 12);
            Cell(0, 3, L.T("Потери", "Loss"), "Dim", 12);
            for (int i = 0; i < stats.Count; i++)
            {
                var s = stats[i];
                PingTable.RowDefinitions.Add(new RowDefinition());
                bool router = hasGateway && i == 0;
                Cell(i + 1, 0, s.Target.Name, "Text", 14, true);
                if (s.Sent == s.Lost) { Cell(i + 1, 1, L.T("не отвечает", "no reply"), "Red", 14); continue; }
                Cell(i + 1, 1, L.T($"{s.Avg:0} мс", $"{s.Avg:0} ms"), router ? (s.Avg > 10 ? "Orange" : "Green") : s.Avg < 40 ? "Green" : s.Avg < 80 ? "Orange" : "Red", 14, true);
                Cell(i + 1, 2, L.T($"±{s.Jitter:0} мс", $"±{s.Jitter:0} ms"), s.Jitter < 5 ? "Green" : s.Jitter < 15 ? "Orange" : "Red", 14);
                Cell(i + 1, 3, s.Lost == 0 ? L.T("нет", "none") : $"{s.LossPercent:0.#}%", s.Lost == 0 ? "Green" : s.LossPercent < 2 ? "Orange" : "Red", 14);
            }
            PingTable.Visibility = Visibility.Visible;

            var gw = hasGateway ? stats[0] : null;
            var servers = stats.Skip(hasGateway ? 1 : 0).Where(s => s.Sent > s.Lost).ToList();
            var best = servers.OrderBy(s => s.Avg).FirstOrDefault();
            string text, color;
            if (best != null && !noValve && best.Avg < 2)
            {
                color = "Orange";
                text = L.T("Пинг до далёких серверов почти нулевой — так не бывает. Похоже, включён VPN или прокси, и отвечает он, а не серверы. Выключи его и повтори проверку.", "Ping to distant servers is almost zero, which is impossible. Looks like a VPN or proxy is on and it’s answering instead of the servers. Turn it off and test again.");
            }
            else if (gw != null && (gw.Jitter > 5 || gw.Lost > 0 || gw.Avg > 10))
            {
                color = "Red";
                text = wifi
                    ? L.T("Скачки начинаются уже на пути до роутера — виноват Wi-Fi, а не интернет. Подключи ПК кабелем или поставь его ближе к роутеру, без стен между ними. Если нельзя — переключись на Wi-Fi 5 ГГц.", "Jitter starts on the way to your router, so Wi-Fi is to blame, not the internet. Use a cable or move the PC closer to the router with no walls between. If you can’t, switch to 5 GHz Wi-Fi.")
                    : L.T("Скачки начинаются уже на пути до роутера: виновата домашняя сеть. Проверь кабель, перезагрузи роутер и посмотри, не качает ли кто-то в сети торренты или обновления.", "Jitter starts on the way to your router: your home network is to blame. Check the cable, restart the router and see if anyone on the network is downloading torrents or updates.");
            }
            else if (best != null && (best.Jitter > 10 || best.LossPercent >= 1))
            {
                color = "Orange";
                text = L.T("До роутера всё ровно, а скачки дальше — на стороне провайдера. Если так бывает по вечерам, это перегрузка сети провайдера: напиши в поддержку и приложи эти цифры.", "Everything is steady up to the router; the jitter is further out, on your ISP’s side. If it happens in the evenings, the ISP network is congested: contact support and attach these numbers.");
            }
            else if (best != null && best.Avg >= 80)
            {
                color = "Orange";
                text = L.T($"Сеть стабильная, но ближайший сервер Valve далеко ({best.Target.Name}, {best.Avg:0} мс). Это расстояние, а не неисправность — исправить его настройками Windows нельзя.", $"The network is stable, but the nearest Valve server is far away ({best.Target.Name}, {best.Avg:0} ms). That’s distance, not a fault: Windows settings can’t fix it.");
            }
            else
            {
                color = "Green";
                text = L.T("Сеть в порядке: пинг ровный, потерь нет.", "The network is fine: steady ping, no loss.") + (wifi ? L.T(" Ты подключён по Wi-Fi: сейчас стабильно, но для игр кабель всегда надёжнее.", " You’re on Wi-Fi: stable right now, but a cable is always more reliable for games.") : "");
            }
            if (noValve) text += L.T(" Список серверов Valve не загрузился, поэтому мерили до Cloudflare — это общий пинг в интернет.", " The Valve server list didn’t load, so we measured to Cloudflare: that’s your general internet ping.");
            PingVerdict.Text = text;
            PingVerdict.Foreground = (Brush)FindResource(color == "Green" ? "Text" : color);
            PingVerdict.Visibility = Visibility.Visible;
        }
    }
}
