using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using WinOpti.Sys;

namespace WinOpti.Diagnostics
{
    /// <summary>
    /// Проверки «Диагностики ПК для игр». Ищем не абстрактные твики, а реальные ошибки настройки,
    /// которые съедают FPS и плавность: монитор не на своей частоте, память медленнее, чем может,
    /// план питания, VBS, фоновая нагрузка. Каждая проверка только читает систему; менять что-то
    /// может лишь Fix, и только по кнопке.
    /// </summary>
    public static class Checks
    {
        public sealed class Check
        {
            public string Name;   // что проверяем — показывается, пока идёт проверка
            public Func<IEnumerable<Finding>> Run;
        }

        public static readonly List<Check> All = new List<Check>
        {
            new Check { Name = L.T("Частота монитора", "Monitor refresh rate"), Run = Monitors },
            new Check { Name = L.T("Оперативная память", "Memory (RAM)"), Run = Memory },
            new Check { Name = L.T("План питания", "Power plan"), Run = PowerPlan },
            new Check { Name = L.T("Защита VBS / HVCI", "VBS / HVCI protection"), Run = Vbs },
            new Check { Name = L.T("Игровой режим", "Game Mode"), Run = GameMode },
            new Check { Name = L.T("Фоновая запись Xbox", "Xbox background recording"), Run = GameDvr },
            new Check { Name = L.T("Ускорение мыши", "Mouse acceleration"), Run = MouseAccel },
            new Check { Name = L.T("Аппаратное планирование GPU", "Hardware GPU scheduling"), Run = Hags },
            new Check { Name = L.T("Системный диск", "System drive"), Run = SystemDisk },
            new Check { Name = L.T("Диск с играми", "Games drive"), Run = GamesDisk },
            new Check { Name = L.T("Драйвер видеокарты", "GPU driver"), Run = GpuDriver },
            new Check { Name = L.T("Фоновая нагрузка", "Background load"), Run = Background },
            new Check { Name = L.T("Ноутбук и питание", "Laptop power"), Run = Battery },
            new Check { Name = L.T("Время без перезагрузки", "Time since restart"), Run = Uptime },
        };

        static IEnumerable<Finding> One(Finding f) { yield return f; }

        // ---------- монитор ----------

        static IEnumerable<Finding> Monitors()
        {
            var displays = Displays.All();
            int n = 0;
            foreach (var d in displays)
            {
                n++;
                string name = displays.Count > 1 ? L.T($"Монитор {n}", $"Monitor {n}") : L.T("Монитор", "Monitor");
                if (d.Hz >= d.MaxHz || d.MaxHz <= 60 && d.Hz >= 59)
                {
                    yield return Finding.Ok("hz" + n, L.T($"{name} работает на максимуме: {d.Hz} Гц", $"{name} runs at its maximum: {d.Hz} Hz"),
                        L.T($"{d.Width}×{d.Height}, это самая высокая частота, которую он отдаёт на этом разрешении.", $"{d.Width}×{d.Height}, the highest refresh rate it supports at this resolution."));
                    continue;
                }
                var dev = d.Device;
                int max = d.MaxHz;
                yield return new Finding
                {
                    Id = "hz" + n,
                    Severity = max >= d.Hz * 1.5 ? Severity.Critical : Severity.Warn,
                    Title = L.T($"{name} работает на {d.Hz} Гц, а может на {max} Гц", $"{name} runs at {d.Hz} Hz but can do {max} Hz"),
                    Detail = L.T($"Сейчас монитор показывает {d.Hz} кадров в секунду, даже если игра выдаёт больше. ", $"Right now the monitor shows {d.Hz} frames per second, even if the game renders more. ") +
                             L.T($"Windows часто ставит {d.Hz} Гц по умолчанию после установки драйвера или смены кабеля.", $"Windows often falls back to {d.Hz} Hz after a driver install or a cable change."),
                    Impact = L.T($"картинка до {Math.Round((double)max / d.Hz, 1)}× плавнее", $"up to {Math.Round((double)max / d.Hz, 1)}× smoother picture"),
                    FixLabel = L.T($"Включить {max} Гц", $"Set {max} Hz"),
                    Fix = () => Displays.SetHz(dev, max)
                        ? L.T($"Готово: {max} Гц. Если экран мигнул — так и должно быть.", $"Done: {max} Hz. If the screen blinked, that’s expected.")
                        : throw new InvalidOperationException(L.T("Windows не дала сменить частоту. Попробуй другой кабель (DisplayPort) или выставь частоту в панели драйвера.", "Windows refused to change the refresh rate. Try another cable (DisplayPort) or set it in the GPU driver panel.")),
                };
            }
        }

