using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using WinOpti.Sys;
using WinOpti.Startup;
using WinOpti.Tweaks;

namespace WinOpti.Fps
{
    /// <summary>Кто мешал игре во время замера и что с этим сделать.</summary>
    [DataContract]
    public sealed class Thief
    {
        [DataMember] public string Process;     // имя процесса без .exe
        [DataMember] public string Title;       // понятное название: «Защитник Windows», «Google Chrome»
        [DataMember] public double Cpu;         // средняя загрузка процессора за замер, % от всего процессора
        [DataMember] public double Gpu;         // средняя загрузка видеокарты, %
        [DataMember] public int StutterHits;    // во скольких фризах этот процесс как раз проснулся
        [DataMember] public double FirstStutterAt; // секунда замера первого такого фриза
        [DataMember] public string Advice;
        /// <summary>tweak:&lt;id&gt;, startup:&lt;id&gt;, defender:&lt;папка&gt;, close:&lt;процесс&gt;, open:&lt;адрес&gt; или null.</summary>
        [DataMember] public string Action;
        [DataMember] public string ActionText;
        /// <summary>Для находок по железу: что именно случилось («до 86°, частота упала с 1911 до 1620 МГц»).</summary>
        [DataMember] public string Detail;
        public bool Hardware => Process != null && Process.StartsWith("hw:");
        /// <summary>Не проблема, а отчёт «с железом всё в порядке» — чтобы было видно, что проверка была.</summary>
        public bool Good => Hardware && Process.EndsWith("-ok");
    }

    public static class FpsThieves
    {
        // не «воры»: сама система окон, звук игры, замер
        static readonly HashSet<string> Ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WinOpti", "dwm", "csrss", "audiodg", "Registry", "smss", "wininit", "winlogon", "lsass", "services", "fontdrvhost",
            "Secure System", "LsaIso", "PresentMon-2.6.0-x64", "conhost", "ctfmon", "sihost", "taskhostw", "RuntimeBroker",
        };

        sealed class Known
        {
            public string Title, Advice, Tweak, Open, OpenText;
            public bool Defender, NoClose;
        }

        static readonly Dictionary<string, Known> KnownProcs = BuildKnown();

