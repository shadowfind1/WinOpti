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
                Title = "Защитник Windows", Defender = true, NoClose = true,
                Advice = "Проверял файлы, которые читала игра. Можно добавить папку игры в исключения: Защитник перестанет сканировать её файлы, а остальной компьютер будет защищать как раньше.",
            }, "MsMpEng", "MpDefenderCoreService", "NisSrv", "MpCmdRun");
            Add(new Known
            {
                Title = "Обновление Windows", NoClose = true, Open = "ms-settings:windowsupdate", OpenText = "Открыть Центр обновления",
                Advice = "Скачивало или устанавливало обновления прямо во время игры. Дай ему закончить до игры или поставь обновления на паузу в Центре обновления.",
            }, "TiWorker", "TrustedInstaller", "MoUsoCoreWorker", "usocoreworker", "wuauclt", "UsoClient", "SIHClient", "WaaSMedicAgent", "mrt");
            Add(new Known
            {
                Title = "Индексация поиска", NoClose = true, Tweak = "disable_search_indexing",
                Advice = "Windows перебирала файлы, чтобы поиск работал быстрее. Если пользуешься поиском редко, индексацию можно выключить.",
            }, "SearchIndexer", "SearchProtocolHost", "SearchFilterHost");
            Add(new Known
            {
                Title = "Телеметрия Windows", NoClose = true, Tweak = "disable_telemetry_tasks",
                Advice = "Windows собирала сведения о компьютере для Microsoft. Для работы ПК это не нужно.",
            }, "CompatTelRunner", "DeviceCensus");
            Add(new Known
            {
                Title = "Отчёты об ошибках Windows", NoClose = true, Tweak = "disable_error_reporting",
                Advice = "Windows собирала отчёт о сбое какой-то программы и отправляла его в Microsoft.",
            }, "WerFault", "wermgr");
            Add(new Known
            {
                Title = "Xbox Game Bar и запись игр", Tweak = "disable_game_dvr",
                Advice = "Фоновая запись последних минут игры. Если не сохраняешь моменты через Win+Alt+G, она не нужна.",
            }, "GameBar", "GameBarFTServer", "bcastdvr");
            Add(new Known
            {
                Title = "Microsoft Edge", Tweak = "disable_edge_background",
                Advice = "Edge работал в фоне, даже если его окна закрыты.",
            }, "msedge");
            Add(new Known
            {
                Title = "Сжатие памяти", NoClose = true,
                Advice = "Windows сжимала оперативную память — значит, её не хватало. Закрывай браузер перед игрой; если такое постоянно, стоит добавить памяти.",
            }, "Memory Compression");
            Add(new Known
            {
                Title = "Драйверы и ядро Windows", NoClose = true,
                Advice = "Процессор занимали драйверы. Чаще всего это драйвер сети, звука или устаревший драйвер видеокарты — обнови их с сайта производителя.",
            }, "System");
            Add(new Known { Title = "Google Chrome", Advice = "Вкладки браузера (особенно видео и стримы) тратят процессор и видеокарту, даже когда окно свёрнуто." }, "chrome");
            Add(new Known { Title = "Яндекс Браузер", Advice = "Вкладки браузера (особенно видео и стримы) тратят процессор и видеокарту, даже когда окно свёрнуто." }, "browser");
            Add(new Known { Title = "Opera", Advice = "Вкладки браузера тратят процессор и видеокарту, даже когда окно свёрнуто." }, "opera");
            Add(new Known { Title = "Firefox", Advice = "Вкладки браузера тратят процессор и видеокарту, даже когда окно свёрнуто." }, "firefox");
            Add(new Known { Title = "Discord", Advice = "Оверлей и аппаратное ускорение Discord нагружают систему. В настройках Discord можно выключить оверлей в игре." }, "Discord");
            Add(new Known { Title = "OBS", Advice = "Запись или стрим. Если ничего не записывал, закрой OBS перед игрой." }, "obs64");
            Add(new Known { Title = "Steam (магазин и чат)", Advice = "Встроенный браузер Steam: окна магазина, библиотеки и чата. Сверни Steam в трей, а не держи открытым." }, "steamwebhelper");
            Add(new Known { Title = "Живые обои", Advice = "Анимированные обои рисуются постоянно и отнимают видеокарту у игры." }, "wallpaper32", "wallpaper64", "Lively", "livelywpf");
            Add(new Known { Title = "OneDrive", Advice = "Синхронизировал файлы во время игры." }, "OneDrive");
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
                        Process = "hw:gpu-heat", Title = "Видеокарта перегревалась и сбрасывала частоту",
                        Detail = $"До {maxT}°, частота падала с {peak:0} до {low:0} МГц (−{drop:0}%), впервые на {FirstAt(s => hot(s) || s.GpuSensors.Value.TempC >= 85):0}-й секунде",
                        StutterHits = HitsWhere(hot),
                        Advice = "Почисти видеокарту и корпус от пыли, проверь, что корпусные вентиляторы выдувают горячий воздух, или подними обороты вентиляторов видеокарты (MSI Afterburner). Нормально в игре — до 80–83°.",
                    });
                else if (maxT >= 83)
                    found.Add(new Thief
                    {
                        Process = "hw:gpu-warm", Title = "Видеокарта на грани перегрева",
                        Detail = $"До {maxT}° — частоту пока не сбрасывала, но запаса почти нет",
                        Advice = "Летом или в более тяжёлой игре начнутся сбросы частоты. Стоит почистить от пыли и проверить продув корпуса.",
                    });

                Func<LoadSample, bool> weak = s => (s.GpuSensors.Value.Reasons & Tests.NvidiaSettings.Throttle.InsufficientPower) != 0;
                if (gpu.Any(weak))
                    found.Add(new Thief
                    {
                        Process = "hw:gpu-power", Title = "Видеокарте не хватало питания",
                        Detail = "Драйвер сообщил о недостаточном питании", StutterHits = HitsWhere(weak),
                        Advice = "Проверь, что все разъёмы питания видеокарты подключены отдельными кабелями от блока питания (не одним кабелем-«раздвойкой»), и что мощности блока хватает.",
                    });
                if (gpu.Any(s => (s.GpuSensors.Value.Reasons & Tests.NvidiaSettings.Throttle.Battery) != 0))
                    found.Add(new Thief
                    {
                        Process = "hw:battery", Title = "Ноутбук играл от батареи",
                        Detail = "От батареи видеокарта работает в урезанном режиме",
                        Advice = "Подключи зарядку: от сети видеокарта ноутбука выдаёт в разы больше FPS.",
                    });
                if (!found.Any() && maxT > 0)
                    found.Add(new Thief
                    {
                        Process = "hw:gpu-ok", Title = "Видеокарта в норме",
                        Detail = $"До {maxT}°, частота держалась {low:0}–{peak:0} МГц",
                        Advice = "Перегрева, сброса частоты и нехватки питания не было.",
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
                        Process = "hw:cpu-throttle", Title = "Процессор сбрасывал частоту под нагрузкой",
                        Detail = $"Частота проседала до {bottom / top * 100:0}% от рабочей, впервые на {FirstAt(slow):0}-й секунде",
                        StutterHits = HitsWhere(slow),
                        Advice = laptop
                            ? "У ноутбуков это обычно перегрев или режим экономии: играй от розетки, на подставке с хорошим продувом, и поставь режим «Максимальная производительность» в фирменной программе ноутбука."
                            : "Обычно это перегрев (пыль, высохшая термопаста, слабый кулер) или ограничение питания в BIOS. Проверь, что кулер процессора крутится и не забит пылью.",
                    });
                }
                else if (top > 0)
                    found.Add(new Thief
                    {
                        Process = "hw:cpu-ok", Title = "Процессор держал частоту",
                        Detail = $"Под нагрузкой — {top:0}% от базовой частоты" + (top > 102 ? " (работал турбо-режим)" : ""),
                        Advice = "Перегрева и ограничения питания не было.",
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
                    Process = "hw:hdd", Title = "Игра установлена на жёсткий диск (HDD)",
                    Detail = "Диск " + Path.GetPathRoot(gamePath).TrimEnd('\\') + " — механический, он в 10–50 раз медленнее SSD",
                    Advice = "Отсюда долгие загрузки и фризы, когда игра подгружает новые текстуры. Перенеси игру на SSD: Steam → игра → Свойства → Установленные файлы → Переместить папку установки.",
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
                    if (tw != null && tw.Supported && TweakEngine.GetState(tw) != TweakState.On) { t.Action = "tweak:" + tw.Id; t.ActionText = "Включить твик"; }
                }
                else if (k?.Defender == true)
                {
                    paths.TryGetValue(game, out var gameExe);
                    var folder = GameFolder(gameExe);
                    if (folder != null) { t.Action = "defender:" + folder; t.ActionText = "Добавить игру в исключения"; }
                }
                else if (k?.Open != null) { t.Action = "open:" + k.Open; t.ActionText = k.OpenText; }

                if (t.Action == null && exe != null && startup != null)
                {
                    var item = startup.FirstOrDefault(s => s.Enabled && s.Target.Equals(exe, StringComparison.OrdinalIgnoreCase));
                    if (item != null) { t.Action = "startup:" + item.Id; t.ActionText = "Убрать из автозагрузки"; }
                }
                bool systemExe = exe == null || exe.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase);
                if (t.Action == null && k?.NoClose != true && !systemExe) { t.Action = "close:" + t.Process; t.ActionText = "Закрыть"; }
                if (t.Advice == null)
                    t.Advice = systemExe ? "Служебный процесс Windows." : "Программа работала в фоне во время игры. Если она не нужна, закрывай её перед игрой.";
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
                    return $"Твик «{tw.Name}» включён." + (warn != null ? " " + warn : "");
                case "startup":
                    var item = StartupManager.List().items.FirstOrDefault(s => s.Id == arg) ?? throw new InvalidOperationException("запись автозагрузки не найдена");
                    StartupManager.SetEnabled(item, false);
                    return "Больше не запускается вместе с Windows.";
                case "defender":
                    var (code, output) = SysInfo.Run("powershell", $"-NoProfile -Command \"$ErrorActionPreference='Stop'; Add-MpPreference -ExclusionPath '{arg.Replace("'", "''")}'\"");
                    if (code != 0) throw new InvalidOperationException(output.Trim().Split('\n')[0]);
                    TweakEngine.LogEntry("defender_exclusion", "Исключение Защитника: " + arg, "defender_exclusion");
                    return $"Папка {arg} добавлена в исключения Защитника.";
                case "close":
                    int n = 0;
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName(arg))
                        try { p.Kill(); n++; } catch { } finally { p.Dispose(); }
                    return n > 0 ? "Закрыто." : "Программа уже закрыта.";
                case "open":
                    System.Diagnostics.Process.Start(new ProcessStartInfo(arg) { UseShellExecute = true });
                    return null;
            }
            return null;
        }
    }
}