        // ---------- память ----------

        static IEnumerable<Finding> Memory()
        {
            var sticks = SysInfo.Wmi("SELECT Capacity, Speed, ConfiguredClockSpeed, PartNumber, Manufacturer FROM Win32_PhysicalMemory");
            if (sticks.Count == 0) yield break;
            double totalGb = sticks.Sum(s => Convert.ToDouble(s["Capacity"] ?? 0)) / (1L << 30);
            int rated = sticks.Min(s => Convert.ToInt32(s["Speed"] ?? 0));
            int configured = sticks.Min(s => Convert.ToInt32(s["ConfiguredClockSpeed"] ?? 0));
            var parts = sticks.Select(s => ((s["PartNumber"] as string) ?? "").Trim()).Where(p => p.Length > 0).Distinct().ToList();

            if (configured > 0 && rated > 0 && configured < rated - 50)
                yield return new Finding
                {
                    Id = "ram_speed",
                    Severity = Severity.Warn,
                    Title = L.T($"Память работает на {configured} МГц, а рассчитана на {rated}", $"RAM runs at {configured} MHz but is rated for {rated}"),
                    Detail = L.T("Планки умеют быстрее, но материнская плата запустила их на безопасной частоте. ", "The sticks can go faster, but the motherboard started them at a safe speed. ") +
                             L.T("В играх, которые упираются в процессор (CS2, Valorant, Fortnite), это минус к FPS и к 1% low.", "In CPU-bound games (CS2, Valorant, Fortnite) this costs FPS and 1% lows."),
                    Impact = L.T("до +10–20% FPS в CPU-играх", "up to +10–20% FPS in CPU-bound games"),
                    Guide = L.T("1. Перезагрузи ПК и зайди в BIOS (обычно Del или F2 при включении).\n", "1. Restart the PC and enter BIOS (usually Del or F2 at boot).\n") +
                            L.T("2. Найди XMP (Intel) или EXPO / DOCP (AMD) и включи профиль 1.\n", "2. Find XMP (Intel) or EXPO / DOCP (AMD) and enable profile 1.\n") +
                            L.T("3. Сохрани (F10) и перезагрузись. Если ПК не стартует — BIOS сам откатит настройку через пару попыток.", "3. Save (F10) and restart. If the PC doesn’t boot, BIOS reverts the setting by itself after a few tries."),
                };

            if (sticks.Count == 1)
                yield return new Finding
                {
                    Id = "ram_single",
                    Severity = Severity.Warn,
                    Title = L.T("Стоит одна планка памяти — одноканальный режим", "A single RAM stick: single-channel mode"),
                    Detail = L.T($"{totalGb:0} ГБ одной планкой работают в одноканальном режиме: у процессора вдвое меньше пропускная способность памяти.", $"{totalGb:0} GB on one stick runs in single-channel mode: the CPU gets half the memory bandwidth."),
                    Impact = L.T("до +15–30% FPS со второй планкой", "up to +15–30% FPS with a second stick"),
                    Guide = L.T("Докупи вторую такую же планку (тот же объём и частота) и поставь в слот через один — ", "Add a second identical stick (same size and speed) in the slot one over — ") +
                            L.T("обычно это A2 и B2, точнее написано в инструкции к материнской плате.", "usually A2 and B2; the motherboard manual has the exact slots."),
                };
            else if (parts.Count > 1)
                yield return new Finding
                {
                    Id = "ram_mixed",
                    Severity = Severity.Info,
                    Title = L.T("Планки памяти разных моделей", "Mixed RAM models"),
                    Detail = L.T($"Стоят разные модели ({string.Join(", ", parts)}). Они работают на частоте самой медленной ", $"Different models are installed ({string.Join(", ", parts)}). They run at the speed of the slowest one ") +
                             L.T("и иногда не дают включить XMP. Не ошибка, но при апгрейде лучше брать одинаковый комплект.", "and sometimes block XMP. Not an error, but buy a matched kit when you upgrade."),
                };

            if (totalGb < 15)
                yield return new Finding
                {
                    Id = "ram_small",
                    Severity = Severity.Info,
                    Title = L.T($"Всего {totalGb:0} ГБ памяти", $"Only {totalGb:0} GB of RAM"),
                    Detail = L.T("Для современных игр с браузером и Discord в фоне комфортно 16 ГБ. На 8 ГБ Windows начинает ", "Modern games with a browser and Discord in the background want 16 GB. With 8 GB Windows starts ") +
                             L.T("выгружать данные на диск, отсюда фризы.", "paging to disk, which causes stutter."),
                };

            if (configured >= rated - 50 && sticks.Count > 1 && parts.Count <= 1 && totalGb >= 15)
                yield return Finding.Ok("ram", L.T($"Память: {totalGb:0} ГБ, {configured} МГц, два канала", $"RAM: {totalGb:0} GB, {configured} MHz, dual channel"),
                    L.T("Работает на заявленной частоте.", "Runs at its rated speed."));
            else if (configured >= rated - 50 && sticks.Count > 1)
                yield return Finding.Ok("ram", L.T($"Память работает на заявленной частоте: {configured} МГц", $"RAM runs at its rated speed: {configured} MHz"), L.T($"{totalGb:0} ГБ, {sticks.Count} планки.", $"{totalGb:0} GB, {sticks.Count} sticks."));
        }