        static Dictionary<string, Known> BuildKnown()
        {
            var d = new Dictionary<string, Known>(StringComparer.OrdinalIgnoreCase);
            void Add(Known k, params string[] names) { foreach (var n in names) d[n] = k; }
            Add(new Known
            {
                Title = L.T("Защитник Windows", "Windows Defender"), Defender = true, NoClose = true,
                Advice = L.T("Проверял файлы, которые читала игра. Можно добавить папку игры в исключения: Защитник перестанет сканировать её файлы, а остальной компьютер будет защищать как раньше.", "Scanned the files the game was reading. You can add the game folder to exclusions: Defender stops scanning its files and keeps protecting the rest of the PC as before."),
            }, "MsMpEng", "MpDefenderCoreService", "NisSrv", "MpCmdRun");
            Add(new Known
            {
                Title = L.T("Обновление Windows", "Windows Update"), NoClose = true, Open = "ms-settings:windowsupdate", OpenText = L.T("Открыть Центр обновления", "Open Windows Update"),
                Advice = L.T("Скачивало или устанавливало обновления прямо во время игры. Дай ему закончить до игры или поставь обновления на паузу в Центре обновления.", "Was downloading or installing updates during your game. Let it finish before playing, or pause updates in Windows Update."),
            }, "TiWorker", "TrustedInstaller", "MoUsoCoreWorker", "usocoreworker", "wuauclt", "UsoClient", "SIHClient", "WaaSMedicAgent", "mrt");
            Add(new Known
            {
                Title = L.T("Индексация поиска", "Search indexing"), NoClose = true, Tweak = "disable_search_indexing",
                Advice = L.T("Windows перебирала файлы, чтобы поиск работал быстрее. Если пользуешься поиском редко, индексацию можно выключить.", "Windows was going through files to speed up search. If you rarely use search, indexing can be turned off."),
            }, "SearchIndexer", "SearchProtocolHost", "SearchFilterHost");
            Add(new Known
            {
                Title = L.T("Телеметрия Windows", "Windows telemetry"), NoClose = true, Tweak = "disable_telemetry_tasks",
                Advice = L.T("Windows собирала сведения о компьютере для Microsoft. Для работы ПК это не нужно.", "Windows was collecting data about your PC for Microsoft. Your PC doesn’t need it to work."),
            }, "CompatTelRunner", "DeviceCensus");
            Add(new Known
            {
                Title = L.T("Отчёты об ошибках Windows", "Windows Error Reporting"), NoClose = true, Tweak = "disable_error_reporting",
                Advice = L.T("Windows собирала отчёт о сбое какой-то программы и отправляла его в Microsoft.", "Windows was building a crash report for some app and sending it to Microsoft."),
            }, "WerFault", "wermgr");
            Add(new Known
            {
                Title = L.T("Xbox Game Bar и запись игр", "Xbox Game Bar and game recording"), Tweak = "disable_game_dvr",
                Advice = L.T("Фоновая запись последних минут игры. Если не сохраняешь моменты через Win+Alt+G, она не нужна.", "Background recording of the last minutes of play. If you don’t save clips with Win+Alt+G, you don’t need it."),
            }, "GameBar", "GameBarFTServer", "bcastdvr");
            Add(new Known
            {
                Title = "Microsoft Edge", Tweak = "disable_edge_background",
                Advice = L.T("Edge работал в фоне, даже если его окна закрыты.", "Edge was running in the background even with its windows closed."),
            }, "msedge");
            Add(new Known
            {
                Title = L.T("Сжатие памяти", "Memory compression"), NoClose = true,
                Advice = L.T("Windows сжимала оперативную память — значит, её не хватало. Закрывай браузер перед игрой; если такое постоянно, стоит добавить памяти.", "Windows was compressing RAM, which means it was running short. Close the browser before playing; if it happens all the time, add more RAM."),
            }, "Memory Compression");
            Add(new Known
            {
                Title = L.T("Драйверы и ядро Windows", "Drivers and Windows kernel"), NoClose = true,
                Advice = L.T("Процессор занимали драйверы. Чаще всего это драйвер сети, звука или устаревший драйвер видеокарты — обнови их с сайта производителя.", "Drivers were using the CPU. Most often it’s the network, audio or an outdated GPU driver: update them from the maker’s site."),
            }, "System");
            Add(new Known { Title = "Google Chrome", Advice = L.T("Вкладки браузера (особенно видео и стримы) тратят процессор и видеокарту, даже когда окно свёрнуто.", "Browser tabs (especially video and streams) use CPU and GPU even when the window is minimized.") }, "chrome");
            Add(new Known { Title = L.T("Яндекс Браузер", "Yandex Browser"), Advice = L.T("Вкладки браузера (особенно видео и стримы) тратят процессор и видеокарту, даже когда окно свёрнуто.", "Browser tabs (especially video and streams) use CPU and GPU even when the window is minimized.") }, "browser");
            Add(new Known { Title = "Opera", Advice = L.T("Вкладки браузера тратят процессор и видеокарту, даже когда окно свёрнуто.", "Browser tabs use CPU and GPU even when the window is minimized.") }, "opera");
            Add(new Known { Title = "Firefox", Advice = L.T("Вкладки браузера тратят процессор и видеокарту, даже когда окно свёрнуто.", "Browser tabs use CPU and GPU even when the window is minimized.") }, "firefox");
            Add(new Known { Title = "Discord", Advice = L.T("Оверлей и аппаратное ускорение Discord нагружают систему. В настройках Discord можно выключить оверлей в игре.", "Discord’s overlay and hardware acceleration load the system. You can turn off the in-game overlay in Discord settings.") }, "Discord");
            Add(new Known { Title = "OBS", Advice = L.T("Запись или стрим. Если ничего не записывал, закрой OBS перед игрой.", "Recording or streaming. If you weren’t recording, close OBS before playing.") }, "obs64");
            Add(new Known { Title = L.T("Steam (магазин и чат)", "Steam (store and chat)"), Advice = L.T("Встроенный браузер Steam: окна магазина, библиотеки и чата. Сверни Steam в трей, а не держи открытым.", "Steam’s built-in browser: the store, library and chat windows. Minimize Steam to the tray instead of keeping it open.") }, "steamwebhelper");
            Add(new Known { Title = L.T("Живые обои", "Live wallpaper"), Advice = L.T("Анимированные обои рисуются постоянно и отнимают видеокарту у игры.", "Animated wallpaper renders all the time and takes GPU away from the game.") }, "wallpaper32", "wallpaper64", "Lively", "livelywpf");
            Add(new Known { Title = "OneDrive", Advice = L.T("Синхронизировал файлы во время игры.", "Was syncing files during your game.") }, "OneDrive");
            return d;
        }

