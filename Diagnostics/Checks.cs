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
            new Check { Name = "Частота монитора", Run = Monitors },
            new Check { Name = "Оперативная память", Run = Memory },
            new Check { Name = "План питания", Run = PowerPlan },
            new Check { Name = "Защита VBS / HVCI", Run = Vbs },
            new Check { Name = "Игровой режим", Run = GameMode },
            new Check { Name = "Фоновая запись Xbox", Run = GameDvr },
            new Check { Name = "Ускорение мыши", Run = MouseAccel },
            new Check { Name = "Аппаратное планирование GPU", Run = Hags },
            new Check { Name = "Системный диск", Run = SystemDisk },
            new Check { Name = "Диск с играми", Run = GamesDisk },
            new Check { Name = "Драйвер видеокарты", Run = GpuDriver },
            new Check { Name = "Фоновая нагрузка", Run = Background },
            new Check { Name = "Ноутбук и питание", Run = Battery },
            new Check { Name = "Время без перезагрузки", Run = Uptime },
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
                string name = displays.Count > 1 ? $"Монитор {n}" : "Монитор";
                if (d.Hz >= d.MaxHz || d.MaxHz <= 60 && d.Hz >= 59)
                {
                    yield return Finding.Ok("hz" + n, $"{name} работает на максимуме: {d.Hz} Гц",
                        $"{d.Width}×{d.Height}, это самая высокая частота, которую он отдаёт на этом разрешении.");
                    continue;
                }
                var dev = d.Device;
                int max = d.MaxHz;
                yield return new Finding
                {
                    Id = "hz" + n,
                    Severity = max >= d.Hz * 1.5 ? Severity.Critical : Severity.Warn,
                    Title = $"{name} работает на {d.Hz} Гц, а может на {max} Гц",
                    Detail = $"Сейчас монитор показывает {d.Hz} кадров в секунду, даже если игра выдаёт больше. " +
                             $"Windows часто ставит {d.Hz} Гц по умолчанию после установки драйвера или смены кабеля.",
                    Impact = $"картинка до {Math.Round((double)max / d.Hz, 1)}× плавнее",
                    FixLabel = $"Включить {max} Гц",
                    Fix = () => Displays.SetHz(dev, max)
                        ? $"Готово: {max} Гц. Если экран мигнул — так и должно быть."
                        : throw new InvalidOperationException("Windows не дала сменить частоту. Попробуй другой кабель (DisplayPort) или выставь частоту в панели драйвера."),
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
                    Title = $"Память работает на {configured} МГц, а рассчитана на {rated}",
                    Detail = "Планки умеют быстрее, но материнская плата запустила их на безопасной частоте. " +
                             "В играх, которые упираются в процессор (CS2, Valorant, Fortnite), это минус к FPS и к 1% low.",
                    Impact = "до +10–20% FPS в CPU-играх",
                    Guide = "1. Перезагрузи ПК и зайди в BIOS (обычно Del или F2 при включении).\n" +
                            "2. Найди XMP (Intel) или EXPO / DOCP (AMD) и включи профиль 1.\n" +
                            "3. Сохрани (F10) и перезагрузись. Если ПК не стартует — BIOS сам откатит настройку через пару попыток.",
                };

            if (sticks.Count == 1)
                yield return new Finding
                {
                    Id = "ram_single",
                    Severity = Severity.Warn,
                    Title = "Стоит одна планка памяти — одноканальный режим",
                    Detail = $"{totalGb:0} ГБ одной планкой работают в одноканальном режиме: у процессора вдвое меньше пропускная способность памяти.",
                    Impact = "до +15–30% FPS со второй планкой",
                    Guide = "Докупи вторую такую же планку (тот же объём и частота) и поставь в слот через один — " +
                            "обычно это A2 и B2, точнее написано в инструкции к материнской плате.",
                };
            else if (parts.Count > 1)
                yield return new Finding
                {
                    Id = "ram_mixed",
                    Severity = Severity.Info,
                    Title = "Планки памяти разных моделей",
                    Detail = $"Стоят разные модели ({string.Join(", ", parts)}). Они работают на частоте самой медленной " +
                             "и иногда не дают включить XMP. Не ошибка, но при апгрейде лучше брать одинаковый комплект.",
                };

            if (totalGb < 15)
                yield return new Finding
                {
                    Id = "ram_small",
                    Severity = Severity.Info,
                    Title = $"Всего {totalGb:0} ГБ памяти",
                    Detail = "Для современных игр с браузером и Discord в фоне комфортно 16 ГБ. На 8 ГБ Windows начинает " +
                             "выгружать данные на диск, отсюда фризы.",
                };

            if (configured >= rated - 50 && sticks.Count > 1 && parts.Count <= 1 && totalGb >= 15)
                yield return Finding.Ok("ram", $"Память: {totalGb:0} ГБ, {configured} МГц, два канала",
                    "Работает на заявленной частоте.");
            else if (configured >= rated - 50 && sticks.Count > 1)
                yield return Finding.Ok("ram", $"Память работает на заявленной частоте: {configured} МГц", $"{totalGb:0} ГБ, {sticks.Count} планки.");
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
                yield return Finding.Ok("power", $"План питания: {name}", "Процессор не сбрасывает частоту посреди игры.");
                yield break;
            }
            bool saver = guid == SysInfo.PlanPowerSaver;
            yield return new Finding
            {
                Id = "power",
                Severity = saver ? Severity.Critical : (laptop ? Severity.Info : Severity.Warn),
                Title = $"План питания «{name}»",
                Detail = saver
                    ? "Экономия энергии режет частоту процессора и видеокарты. В играх это прямой минус FPS."
                    : "Сбалансированный план опускает частоту процессора, когда нагрузка падает, и поднимает с задержкой — отсюда микрофризы. " +
                      (laptop ? "На ноутбуке включай высокую производительность только от сети." : ""),
                Impact = saver ? "до +30% FPS" : "ровнее 1% low",
                FixLabel = "Высокая производительность",
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
                    if (target == null) throw new InvalidOperationException("Windows не дала создать план «Высокая производительность».");
                    var (code, _) = SysInfo.Run("powercfg", "/setactive " + target);
                    if (code != 0) throw new InvalidOperationException("Windows не дала сменить план питания.");
                    return "Готово: включена высокая производительность.";
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
                yield return Finding.Ok("vbs", "Виртуализационная защита (VBS) выключена", "Процессор не тратит ресурсы на изоляцию памяти.");
                yield break;
            }
            yield return new Finding
            {
                Id = "vbs",
                Severity = hvci ? Severity.Warn : Severity.Info,
                Title = hvci ? "Включена «Целостность памяти» (HVCI)" : "Работает виртуализационная защита (VBS)",
                Detail = (hvci
                    ? "Windows проверяет каждый драйвер через гипервизор. Microsoft сама советует выключать это на игровых ПК: "
                    : "Windows держит часть системы в изолированной виртуальной среде. Это нагрузка на процессор: ") +
                    "минус несколько процентов FPS, особенно на процессорах старше 2020 года. Защита от редких атак на драйверы при этом слабее — решай сам.",
                Impact = hvci ? "до +5–10% FPS" : "до +5% FPS",
                FixLabel = "Выключить",
                FixNeedsAdmin = true,
                FixNeedsReboot = true,
                Fix = () =>
                {
                    Reg.SetDword(RegistryHive.LocalMachine, Hvci, "Enabled", 0);
                    Reg.SetDword(RegistryHive.LocalMachine, DeviceGuard, "EnableVirtualizationBasedSecurity", 0);
                    return "Готово, перезагрузи ПК. Если VBS остался — его держит Hyper-V, WSL или эмулятор Android: они без него не работают.";
                },
            };
        }

        // ---------- игровые настройки Windows ----------

        static IEnumerable<Finding> GameMode()
        {
            var v = Reg.GetInt(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled");
            if (v != 0)
                return One(Finding.Ok("gamemode", "Игровой режим включён", "Windows отдаёт игре приоритет и не ставит обновления во время игры."));
            return One(new Finding
            {
                Id = "gamemode",
                Severity = Severity.Warn,
                Title = "Игровой режим выключен",
                Detail = "Без него Windows может запускать обновления и фоновые задачи прямо во время игры.",
                Impact = "меньше фризов",
                FixLabel = "Включить",
                Fix = () =>
                {
                    Reg.SetDword(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1);
                    Reg.SetDword(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1);
                    return "Готово: игровой режим включён.";
                },
            });
        }

        static IEnumerable<Finding> GameDvr()
        {
            var capture = Reg.GetInt(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
            var dvr = Reg.GetInt(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled");
            if (capture == 0 || dvr == 0)
                return One(Finding.Ok("dvr", "Фоновая запись Xbox выключена", "Игры не пишутся на диск в фоне."));
            return One(new Finding
            {
                Id = "dvr",
                Severity = Severity.Warn,
                Title = "Xbox Game Bar записывает игры в фоне",
                Detail = "Фоновая запись постоянно кодирует видео, чтобы можно было сохранить последние минуты. Это нагрузка на видеокарту, даже если ты ничего не сохраняешь.",
                Impact = "до +3–5% FPS",
                FixLabel = "Выключить запись",
                Fix = () =>
                {
                    Reg.SetDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
                    Reg.SetDword(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0);
                    return "Готово: фоновая запись выключена.";
                },
            });
        }

        static IEnumerable<Finding> MouseAccel()
        {
            var speed = Reg.Get(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed") as string;
            if (speed == "0")
                return One(Finding.Ok("mouse", "Ускорение мыши выключено", "Прицел двигается ровно на столько, на сколько двинул мышь."));
            return One(new Finding
            {
                Id = "mouse",
                Severity = Severity.Warn,
                Title = "Включено ускорение мыши («Повышенная точность»)",
                Detail = "Быстрое движение уводит прицел дальше медленного на то же расстояние. Мышечная память не вырабатывается — в шутерах это мимо.",
                Impact = "стабильный аим",
                FixLabel = "Выключить",
                Fix = () =>
                {
                    Reg.SetString(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", "0");
                    Reg.SetString(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1", "0");
                    Reg.SetString(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2", "0");
                    Native.SystemParametersInfo(Native.SPI_SETMOUSE, 0, new[] { 0, 0, 0 }, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
                    return "Готово: ускорение мыши выключено, работает сразу.";
                },
            });
        }

        static IEnumerable<Finding> Hags()
        {
            var mode = Reg.GetInt(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
            if (mode == 2)
                return One(Finding.Ok("hags", "Аппаратное планирование GPU включено", "Видеокарта сама управляет очередью кадров."));
            if (mode == null) return Enumerable.Empty<Finding>(); // видеокарта или Windows его не поддерживают
            return One(new Finding
            {
                Id = "hags",
                Severity = Severity.Info,
                Title = "Аппаратное планирование GPU выключено",
                Detail = "Видеокарта поддерживает его. Оно снимает часть работы с процессора и немного снижает задержку. Нужно для DLSS Frame Generation.",
                Impact = "меньше задержка",
                FixLabel = "Включить",
                FixNeedsAdmin = true,
                FixNeedsReboot = true,
                Fix = () =>
                {
                    Reg.SetDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2);
                    return "Готово, заработает после перезагрузки.";
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
                    Title = $"На диске {letter} свободно всего {freeGb:0.#} ГБ",
                    Detail = "Windows нужно место под файл подкачки, кэш шейдеров и обновления. Когда диск почти полон, игры начинают фризить при подгрузке.",
                    Impact = "меньше фризов",
                    Guide = "Открой вкладку «Очистка» в WinOpti: временные файлы и кэш обновлений обычно освобождают несколько гигабайт.",
                };
            else
                yield return Finding.Ok("disk", $"На диске {letter} свободно {freeGb:0} ГБ", "Места хватает.");

            var hdd = SysInfo.Wmi("SELECT MediaType FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage")
                .Any(d => Convert.ToInt32(d["MediaType"] ?? 0) == 3);
            var ssd = SysInfo.Wmi("SELECT MediaType FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage")
                .Any(d => Convert.ToInt32(d["MediaType"] ?? 0) == 4);
            if (hdd && !ssd)
                yield return new Finding
                {
                    Id = "hdd",
                    Severity = Severity.Warn,
                    Title = "В ПК только жёсткий диск (HDD)",
                    Detail = "Современные игры подгружают текстуры на ходу. С HDD это долгие загрузки и фризы при входе в новые зоны.",
                    Impact = "загрузки в 3–5× быстрее",
                    Guide = "Самый заметный апгрейд за свои деньги — SSD на 500 ГБ–1 ТБ. Поставь на него Windows и игры.",
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
                    Title = onHdd.Count == 1 ? $"{onHdd[0].Name} установлена на жёсткий диск (HDD)" : $"{onHdd.Count} игры установлены на жёсткий диск (HDD)",
                    Detail = $"{string.Join(", ", onHdd.Select(g => g.Name))}. В ПК есть SSD, но игры лежат на механическом диске: отсюда долгие загрузки и фризы, когда игра подгружает новые текстуры.",
                    Impact = "загрузки в 3–5× быстрее",
                    Guide = "Steam: правой кнопкой по игре → Свойства → Установленные файлы → Переместить папку установки → выбери диск с SSD. Epic Games и Riot: переустанови игру на SSD.",
                };
            else if (onHdd.Count == 0)
                yield return Finding.Ok("games_hdd", "Игры на быстром диске", "Все найденные игры лежат на SSD.");
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
                        Title = "Не установлен драйвер видеокарты",
                        Detail = "Windows использует базовый видеодрайвер: игры идут на минимальной скорости или не запускаются.",
                        Impact = "игры заработают в полную силу",
                        Guide = "Скачай драйвер с сайта производителя видеокарты: nvidia.com, amd.com или intel.com.",
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
                        Title = $"Драйвер видеокарты от {date:dd.MM.yyyy}",
                        Detail = $"{name}: драйверу {days / 30} мес. В новых драйверах — оптимизации под свежие игры и исправления статтеров.",
                        Guide = "Обнови через NVIDIA App / GeForce Experience или AMD Adrenalin, либо скачай с сайта производителя.",
                    };
                else
                    yield return Finding.Ok("gpu_driver", $"Драйвер видеокарты свежий: {date:dd.MM.yyyy}", name);
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
                yield return Finding.Ok("bg", "Фон не грузит процессор", "Ни одна программа в фоне не берёт больше 5% процессора.");
                yield break;
            }
            double total = hogs.Sum(h => h.cpu);
            yield return new Finding
            {
                Id = "bg",
                Severity = total >= 15 ? Severity.Warn : Severity.Info,
                Title = $"Фоновые программы занимают {total:0}% процессора",
                Detail = "Сейчас больше всего грузят: " + string.Join(", ", hogs.Select(h => $"{h.name} ({h.cpu:0}%)")) +
                         ". Во время игры эти ресурсы отнимаются у неё.",
                Impact = "больше ресурсов игре",
                Guide = "Закрой эти программы перед игрой. Если что-то из них запускается само — выключи во вкладке «Автозагрузка».",
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
                    Title = "Ноутбук работает от батареи",
                    Detail = "От батареи процессор и видеокарта ограничены по мощности — в играх FPS может упасть в 2 раза.",
                    Impact = "до 2× FPS от сети",
                    Guide = "Подключи зарядку перед игрой.",
                };
            else
                yield return Finding.Ok("battery", "Ноутбук работает от сети", "Процессор и видеокарта получают полную мощность.");
        }

        static IEnumerable<Finding> Uptime()
        {
            var up = SysInfo.Uptime;
            if (up.TotalDays < 7)
            {
                yield return Finding.Ok("uptime", "ПК перезагружался недавно", $"Без перезагрузки {(up.TotalDays >= 1 ? $"{up.TotalDays:0} дн." : $"{up.TotalHours:0} ч")}.");
                yield break;
            }
            yield return new Finding
            {
                Id = "uptime",
                Severity = Severity.Info,
                Title = $"ПК не перезагружался {up.TotalDays:0} дней",
                Detail = "Быстрый запуск Windows не сбрасывает систему при выключении. За неделю копятся утечки памяти драйверов и программ.",
                Guide = "Перезагрузи ПК перед игровой сессией: именно «Перезагрузка», а не «Завершение работы».",
            };
        }
    }
}