        // ---------- питание ----------

        static IEnumerable<Finding> PowerPlan()
        {
            var (guid, name) = SysInfo.ActivePowerPlan();
            if (guid == null) yield break;
            bool laptop = SysInfo.IsLaptop;
            if (guid == SysInfo.PlanHigh || guid == SysInfo.PlanUltimate || (name ?? "").IndexOf("Ultimate", StringComparison.OrdinalIgnoreCase) >= 0
                || (name ?? "").IndexOf("макс", StringComparison.OrdinalIgnoreCase) >= 0 || (name ?? "").IndexOf("высок", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                yield return Finding.Ok("power", L.T($"План питания: {name}", $"Power plan: {name}"), L.T("Процессор не сбрасывает частоту посреди игры.", "The CPU won’t drop its clock mid-game."));
                yield break;
            }
            bool saver = guid == SysInfo.PlanPowerSaver;
            yield return new Finding
            {
                Id = "power",
                Severity = saver ? Severity.Critical : (laptop ? Severity.Info : Severity.Warn),
                Title = L.T($"План питания «{name}»", $"Power plan “{name}”"),
                Detail = saver
                    ? L.T("Экономия энергии режет частоту процессора и видеокарты. В играх это прямой минус FPS.", "Power saver cuts CPU and GPU clocks. In games that’s a direct FPS loss.")
                    : L.T("Сбалансированный план опускает частоту процессора, когда нагрузка падает, и поднимает с задержкой — отсюда микрофризы. ", "Balanced lowers the CPU clock when load drops and raises it with a delay, which causes micro-stutter. ") +
                      (laptop ? L.T("На ноутбуке включай высокую производительность только от сети.", "On a laptop, use High performance only when plugged in.") : ""),
                Impact = saver ? L.T("до +30% FPS", "up to +30% FPS") : L.T("ровнее 1% low", "steadier 1% lows"),
                FixLabel = L.T("Высокая производительность", "High performance"),
                FixNeedsAdmin = false,
                Fix = () =>
                {
                    var plans = SysInfo.PowerPlans();
                    string target = plans.Contains(SysInfo.PlanHigh) ? SysInfo.PlanHigh : null;
                    if (target == null)
                    {
                        // на ноутбуках с Modern Standby плана нет в списке — создаём копию
                        var (_, o) = SysInfo.Run("powercfg", "-duplicatescheme " + SysInfo.PlanHigh);
                        var m = System.Text.RegularExpressions.Regex.Match(o, @"[0-9a-f]{8}-[0-9a-f-]{27}");
                        target = m.Success ? m.Value : null;
                    }
                    if (target == null) throw new InvalidOperationException(L.T("Windows не дала создать план «Высокая производительность».", "Windows refused to create the High performance plan."));
                    var (code, _) = SysInfo.Run("powercfg", "/setactive " + target);
                    if (code != 0) throw new InvalidOperationException(L.T("Windows не дала сменить план питания.", "Windows refused to change the power plan."));
                    return L.T("Готово: включена высокая производительность.", "Done: High performance is on.");
                },
            };
        }

        // ---------- VBS ----------

        const string DeviceGuard = @"SYSTEM\CurrentControlSet\Control\DeviceGuard";
        const string Hvci = DeviceGuard + @"\Scenarios\HypervisorEnforcedCodeIntegrity";

        static IEnumerable<Finding> Vbs()
        {
            var dg = SysInfo.Wmi("SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard",
                @"root\Microsoft\Windows\DeviceGuard").FirstOrDefault();
            if (dg == null) yield break;
            int status = Convert.ToInt32(dg["VirtualizationBasedSecurityStatus"] ?? 0);
            var running = (dg["SecurityServicesRunning"] as uint[]) ?? new uint[0];
            bool hvci = running.Contains(2u);
            if (status != 2)
            {
                yield return Finding.Ok("vbs", L.T("Виртуализационная защита (VBS) выключена", "Virtualization-based security (VBS) is off"), L.T("Процессор не тратит ресурсы на изоляцию памяти.", "The CPU doesn’t spend resources on memory isolation."));
                yield break;
            }
            yield return new Finding
            {
                Id = "vbs",
                Severity = hvci ? Severity.Warn : Severity.Info,
                Title = hvci ? L.T("Включена «Целостность памяти» (HVCI)", "Memory integrity (HVCI) is on") : L.T("Работает виртуализационная защита (VBS)", "Virtualization-based security (VBS) is running"),
                Detail = (hvci
                    ? L.T("Windows проверяет каждый драйвер через гипервизор. Microsoft сама советует выключать это на игровых ПК: ", "Windows checks every driver through the hypervisor. Microsoft itself suggests turning this off on gaming PCs: ")
                    : L.T("Windows держит часть системы в изолированной виртуальной среде. Это нагрузка на процессор: ", "Windows keeps part of the system in an isolated virtual environment. That loads the CPU: ")) +
                    L.T("минус несколько процентов FPS, особенно на процессорах старше 2020 года. Защита от редких атак на драйверы при этом слабее — решай сам.", "a few percent less FPS, especially on CPUs older than 2020. Protection against rare driver attacks gets weaker, so it’s your call."),
                Impact = hvci ? L.T("до +5–10% FPS", "up to +5–10% FPS") : L.T("до +5% FPS", "up to +5% FPS"),
                FixLabel = L.T("Выключить", "Turn off"),
                FixNeedsAdmin = true,
                FixNeedsReboot = true,
                Fix = () =>
                {
                    Reg.SetDword(RegistryHive.LocalMachine, Hvci, "Enabled", 0);
                    Reg.SetDword(RegistryHive.LocalMachine, DeviceGuard, "EnableVirtualizationBasedSecurity", 0);
                    return L.T("Готово, перезагрузи ПК. Если VBS остался — его держит Hyper-V, WSL или эмулятор Android: они без него не работают.", "Done, restart the PC. If VBS is still on, Hyper-V, WSL or an Android emulator keeps it: they need it to work.");
                },
            };
        }

        // ---------- игровые настройки Windows ----------

        static IEnumerable<Finding> GameMode()
        {
            var v = Reg.GetInt(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled");
            if (v != 0)
                return One(Finding.Ok("gamemode", L.T("Игровой режим включён", "Game Mode is on"), L.T("Windows отдаёт игре приоритет и не ставит обновления во время игры.", "Windows prioritizes the game and doesn’t install updates while you play.")));
            return One(new Finding
            {
                Id = "gamemode",
                Severity = Severity.Warn,
                Title = L.T("Игровой режим выключен", "Game Mode is off"),
                Detail = L.T("Без него Windows может запускать обновления и фоновые задачи прямо во время игры.", "Without it Windows may run updates and background tasks right while you play."),
                Impact = L.T("меньше фризов", "less stutter"),
                FixLabel = L.T("Включить", "Turn on"),
                Fix = () =>
                {
                    Reg.SetDword(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1);
                    Reg.SetDword(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1);
                    return L.T("Готово: игровой режим включён.", "Done: Game Mode is on.");
                },
            });
        }

        static IEnumerable<Finding> GameDvr()
        {
            var capture = Reg.GetInt(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
            var dvr = Reg.GetInt(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled");
            if (capture == 0 || dvr == 0)
                return One(Finding.Ok("dvr", L.T("Фоновая запись Xbox выключена", "Xbox background recording is off"), L.T("Игры не пишутся на диск в фоне.", "Games aren’t recorded to disk in the background.")));
            return One(new Finding
            {
                Id = "dvr",
                Severity = Severity.Warn,
                Title = L.T("Xbox Game Bar записывает игры в фоне", "Xbox Game Bar records games in the background"),
                Detail = L.T("Фоновая запись постоянно кодирует видео, чтобы можно было сохранить последние минуты. Это нагрузка на видеокарту, даже если ты ничего не сохраняешь.", "Background recording constantly encodes video so you can save the last minutes. It loads the GPU even if you never save anything."),
                Impact = L.T("до +3–5% FPS", "up to +3–5% FPS"),
                FixLabel = L.T("Выключить запись", "Turn off recording"),
                Fix = () =>
                {
                    Reg.SetDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
                    Reg.SetDword(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0);
                    return L.T("Готово: фоновая запись выключена.", "Done: background recording is off.");
                },
            });
        }

        static IEnumerable<Finding> MouseAccel()
        {
            var speed = Reg.Get(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed") as string;
            if (speed == "0")
                return One(Finding.Ok("mouse", L.T("Ускорение мыши выключено", "Mouse acceleration is off"), L.T("Прицел двигается ровно на столько, на сколько двинул мышь.", "Your crosshair moves exactly as far as your mouse does.")));
            return One(new Finding
            {
                Id = "mouse",
                Severity = Severity.Warn,
                Title = L.T("Включено ускорение мыши («Повышенная точность»)", "Mouse acceleration is on (“Enhance pointer precision”)"),
                Detail = L.T("Быстрое движение уводит прицел дальше медленного на то же расстояние. Мышечная память не вырабатывается — в шутерах это мимо.", "A fast flick moves the crosshair further than a slow one over the same distance. Muscle memory can’t form, so in shooters you miss."),
                Impact = L.T("стабильный аим", "consistent aim"),
                FixLabel = L.T("Выключить", "Turn off"),
                Fix = () =>
                {
                    Reg.SetString(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", "0");
                    Reg.SetString(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1", "0");
                    Reg.SetString(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2", "0");
                    Native.SystemParametersInfo(Native.SPI_SETMOUSE, 0, new[] { 0, 0, 0 }, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
                    return L.T("Готово: ускорение мыши выключено, работает сразу.", "Done: mouse acceleration is off, works right away.");
                },
            });
        }

        static IEnumerable<Finding> Hags()
        {
            var mode = Reg.GetInt(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
            if (mode == 2)
                return One(Finding.Ok("hags", L.T("Аппаратное планирование GPU включено", "Hardware GPU scheduling is on"), L.T("Видеокарта сама управляет очередью кадров.", "The GPU manages its own frame queue.")));
            if (mode == null) return Enumerable.Empty<Finding>(); // видеокарта или Windows его не поддерживают
            return One(new Finding
            {
                Id = "hags",
                Severity = Severity.Info,
                Title = L.T("Аппаратное планирование GPU выключено", "Hardware GPU scheduling is off"),
                Detail = L.T("Видеокарта поддерживает его. Оно снимает часть работы с процессора и немного снижает задержку. Нужно для DLSS Frame Generation.", "Your GPU supports it. It takes some work off the CPU and slightly lowers latency. Required for DLSS Frame Generation."),
                Impact = L.T("меньше задержка", "lower latency"),
                FixLabel = L.T("Включить", "Turn on"),
                FixNeedsAdmin = true,
                FixNeedsReboot = true,
                Fix = () =>
                {
                    Reg.SetDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2);
                    return L.T("Готово, заработает после перезагрузки.", "Done, takes effect after a restart.");
                },
            });
        }

        // ---------- диск, драйвер, фон ----------

        static IEnumerable<Finding> SystemDisk()
        {
            var sys = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory));
            double freeGb = sys.AvailableFreeSpace / (double)(1L << 30);
            double share = sys.AvailableFreeSpace / (double)sys.TotalSize;
            string letter = sys.Name.TrimEnd('\\');
            if (freeGb < 15 || share < 0.1)
                yield return new Finding
                {
                    Id = "disk",
                    Severity = freeGb < 5 ? Severity.Critical : Severity.Warn,
                    Title = L.T($"На диске {letter} свободно всего {freeGb:0.#} ГБ", $"Only {freeGb:0.#} GB free on {letter}"),
                    Detail = L.T("Windows нужно место под файл подкачки, кэш шейдеров и обновления. Когда диск почти полон, игры начинают фризить при подгрузке.", "Windows needs space for the page file, shader cache and updates. When the drive is almost full, games stutter while loading."),
                    Impact = L.T("меньше фризов", "less stutter"),
                    Guide = L.T("Открой вкладку «Очистка» в WinOpti: временные файлы и кэш обновлений обычно освобождают несколько гигабайт.", "Open the Cleanup tab in WinOpti: temp files and the update cache usually free several gigabytes."),
                };
            else
                yield return Finding.Ok("disk", L.T($"На диске {letter} свободно {freeGb:0} ГБ", $"{freeGb:0} GB free on {letter}"), L.T("Места хватает.", "Plenty of space."));

            var hdd = SysInfo.Wmi("SELECT MediaType FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage")
                .Any(d => Convert.ToInt32(d["MediaType"] ?? 0) == 3);
            var ssd = SysInfo.Wmi("SELECT MediaType FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage")
                .Any(d => Convert.ToInt32(d["MediaType"] ?? 0) == 4);
            if (hdd && !ssd)
                yield return new Finding
                {
                    Id = "hdd",
                    Severity = Severity.Warn,
                    Title = L.T("В ПК только жёсткий диск (HDD)", "This PC has only a hard drive (HDD)"),
                    Detail = L.T("Современные игры подгружают текстуры на ходу. С HDD это долгие загрузки и фризы при входе в новые зоны.", "Modern games stream textures on the fly. On an HDD that means long loads and stutter when entering new areas."),
                    Impact = L.T("загрузки в 3–5× быстрее", "3–5× faster loading"),
                    Guide = L.T("Самый заметный апгрейд за свои деньги — SSD на 500 ГБ–1 ТБ. Поставь на него Windows и игры.", "The most noticeable upgrade for the money is a 500 GB–1 TB SSD. Put Windows and your games on it."),
                };
        }

        /// <summary>Игры на механическом диске, хотя в ПК есть SSD — частая ошибка: Steam ставит игры туда, где больше места.</summary>
        static IEnumerable<Finding> GamesDisk()
        {
            var games = Tests.GpuCheck.Games();
            if (games.Count == 0) yield break;
            var cache = new Dictionary<char, bool?>();
            bool? Hdd(string path)
            {
                var letter = char.ToUpperInvariant(path[0]);
                if (!cache.TryGetValue(letter, out var v)) cache[letter] = v = SysInfo.IsHdd(path);
                return v;
            }
            var onHdd = games.Where(g => Hdd(g.Exe) == true).ToList();
            bool hasSsd = SysInfo.Wmi("SELECT MediaType FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage").Any(d => Convert.ToInt32(d["MediaType"] ?? 0) == 4);
            if (onHdd.Count > 0 && hasSsd)
                yield return new Finding
                {
                    Id = "games_hdd",
                    Severity = Severity.Warn,
                    Title = onHdd.Count == 1 ? L.T($"{onHdd[0].Name} установлена на жёсткий диск (HDD)", $"{onHdd[0].Name} is installed on a hard drive (HDD)") : L.T($"{onHdd.Count} игры установлены на жёсткий диск (HDD)", $"{onHdd.Count} games are installed on a hard drive (HDD)"),
                    Detail = L.T($"{string.Join(", ", onHdd.Select(g => g.Name))}. В ПК есть SSD, но игры лежат на механическом диске: отсюда долгие загрузки и фризы, когда игра подгружает новые текстуры.", $"{string.Join(", ", onHdd.Select(g => g.Name))}. You have an SSD, but the games sit on a mechanical drive: long loads and stutter when the game streams new textures."),
                    Impact = L.T("загрузки в 3–5× быстрее", "3–5× faster loading"),
                    Guide = L.T("Steam: правой кнопкой по игре → Свойства → Установленные файлы → Переместить папку установки → выбери диск с SSD. Epic Games и Riot: переустанови игру на SSD.", "Steam: right-click the game → Properties → Installed Files → Move install folder → pick the SSD. Epic Games and Riot: reinstall the game on the SSD."),
                };
            else if (onHdd.Count == 0)
                yield return Finding.Ok("games_hdd", L.T("Игры на быстром диске", "Games are on a fast drive"), L.T("Все найденные игры лежат на SSD.", "All detected games are on an SSD."));
        }

        static IEnumerable<Finding> GpuDriver()
        {
            foreach (var g in SysInfo.Wmi("SELECT Name, DriverDate, DriverVersion FROM Win32_VideoController"))
            {
                string name = (g["Name"] as string ?? "").Trim();
                if (name.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Базовый", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    yield return new Finding
                    {
                        Id = "gpu_basic",
                        Severity = Severity.Critical,
                        Title = L.T("Не установлен драйвер видеокарты", "No GPU driver installed"),
                        Detail = L.T("Windows использует базовый видеодрайвер: игры идут на минимальной скорости или не запускаются.", "Windows uses the basic display driver: games run at minimum speed or don’t start."),
                        Impact = L.T("игры заработают в полную силу", "games run at full power"),
                        Guide = L.T("Скачай драйвер с сайта производителя видеокарты: nvidia.com, amd.com или intel.com.", "Download the driver from your GPU maker’s site: nvidia.com, amd.com or intel.com."),
                    };
                    continue;
                }
                if (name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) < 0) continue; // встроенную графику не трогаем
                var raw = g["DriverDate"] as string;
                if (raw == null || raw.Length < 8) continue;
                DateTime date;
                try { date = ManagementDate(raw); } catch { continue; }
                int days = (int)(DateTime.Now - date).TotalDays;
                if (days > 240)
                    yield return new Finding
                    {
                        Id = "gpu_driver",
                        Severity = Severity.Info,
                        Title = L.T($"Драйвер видеокарты от {date:dd.MM.yyyy}", $"GPU driver from {date:dd.MM.yyyy}"),
                        Detail = L.T($"{name}: драйверу {days / 30} мес. В новых драйверах — оптимизации под свежие игры и исправления статтеров.", $"{name}: the driver is {days / 30} months old. New drivers bring optimizations for recent games and stutter fixes."),
                        Guide = L.T("Обнови через NVIDIA App / GeForce Experience или AMD Adrenalin, либо скачай с сайта производителя.", "Update via NVIDIA App / GeForce Experience or AMD Adrenalin, or download from the maker’s site."),
                    };
                else
                    yield return Finding.Ok("gpu_driver", L.T($"Драйвер видеокарты свежий: {date:dd.MM.yyyy}", $"GPU driver is recent: {date:dd.MM.yyyy}"), name);
            }
        }

        static DateTime ManagementDate(string wmi) => System.Management.ManagementDateTimeConverter.ToDateTime(wmi);

        static readonly HashSet<string> Harmless = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Idle", "System", "Memory Compression", "dwm", "csrss", "WinOpti", "audiodg", "Registry" };

        static IEnumerable<Finding> Background()
        {
            var hogs = SysInfo.CpuHogs(TimeSpan.FromSeconds(1.5)).Where(h => !Harmless.Contains(h.name) && h.cpu >= 5).Take(4).ToList();
            if (hogs.Count == 0)
            {
                yield return Finding.Ok("bg", L.T("Фон не грузит процессор", "Background isn’t loading the CPU"), L.T("Ни одна программа в фоне не берёт больше 5% процессора.", "No background app uses more than 5% CPU."));
                yield break;
            }
            double total = hogs.Sum(h => h.cpu);
            yield return new Finding
            {
                Id = "bg",
                Severity = total >= 15 ? Severity.Warn : Severity.Info,
                Title = L.T($"Фоновые программы занимают {total:0}% процессора", $"Background apps use {total:0}% CPU"),
                Detail = L.T("Сейчас больше всего грузят: ", "Top users right now: ") + string.Join(", ", hogs.Select(h => $"{h.name} ({h.cpu:0}%)")) +
                         L.T(". Во время игры эти ресурсы отнимаются у неё.", ". During a game these resources are taken from it."),
                Impact = L.T("больше ресурсов игре", "more resources for the game"),
                Guide = L.T("Закрой эти программы перед игрой. Если что-то из них запускается само — выключи во вкладке «Автозагрузка».", "Close these apps before playing. If any of them start on their own, turn them off in the Startup tab."),
            };
        }

        static IEnumerable<Finding> Battery()
        {
            if (!SysInfo.IsLaptop) yield break;
            if (SysInfo.OnBattery)
                yield return new Finding
                {
                    Id = "battery",
                    Severity = Severity.Warn,
                    Title = L.T("Ноутбук работает от батареи", "Laptop is on battery"),
                    Detail = L.T("От батареи процессор и видеокарта ограничены по мощности — в играх FPS может упасть в 2 раза.", "On battery the CPU and GPU are power-limited: FPS in games can drop by half."),
                    Impact = L.T("до 2× FPS от сети", "up to 2× FPS when plugged in"),
                    Guide = L.T("Подключи зарядку перед игрой.", "Plug in the charger before playing."),
                };
            else
                yield return Finding.Ok("battery", L.T("Ноутбук работает от сети", "Laptop is plugged in"), L.T("Процессор и видеокарта получают полную мощность.", "The CPU and GPU get full power."));
        }

        static IEnumerable<Finding> Uptime()
        {
            var up = SysInfo.Uptime;
            if (up.TotalDays < 7)
            {
                yield return Finding.Ok("uptime", L.T("ПК перезагружался недавно", "PC was restarted recently"), L.T("Без перезагрузки ", "Up for ") + (up.TotalDays >= 1 ? L.T($"{up.TotalDays:0} дн.", $"{up.TotalDays:0} d.") : L.T($"{up.TotalHours:0} ч.", $"{up.TotalHours:0} h.")));
                yield break;
            }
            yield return new Finding
            {
                Id = "uptime",
                Severity = Severity.Info,
                Title = L.T($"ПК не перезагружался {up.TotalDays:0} дней", $"PC hasn’t been restarted for {up.TotalDays:0} days"),
                Detail = L.T("Быстрый запуск Windows не сбрасывает систему при выключении. За неделю копятся утечки памяти драйверов и программ.", "Windows Fast Startup doesn’t reset the system on shutdown. Over a week, driver and app memory leaks pile up."),
                Guide = L.T("Перезагрузи ПК перед игровой сессией: именно «Перезагрузка», а не «Завершение работы».", "Restart the PC before a gaming session: use Restart, not Shut down."),
            };
        }
    }
}