        /// <summary>
        /// samples — нагрузка по процессам (секунды от начала мониторинга), times — моменты кадров игры в тех же секундах.
        /// skipBefore — до этой секунды идёт вход в игру, её не считаем.
        /// </summary>
        public static List<Thief> Analyze(List<LoadSample> samples, string game, List<double> frameMs, List<double> times, double skipBefore)
        {
            if (samples.Count < 3 || frameMs.Count < 30) return new List<Thief>();
            double from = times.Where(t => t >= 0).DefaultIfEmpty(0).Min(), to = times.Max();
            var window = samples.Where(s => s.T >= from && s.T <= to + 1.2).ToList();
            if (window.Count < 2) window = samples;
            int gpuSamples = Math.Max(1, window.Count(s => s.Gpu != null));

            var names = window.SelectMany(s => s.Cpu.Keys.Concat(s.Gpu?.Keys ?? Enumerable.Empty<string>()))
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .Where(n => !Ignore.Contains(n) && !n.Equals(game, StringComparison.OrdinalIgnoreCase))
                              .ToList();
            var avgCpu = names.ToDictionary(n => n, n => window.Sum(s => s.Cpu.TryGetValue(n, out var v) ? v : 0) / window.Count, StringComparer.OrdinalIgnoreCase);
            var avgGpu = names.ToDictionary(n => n, n => window.Sum(s => s.Gpu != null && s.Gpu.TryGetValue(n, out var v) ? v : 0) / gpuSamples, StringComparer.OrdinalIgnoreCase);

            // фризы и кто проснулся в ту же секунду
            var sorted = frameMs.OrderBy(x => x).ToArray();
            double stutterMs = Math.Max(FrameStats.Percentile(sorted, 50) * 2.5, 1000.0 / 30);
            var hits = new Dictionary<string, (int count, double first)>(StringComparer.OrdinalIgnoreCase);
            var stutters = new List<double>();
            double lastStutter = -10;
            for (int i = 0; i < frameMs.Count; i++)
            {
                if (frameMs[i] <= stutterMs || times[i] < 0) continue;
                double at = times[i];
                if (at - lastStutter < 1) continue; // серия подряд идущих долгих кадров — один фриз
                lastStutter = at;
                stutters.Add(at);
                var s = window.FirstOrDefault(x => x.T >= at && x.T - 1.3 < at);
                if (s == null) continue;
                foreach (var n in names)
                {
                    double cpu = s.Cpu.TryGetValue(n, out var c) ? c : 0;
                    double gpu = s.Gpu != null && s.Gpu.TryGetValue(n, out var g) ? g : 0;
                    bool spike = (cpu >= 4 && cpu >= avgCpu[n] * 3 + 1) || (gpu >= 8 && gpu >= avgGpu[n] * 3 + 2);
                    if (!spike) continue;
                    hits.TryGetValue(n, out var h);
                    hits[n] = (h.count + 1, h.count == 0 ? at - skipBefore : h.first);
                }
            }

            var result = new List<Thief>();
            foreach (var n in names)
            {
                hits.TryGetValue(n, out var h);
                double minCpu = n.Equals("System", StringComparison.OrdinalIgnoreCase) ? 4 : 1.5;
                if (avgCpu[n] < minCpu && avgGpu[n] < 3 && h.count == 0) continue;
                result.Add(new Thief { Process = n, Cpu = avgCpu[n], Gpu = avgGpu[n], StutterHits = h.count, FirstStutterAt = Math.Max(0, h.first) });
            }
            result = result.OrderByDescending(t => t.StutterHits * 5 + t.Cpu + t.Gpu).Take(6).ToList();
            // проблемы с железом — первыми, отчёты «всё в порядке» — в конце
            var hw = Hardware(window, stutters, skipBefore);
            result.InsertRange(0, hw.Where(t => !t.Good));
            result.AddRange(hw.Where(t => t.Good));
            Describe(result, game);
            return result;
        }

        /// <summary>Название, совет и действие. Пути процессов берём из WMI, пока они ещё работают.</summary>
        static double Pct(List<double> sorted, double p) => sorted.Count == 0 ? 0 : sorted[(int)Math.Min(sorted.Count - 1, Math.Floor(sorted.Count * p))];

        /// <summary>
        /// Что было с железом: перегрев и сброс частот видеокарты (NVIDIA сообщает причину сама), нехватка питания,
        /// работа от батареи, сброс частоты процессора под нагрузкой.
        /// </summary>
        static List<Thief> Hardware(List<LoadSample> window, List<double> stutters, double skipBefore)
        {
            var found = new List<Thief>();
            int HitsWhere(Func<LoadSample, bool> bad) =>
                stutters.Count(at => window.FirstOrDefault(x => x.T >= at && x.T - 1.3 < at) is LoadSample s && bad(s));
            double FirstAt(Func<LoadSample, bool> bad) => Math.Max(0, (window.FirstOrDefault(bad)?.T ?? skipBefore) - skipBefore);

            // ---- видеокарта (NVIDIA) ----
            var gpu = window.Where(s => s.GpuSensors.HasValue).ToList();
            if (gpu.Count >= 3)
            {
                var temps = gpu.Select(s => s.GpuSensors.Value.TempC).Where(t => t > 0).ToList();
                int maxT = temps.Count > 0 ? temps.Max() : 0;
                var clocks = gpu.Select(s => (double)s.GpuSensors.Value.CoreMhz).Where(c => c > 0).OrderBy(c => c).ToList();
                double peak = Pct(clocks, 0.9), low = Pct(clocks, 0.1);
                double drop = peak > 0 ? (peak - low) / peak * 100 : 0;
                Func<LoadSample, bool> hot = s => (s.GpuSensors.Value.Reasons & Tests.NvidiaSettings.Throttle.Thermal) != 0;
                int thermal = gpu.Count(hot);

                if (thermal >= 2 || (maxT >= 85 && drop >= 10))
                    found.Add(new Thief
                    {
                        Process = "hw:gpu-heat", Title = L.T("Видеокарта перегревалась и сбрасывала частоту", "The GPU overheated and dropped its clock"),
                        Detail = L.T($"До {maxT}°, частота падала с {peak:0} до {low:0} МГц (−{drop:0}%), впервые на {FirstAt(s => hot(s) || s.GpuSensors.Value.TempC >= 85):0}-й секунде", $"Up to {maxT}°, clock fell from {peak:0} to {low:0} MHz (−{drop:0}%), first at second {FirstAt(s => hot(s) || s.GpuSensors.Value.TempC >= 85):0}"),
                        StutterHits = HitsWhere(hot),
                        Advice = L.T("Почисти видеокарту и корпус от пыли, проверь, что корпусные вентиляторы выдувают горячий воздух, или подними обороты вентиляторов видеокарты (MSI Afterburner). Нормально в игре — до 80–83°.", "Clean the dust out of the GPU and case, make sure the case fans exhaust hot air, or raise the GPU fan speed (MSI Afterburner). Up to 80–83° is normal in games."),
                    });
                else if (maxT >= 83)
                    found.Add(new Thief
                    {
                        Process = "hw:gpu-warm", Title = L.T("Видеокарта на грани перегрева", "The GPU is close to overheating"),
                        Detail = L.T($"До {maxT}° — частоту пока не сбрасывала, но запаса почти нет", $"Up to {maxT}°: no clock drops yet, but almost no headroom"),
                        Advice = L.T("Летом или в более тяжёлой игре начнутся сбросы частоты. Стоит почистить от пыли и проверить продув корпуса.", "In summer or a heavier game it will start dropping clocks. Clean out the dust and check case airflow."),
                    });

                Func<LoadSample, bool> weak = s => (s.GpuSensors.Value.Reasons & Tests.NvidiaSettings.Throttle.InsufficientPower) != 0;
                if (gpu.Any(weak))
                    found.Add(new Thief
                    {
                        Process = "hw:gpu-power", Title = L.T("Видеокарте не хватало питания", "The GPU was short on power"),
                        Detail = L.T("Драйвер сообщил о недостаточном питании", "The driver reported insufficient power"), StutterHits = HitsWhere(weak),
                        Advice = L.T("Проверь, что все разъёмы питания видеокарты подключены отдельными кабелями от блока питания (не одним кабелем-«раздвойкой»), и что мощности блока хватает.", "Make sure every GPU power connector has its own cable from the PSU (not one daisy-chained cable), and that the PSU is powerful enough."),
                    });
                if (gpu.Any(s => (s.GpuSensors.Value.Reasons & Tests.NvidiaSettings.Throttle.Battery) != 0))
                    found.Add(new Thief
                    {
                        Process = "hw:battery", Title = L.T("Ноутбук играл от батареи", "The laptop was on battery"),
                        Detail = L.T("От батареи видеокарта работает в урезанном режиме", "On battery the GPU runs in a reduced mode"),
                        Advice = L.T("Подключи зарядку: от сети видеокарта ноутбука выдаёт в разы больше FPS.", "Plug in the charger: a laptop GPU gives several times more FPS on mains power."),
                    });
                if (!found.Any() && maxT > 0)
                    found.Add(new Thief
                    {
                        Process = "hw:gpu-ok", Title = L.T("Видеокарта в норме", "The GPU is fine"),
                        Detail = L.T($"До {maxT}°, частота держалась {low:0}–{peak:0} МГц", $"Up to {maxT}°, clock held at {low:0}–{peak:0} MHz"),
                        Advice = L.T("Перегрева, сброса частоты и нехватки питания не было.", "No overheating, clock drops or power shortage."),
                    });
            }

            // ---- процессор: частота под нагрузкой ----
            // при малой нагрузке ядра снижают частоту сами — это нормально, поэтому смотрим только секунды, где процессор занят
            var busy = window.Where(s => s.CpuPerf.HasValue && s.Cpu.Values.Sum() >= 25).ToList();
            if (busy.Count >= 5)
            {
                var perf = busy.Select(s => s.CpuPerf.Value).OrderBy(v => v).ToList();
                double top = Pct(perf, 0.9), bottom = Pct(perf, 0.1);
                Func<LoadSample, bool> slow = s => s.CpuPerf.HasValue && s.CpuPerf.Value < top * 0.8 && s.Cpu.Values.Sum() >= 25;
                if (top > 0 && bottom < top * 0.8 && busy.Count(slow) >= 3)
                {
                    bool laptop = false;
                    try { laptop = SysInfo.IsLaptop; } catch { }
                    found.Add(new Thief
                    {
                        Process = "hw:cpu-throttle", Title = L.T("Процессор сбрасывал частоту под нагрузкой", "The CPU dropped its clock under load"),
                        Detail = L.T($"Частота проседала до {bottom / top * 100:0}% от рабочей, впервые на {FirstAt(slow):0}-й секунде", $"Clock sagged to {bottom / top * 100:0}% of normal, first at second {FirstAt(slow):0}"),
                        StutterHits = HitsWhere(slow),
                        Advice = laptop
                            ? L.T("У ноутбуков это обычно перегрев или режим экономии: играй от розетки, на подставке с хорошим продувом, и поставь режим «Максимальная производительность» в фирменной программе ноутбука.", "On laptops it’s usually heat or a power-saving mode: play plugged in, on a stand with good airflow, and pick the max performance mode in the laptop’s own control app.")
                            : L.T("Обычно это перегрев (пыль, высохшая термопаста, слабый кулер) или ограничение питания в BIOS. Проверь, что кулер процессора крутится и не забит пылью.", "Usually it’s heat (dust, dried thermal paste, a weak cooler) or a BIOS power limit. Check that the CPU cooler spins and isn’t clogged with dust."),
                    });
                }
                else if (top > 0)
                    found.Add(new Thief
                    {
                        Process = "hw:cpu-ok", Title = L.T("Процессор держал частоту", "The CPU held its clock"),
                        Detail = L.T($"Под нагрузкой — {top:0}% от базовой частоты", $"Under load: {top:0}% of base clock") + (top > 102 ? L.T(" (работал турбо-режим)", " (turbo was working)") : ""),
                        Advice = L.T("Перегрева и ограничения питания не было.", "No overheating or power limits."),
                    });
            }
            return found;
        }

        static void Describe(List<Thief> thieves, string game)
        {
            if (thieves.Count == 0) return;
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in SysInfo.Wmi("SELECT Name, ExecutablePath FROM Win32_Process"))
                {
                    var name = Path.GetFileNameWithoutExtension(p["Name"] as string ?? "");
                    var path = p["ExecutablePath"] as string;
                    if (!string.IsNullOrEmpty(path) && !paths.ContainsKey(name)) paths[name] = path;
                }
            }
            catch { }
            List<StartupItem> startup = null;
            try { startup = StartupManager.List().items; } catch { }
            var tweaks = Tweak.LoadAll();
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            // игра на жёстком диске — долгие загрузки и подгрузочные фризы
            if (paths.TryGetValue(game, out var gamePath) && SysInfo.IsHdd(gamePath) == true)
                thieves.Insert(0, new Thief
                {
                    Process = "hw:hdd", Title = L.T("Игра установлена на жёсткий диск (HDD)", "The game is installed on a hard drive (HDD)"),
                    Detail = L.T("Диск ", "Drive ") + Path.GetPathRoot(gamePath).TrimEnd('\\') + L.T(" — механический, он в 10–50 раз медленнее SSD", " is mechanical, 10–50 times slower than an SSD"),
                    Advice = L.T("Отсюда долгие загрузки и фризы, когда игра подгружает новые текстуры. Перенеси игру на SSD: Steam → игра → Свойства → Установленные файлы → Переместить папку установки.", "That means long loads and stutter when the game streams new textures. Move the game to an SSD: Steam → game → Properties → Installed Files → Move install folder."),
                });

            foreach (var t in thieves.Where(x => !x.Hardware))
            {
                paths.TryGetValue(t.Process, out var exe);
                KnownProcs.TryGetValue(t.Process, out var k);
                t.Title = k?.Title ?? Description(exe) ?? t.Process;
                t.Advice = k?.Advice;

                if (k?.Tweak != null)
                {
                    var tw = tweaks.FirstOrDefault(x => x.Id == k.Tweak);
                    if (tw != null && tw.Supported && TweakEngine.GetState(tw) != TweakState.On) { t.Action = "tweak:" + tw.Id; t.ActionText = L.T("Включить твик", "Enable tweak"); }
                }
                else if (k?.Defender == true)
                {
                    paths.TryGetValue(game, out var gameExe);
                    var folder = GameFolder(gameExe);
                    if (folder != null) { t.Action = "defender:" + folder; t.ActionText = L.T("Добавить игру в исключения", "Add game to exclusions"); }
                }
                else if (k?.Open != null) { t.Action = "open:" + k.Open; t.ActionText = k.OpenText; }

                if (t.Action == null && exe != null && startup != null)
                {
                    var item = startup.FirstOrDefault(s => s.Enabled && s.Target.Equals(exe, StringComparison.OrdinalIgnoreCase));
                    if (item != null) { t.Action = "startup:" + item.Id; t.ActionText = L.T("Убрать из автозагрузки", "Remove from startup"); }
                }
                bool systemExe = exe == null || exe.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase);
                if (t.Action == null && k?.NoClose != true && !systemExe) { t.Action = "close:" + t.Process; t.ActionText = L.T("Закрыть", "Close"); }
                if (t.Advice == null)
                    t.Advice = systemExe ? L.T("Служебный процесс Windows.", "A Windows system process.") : L.T("Программа работала в фоне во время игры. Если она не нужна, закрывай её перед игрой.", "This app ran in the background during your game. If you don’t need it, close it before playing.");
            }
        }

        static string Description(string exe)
        {
            try
            {
                if (exe == null || !File.Exists(exe)) return null;
                var d = FileVersionInfo.GetVersionInfo(exe).FileDescription?.Trim();
                return string.IsNullOrEmpty(d) ? null : d;
            }
            catch { return null; }
        }

        /// <summary>Папка игры для исключения Защитника: папка exe, а у игр Steam (…\steamapps\common\Игра\bin\…) — папка самой игры.</summary>
        static string GameFolder(string gameExe)
        {
            if (string.IsNullOrEmpty(gameExe)) return null;
            var dir = Path.GetDirectoryName(gameExe);
            const string common = @"\steamapps\common\";
            int i = dir.IndexOf(common, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                int end = dir.IndexOf('\\', i + common.Length);
                if (end > 0) dir = dir.Substring(0, end);
            }
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var root = Path.GetPathRoot(dir);
            // исключать системные папки и корень диска нельзя ни в коем случае
            if (dir.StartsWith(win, StringComparison.OrdinalIgnoreCase) || dir.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return null;
            return dir;
        }

        /// <summary>Выполнить действие. -> текст результата.</summary>
        public static string Run(Thief t)
        {
            var kind = t.Action.Substring(0, t.Action.IndexOf(':'));
            var arg = t.Action.Substring(kind.Length + 1);
            switch (kind)
            {
                case "tweak":
                    var tw = Tweak.LoadAll().First(x => x.Id == arg);
                    TweakEngine.EnsureRestorePoint();
                    var warn = TweakEngine.Apply(tw);
                    return L.T($"Твик «{tw.Name}» включён.", $"Tweak “{tw.Name}” is on.") + (warn != null ? " " + warn : "");
                case "startup":
                    var item = StartupManager.List().items.FirstOrDefault(s => s.Id == arg) ?? throw new InvalidOperationException(L.T("запись автозагрузки не найдена", "startup entry not found"));
                    StartupManager.SetEnabled(item, false);
                    return L.T("Больше не запускается вместе с Windows.", "No longer starts with Windows.");
                case "defender":
                    var (code, output) = SysInfo.Run("powershell", $"-NoProfile -Command \"$ErrorActionPreference='Stop'; Add-MpPreference -ExclusionPath '{arg.Replace("'", "''")}'\"");
                    if (code != 0) throw new InvalidOperationException(output.Trim().Split('\n')[0]);
                    TweakEngine.LogEntry("defender_exclusion", L.T("Исключение Защитника: ", "Defender exclusion: ") + arg, "defender_exclusion");
                    return L.T($"Папка {arg} добавлена в исключения Защитника.", $"Folder {arg} added to Defender exclusions.");
                case "close":
                    int n = 0;
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName(arg))
                        try { p.Kill(); n++; } catch { } finally { p.Dispose(); }
                    return n > 0 ? L.T("Закрыто.", "Closed.") : L.T("Программа уже закрыта.", "The app is already closed.");
                case "open":
                    System.Diagnostics.Process.Start(new ProcessStartInfo(arg) { UseShellExecute = true });
                    return null;
            }
            return null;
        }
    }
}
