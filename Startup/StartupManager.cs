using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml.Linq;
using Microsoft.Win32;
using WinOpti.Sys;
using WinOpti.Tweaks;

namespace WinOpti.Startup
{
    public enum Source { Registry, Folder, Task, Service, Store }

    public sealed class StartupItem
    {
        public string Id, Name, Command, Exe, Where, Company = "", Target = "";
        public Source Source;
        public bool Enabled, Admin, Missing;
        /// <summary>Включено или выключено политикой организации — переключать нельзя.</summary>
        public bool Locked;
        public RegistryHive Hive;
        public string Approved, ValueName, TaskPath, ServiceName, StoreKey;
        /// <summary>"keep" — лучше оставить, "disable" — можно выключить, null — без подсказки.</summary>
        public string Hint, HintText;
        /// <summary>Что эта программа занимает прямо сейчас (если запущена): память и процессорное время с момента запуска.</summary>
        public long RamBytes;
        public TimeSpan CpuTime;
        public int Processes;
    }

    /// <summary>
    /// Автозагрузка, как в WinOpti 1 (startup.py): разделы реестра Run, папки «Автозагрузка» и сторонние задачи
    /// планировщика при входе или загрузке. Записи реестра и папок не удаляем, а помечаем выключенными
    /// в StartupApproved, как Диспетчер задач: всё обратимо и совпадает с ним.
    /// </summary>
    public static class StartupManager
    {
        const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Run32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        static readonly (string id, RegistryHive hive, string path, string approved, string where)[] RegSources =
        {
            ("hkcu_run", RegistryHive.CurrentUser, Run, "Run", L.T("реестр, текущий пользователь", "registry, current user")),
            ("hklm_run", RegistryHive.LocalMachine, Run, "Run", L.T("реестр, все пользователи", "registry, all users")),
            ("hklm_run32", RegistryHive.LocalMachine, Run32, "Run32", L.T("реестр, все пользователи (32-бит)", "registry, all users (32-bit)")),
            ("hkcu_run32", RegistryHive.CurrentUser, Run32, "Run32", L.T("реестр, текущий пользователь (32-бит)", "registry, current user (32-bit)")),
        };

        static readonly (string id, RegistryHive hive, string folder, string where)[] FolderSources =
        {
            ("user_folder", RegistryHive.CurrentUser, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup)), L.T("папка «Автозагрузка», текущий пользователь", "Startup folder, current user")),
            ("common_folder", RegistryHive.LocalMachine, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), L.T("папка «Автозагрузка», все пользователи", "Startup folder, all users")),
        };

        // Подсказки: (подстроки в имени, пути или издателе, текст). Keep проверяем первым.
        static readonly (string[] keys, string text)[] Keep =
        {
            (new[] { "securityhealth" }, L.T("значок «Безопасности Windows»", "Windows Security tray icon")),
            (new[] { "vanguard", "vgtray" }, L.T("античит Riot: без него не запустятся Valorant и League of Legends", "Riot anti-cheat: Valorant and League of Legends won’t start without it")),
            (new[] { "faceit ac", "faceitac" }, L.T("античит FACEIT", "FACEIT anti-cheat")),
            (new[] { "realtek", "rtkaud", "ravcpl", "rthdvcpl" }, L.T("звуковой драйвер", "audio driver")),
            (new[] { "synaptics", "syntp", "elan", "etdctrl" }, L.T("драйвер тачпада", "touchpad driver")),
            (new[] { "igfx", "intel" }, L.T("драйвер Intel", "Intel driver")),
            (new[] { "avp.exe", "kaspersky", "eset", "egui", "avast", "avgui", "drweb", "360tray", "defender" }, L.T("антивирус", "antivirus")),
            (new[] { "ctfmon" }, L.T("языковая панель и ввод текста", "language bar and text input")),
            (new[] { "nvidia", "nvcontainer", "nvdisplay" }, L.T("служба драйвера NVIDIA: без неё не работают панель управления NVIDIA и настройки видеокарты", "NVIDIA driver service: the NVIDIA Control Panel and GPU settings need it")),
            (new[] { "amd external events", "amdrsserv", "radeon" }, L.T("служба драйвера AMD: настройки видеокарты", "AMD driver service: GPU settings")),
            (new[] { "lghub", "logitech", "razer", "steelseries", "corsair", "icue", "bloody" }, L.T("ПО мыши или клавиатуры: настройки DPI, макросы и подсветка", "mouse or keyboard software: DPI settings, macros and lighting")),
        };
        static readonly (string[] keys, string text)[] Disable =
        {
            (new[] { "steam" }, L.T("лаунчер: сам запустится, когда откроешь игру", "launcher: starts by itself when you open a game")),
            (new[] { "epicgames", "epic games" }, L.T("лаунчер Epic Games", "Epic Games launcher")),
            (new[] { "riotclient" }, L.T("лаунчер Riot", "Riot launcher")),
            (new[] { "eadesktop", "ea desktop", "origin" }, L.T("лаунчер EA", "EA launcher")),
            (new[] { "battle.net" }, L.T("лаунчер Battle.net", "Battle.net launcher")),
            (new[] { "ubisoft", "upc.exe" }, L.T("лаунчер Ubisoft", "Ubisoft launcher")),
            (new[] { "roblox" }, L.T("лаунчер Roblox", "Roblox launcher")),
            (new[] { "faceit" }, L.T("клиент FACEIT: запусти его перед игрой", "FACEIT client: start it before you play")),
            (new[] { "discord" }, L.T("мессенджер: оставь, если нужен сразу после включения ПК", "messenger: keep it if you need it right after the PC starts")),
            (new[] { "telegram", "viber", "whatsapp", "skype", "teams", "zoom" }, L.T("мессенджер: оставь, если нужен сразу после включения ПК", "messenger: keep it if you need it right after the PC starts")),
            (new[] { "spotify", "yandex music", "яндекс музыка" }, L.T("музыка: запускается за секунду вручную", "music: takes a second to start by hand")),
            (new[] { "onedrive" }, L.T("облако: выключи, если не синхронизируешь файлы", "cloud storage: turn off if you don’t sync files")),
            (new[] { "edgeupdate", "browser update", "gupdate", "brave update" }, L.T("обновления браузера: браузер проверит их сам, когда ты его откроешь", "browser updates: the browser checks for them when you open it")),
            (new[] { "autolaunch", "browser.exe", "opera" }, L.T("фоновый запуск браузера ради «быстрого старта»", "background browser launch for “fast start”")),
            (new[] { "yourphone", "phone link", "связь с телефоном" }, L.T("«Связь с телефоном»: выключи, если не подключаешь телефон к ПК", "Phone Link: turn off if you don’t connect your phone to the PC")),
            (new[] { "pcmanager", "pc manager" }, L.T("Microsoft PC Manager — ещё одна программа для очистки, Windows без неё работает так же", "Microsoft PC Manager: yet another cleanup app, Windows works the same without it")),
            (new[] { "lively" }, L.T("живые обои: постоянно нагружают видеокарту, особенно заметно в играх", "live wallpaper: constantly loads the GPU, especially noticeable in games")),
            (new[] { "googleupdate", "google update" }, L.T("обновления Google: проверятся сами при запуске Chrome", "Google updates: checked by Chrome itself when it starts")),
            (new[] { "adobe", "acrotray" }, L.T("служебные программы и обновления Adobe", "Adobe helpers and updates")),
            (new[] { "jusched", "java update" }, L.T("проверка обновлений Java", "Java update check")),
            (new[] { "ituneshelper" }, L.T("помощник iTunes", "iTunes helper")),
            (new[] { "ccleaner" }, L.T("фоновый мониторинг CCleaner", "CCleaner background monitoring")),
            (new[] { "utorrent", "qbittorrent", "bittorrent" }, L.T("торрент-клиент", "torrent client")),
        };

        static (string hint, string text) HintFor(string text)
        {
            text = text.ToLowerInvariant();
            foreach (var (keys, msg) in Keep) if (keys.Any(text.Contains)) return ("keep", msg);
            foreach (var (keys, msg) in Disable) if (keys.Any(text.Contains)) return ("disable", msg);
            return (null, null);
        }

        // ---------- реестр ----------

        static Dictionary<string, object> Values(RegistryHive hive, string path)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var k = Reg.Root(hive).OpenSubKey(path))
                    if (k != null) foreach (var n in k.GetValueNames()) d[n] = k.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            }
            catch (Exception e) when (e is System.Security.SecurityException || e is UnauthorizedAccessException || e is IOException) { }
            return d;
        }

        /// <summary>StartupApproved: первый байт чётный (02, 06) = включено, нечётный (03, 07) = выключено.</summary>
        static bool ApprovedEnabled(object data) => !(data is byte[] b && b.Length > 0 && (b[0] & 1) == 1);

        static byte[] ApprovedBytes(bool enable)
        {
            var b = new byte[12];
            if (enable) { b[0] = 2; return b; }
            b[0] = 3; // когда выключено — как у Диспетчера задач
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(b, 4);
            return b;
        }

        /// <summary>Путь к программе из командной строки.</summary>
        static string ExeOf(string cmd)
        {
            cmd = Environment.ExpandEnvironmentVariables((cmd ?? "").Trim());
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                return end > 0 ? cmd.Substring(1, end - 1) : cmd.Substring(1);
            }
            int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? cmd.Substring(0, i + 4) : cmd.Split(' ')[0];
        }

        // ---------- задачи планировщика ----------

        static IEnumerable<StartupItem> Tasks()
        {
            var ns = Actions.TaskNs;
            var root = Actions.TasksDir;
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                string[] subdirs, files;
                try { subdirs = Directory.GetDirectories(dir); files = Directory.GetFiles(dir); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { continue; }
                foreach (var s in subdirs)
                    if (!(dir == root && Path.GetFileName(s).Equals("Microsoft", StringComparison.OrdinalIgnoreCase))) stack.Push(s);
                foreach (var f in files)
                {
                    XElement doc;
                    try { doc = XDocument.Load(f).Root; }
                    catch { continue; }
                    var triggers = doc?.Element(ns + "Triggers");
                    if (triggers == null || !triggers.Elements().Any(t => t.Name == ns + "LogonTrigger" || t.Name == ns + "BootTrigger")) continue;
                    var exec = doc.Element(ns + "Actions")?.Element(ns + "Exec");
                    string command = "";
                    if (exec != null)
                    {
                        var c = (exec.Element(ns + "Command")?.Value ?? "").Trim();
                        var args = (exec.Element(ns + "Arguments")?.Value ?? "").Trim();
                        if (c.Length > 0) command = $"\"{c.Trim('"')}\" {args}".Trim();
                    }
                    var enabled = doc.Element(ns + "Settings")?.Element(ns + "Enabled");
                    var path = "\\" + f.Substring(root.Length).TrimStart('\\');
                    yield return new StartupItem
                    {
                        Id = "task:" + path, Source = Source.Task, Name = Path.GetFileName(f), Command = command,
                        Exe = command.Length > 0 ? ExeOf(command) : "", Where = L.T("задача планировщика", "scheduled task"),
                        Enabled = !(enabled != null && enabled.Value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)),
                        Admin = true, TaskPath = path,
                    };
                }
            }
        }

        // ---------- службы ----------

        const string ServicesKey = @"SYSTEM\CurrentControlSet\Services\";
        static string OffServicesFile => Path.Combine(App.DataDir, "startup_services.json");
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        /// <summary>Службы, которые выключили мы: имя -> был ли «отложенный запуск». Нужно, чтобы показать их и вернуть как было.</summary>
        static Dictionary<string, bool> OffServices()
        {
            try { return Json.Deserialize<Dictionary<string, bool>>(File.ReadAllText(OffServicesFile)) ?? new Dictionary<string, bool>(); }
            catch { return new Dictionary<string, bool>(); }
        }

        static void SaveOffServices(Dictionary<string, bool> d)
        {
            Directory.CreateDirectory(App.DataDir);
            File.WriteAllText(OffServicesFile, Json.Serialize(d));
        }

        /// <summary>
        /// Сторонние службы с автозапуском (и выключенные нами). Системные — из папки Windows — и Защитник не показываем:
        /// их выключение ломает Windows. Службы драйверов из DriverStore (NVIDIA, Intel) — сторонние, показываем.
        /// </summary>
        static IEnumerable<StartupItem> Services()
        {
            var off = OffServices();
            foreach (var s in SysInfo.Wmi("SELECT Name, DisplayName, PathName, StartMode FROM Win32_Service"))
            {
                var name = s["Name"] as string;
                var cmd = s["PathName"] as string;
                var mode = s["StartMode"] as string;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(cmd)) continue;
                bool auto = mode == "Auto";
                if (!auto && !(mode == "Manual" && off.ContainsKey(name))) continue;
                var exe = ExeOf(cmd);
                bool system = exe.StartsWith(WinDir + "\\", StringComparison.OrdinalIgnoreCase)
                              && exe.IndexOf(@"\DriverStore\", StringComparison.OrdinalIgnoreCase) < 0;
                if (system || exe.IndexOf("Windows Defender", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                yield return new StartupItem
                {
                    Id = "service:" + name, Source = Source.Service, Name = s["DisplayName"] as string ?? name, Command = cmd, Exe = exe,
                    Where = L.T("служба", "service"), Enabled = auto, Admin = true, ServiceName = name,
                };
            }
        }

        /// <summary>Выключаем в «вручную», а не «отключена»: если службу позовёт её программа, она запустится.</summary>
        static void ServiceSet(string name, bool enable)
        {
            var off = OffServices();
            string mode;
            if (enable) mode = off.TryGetValue(name, out var delayed) && delayed ? "delayed-auto" : "auto";
            else
            {
                mode = "demand";
                off[name] = Reg.GetInt(RegistryHive.LocalMachine, ServicesKey + name, "DelayedAutostart") == 1;
            }
            var (code, output) = SysInfo.Run("sc.exe", $"config \"{name}\" start= {mode}");
            if (code != 0) throw new InvalidOperationException($"sc {name}: {output.Trim()}");
            if (enable) off.Remove(name);
            SaveOffServices(off);
        }

        // ---------- приложения из Microsoft Store ----------

        const string StoreRoot = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\";

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int SHLoadIndirectString(string source, StringBuilder buf, int size, IntPtr reserved);

        /// <summary>State: 0 — выключено, 1 — выключено пользователем, 2 — включено, 3/4 — выключено/включено политикой.</summary>
        static bool StoreEnabled(int? state) => state == 2 || state == 4;

        /// <summary>Понятное имя из пакета («Связь с телефоном», а не Microsoft.YourPhone_8wekyb3d8bbwe) и папка приложения.</summary>
        static (string name, string folder) StorePackage(string family)
        {
            int cut = family.LastIndexOf('_');
            if (cut < 0) return (family, null);
            string pkg = family.Substring(0, cut), publisher = family.Substring(cut + 1);
            try
            {
                using (var repo = Reg.Root(RegistryHive.CurrentUser).OpenSubKey(StoreRoot + @"Repository\Packages"))
                {
                    var full = repo?.GetSubKeyNames().FirstOrDefault(k => k.StartsWith(pkg + "_", StringComparison.OrdinalIgnoreCase) && k.EndsWith("_" + publisher, StringComparison.OrdinalIgnoreCase));
                    if (full != null)
                        using (var k = repo.OpenSubKey(full))
                        {
                            var display = k?.GetValue("DisplayName") as string;
                            var folder = k?.GetValue("PackageRootFolder") as string;
                            if (display != null && display.StartsWith("@"))
                            {
                                var sb = new StringBuilder(512);
                                display = SHLoadIndirectString(display, sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : null;
                            }
                            if (!string.IsNullOrWhiteSpace(display)) return (display, folder);
                        }
                }
            }
            catch { }
            return (pkg.Substring(pkg.LastIndexOf('.') + 1), null); // 12030rocksdanister.LivelyWallpaper -> LivelyWallpaper
        }

        static IEnumerable<StartupItem> StoreApps()
        {
            var list = new List<StartupItem>();
            try
            {
                using (var root = Reg.Root(RegistryHive.CurrentUser).OpenSubKey(StoreRoot + "SystemAppData"))
                {
                    if (root == null) return list;
                    foreach (var family in root.GetSubKeyNames())
                        using (var fk = root.OpenSubKey(family))
                        {
                            if (fk == null) continue;
                            foreach (var task in fk.GetSubKeyNames())
                                using (var tk = fk.OpenSubKey(task))
                                {
                                    if (!(tk?.GetValue("State") is int state)) continue;
                                    var (name, folder) = StorePackage(family);
                                    list.Add(new StartupItem
                                    {
                                        Id = $@"store:{family}\{task}", Source = Source.Store, Name = name, Command = family, Exe = "",
                                        Where = L.T("приложение из Microsoft Store", "Microsoft Store app"), Enabled = StoreEnabled(state), Locked = state >= 3,
                                        StoreKey = $@"{StoreRoot}SystemAppData\{family}\{task}", Target = folder ?? "",
                                    });
                                }
                        }
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException || e is UnauthorizedAccessException || e is IOException) { }
            return list;
        }

        // ---------- ярлыки ----------

        static object _shell;

        /// <summary>Куда ведёт ярлык .lnk (через WScript.Shell, без PowerShell).</summary>
        static string LinkTarget(string lnk)
        {
            try
            {
                if (_shell == null) _shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                var sc = _shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, _shell, new object[] { lnk });
                return sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null) as string ?? "";
            }
            catch { return ""; }
        }

        // ---------- публичный API ----------

        /// <summary>-> (записи, предупреждение или null). Включённые сверху.</summary>
        public static (List<StartupItem> items, string warning) List()
        {
            var items = new List<StartupItem>();
            string warning = null;
            foreach (var (id, hive, path, approved, where) in RegSources)
            {
                var flags = Values(hive, ApprovedRoot + approved);
                foreach (var kv in Values(hive, path))
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    var cmd = Convert.ToString(kv.Value);
                    items.Add(new StartupItem
                    {
                        Id = $"{id}:{kv.Key}", Source = Source.Registry,
                        Name = Regex.Replace(kv.Key, "_[0-9A-Fa-f]{16,}$", ""), // BrowserAutoLaunch_9A74EE… -> BrowserAutoLaunch
                        Command = cmd, Exe = ExeOf(cmd), Where = where,
                        Enabled = ApprovedEnabled(flags.TryGetValue(kv.Key, out var f) ? f : null),
                        Admin = hive == RegistryHive.LocalMachine, Hive = hive, Approved = ApprovedRoot + approved, ValueName = kv.Key,
                    });
                }
            }
            foreach (var (id, hive, folder, where) in FolderSources)
            {
                var flags = Values(hive, ApprovedRoot + "StartupFolder");
                string[] files;
                try { files = Directory.GetFiles(folder); } catch { files = new string[0]; }
                foreach (var file in files.Where(x => !Path.GetFileName(x).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                {
                    var name = Path.GetFileName(file);
                    items.Add(new StartupItem
                    {
                        Id = $"{id}:{name}", Source = Source.Folder, Name = Path.GetFileNameWithoutExtension(file), Command = file, Exe = file,
                        Where = where, Enabled = ApprovedEnabled(flags.TryGetValue(name, out var f) ? f : null),
                        Admin = hive == RegistryHive.LocalMachine, Hive = hive, Approved = ApprovedRoot + "StartupFolder", ValueName = name,
                    });
                }
            }
            try { items.AddRange(Tasks()); }
            catch (Exception e) { warning = L.T("задачи планировщика не прочитаны: ", "scheduled tasks not read: ") + e.Message; }
            try { items.AddRange(Services()); }
            catch (Exception e) { warning = (warning == null ? "" : warning + "; ") + L.T("службы не прочитаны: ", "services not read: ") + e.Message; }
            items.AddRange(StoreApps());

            foreach (var it in items)
            {
                if (it.Source != Source.Store) it.Target = it.Exe.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? LinkTarget(it.Exe) : it.Exe;
                if (it.Target.Length > 0 && it.Source != Source.Store)
                {
                    try
                    {
                        if (File.Exists(it.Target)) it.Company = (FileVersionInfo.GetVersionInfo(it.Target).CompanyName ?? "").Trim();
                        else it.Missing = Path.IsPathRooted(it.Target);
                    }
                    catch { }
                }
                (it.Hint, it.HintText) = HintFor(string.Join(" ", it.Name, it.Exe, it.Target, it.Company));
                if (it.Missing) (it.Hint, it.HintText) = ("disable", L.T("программы уже нет на компьютере, а запись о запуске осталась. Выключение ничего не сломает", "the app is gone from this PC but its startup entry remains. Turning it off won’t break anything"));
            }
            MeasureUsage(items);
            return (items.OrderBy(i => !i.Enabled).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), warning);
        }

        static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        /// <summary>
        /// Сколько запущенная программа занимает прямо сейчас. Считаем все процессы из её папки:
        /// Discord, Steam и браузеры — это десяток процессов, а не один. Для программ из папки Windows — только сам exe.
        /// </summary>
        static void MeasureUsage(List<StartupItem> items)
        {
            List<(string path, long ram, TimeSpan cpu)> procs;
            try
            {
                procs = SysInfo.Wmi("SELECT ExecutablePath, WorkingSetSize, KernelModeTime, UserModeTime FROM Win32_Process")
                    .Select(p => (path: p["ExecutablePath"] as string, ram: Convert.ToInt64(p["WorkingSetSize"] ?? 0L),
                                  cpu: TimeSpan.FromTicks(Convert.ToInt64(p["KernelModeTime"] ?? 0L) + Convert.ToInt64(p["UserModeTime"] ?? 0L))))
                    .Where(p => !string.IsNullOrEmpty(p.path)).ToList();
            }
            catch { return; }
            foreach (var it in items)
            {
                if (string.IsNullOrEmpty(it.Target)) continue;
                // у приложения из Store Target — папка пакета, у остальных — exe
                bool package = it.Source == Source.Store;
                if (!package && !it.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                var dir = (package ? it.Target.TrimEnd('\\') : Path.GetDirectoryName(it.Target)) + "\\";
                bool system = dir.StartsWith(WinDir + "\\", StringComparison.OrdinalIgnoreCase);
                var mine = procs.Where(p => system ? p.path.Equals(it.Target, StringComparison.OrdinalIgnoreCase)
                                                   : p.path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)).ToList();
                it.Processes = mine.Count;
                it.RamBytes = mine.Sum(p => p.ram);
                it.CpuTime = TimeSpan.FromTicks(mine.Sum(p => p.cpu.Ticks));
            }
        }

        public static void SetEnabled(StartupItem it, bool enable)
        {
            if (it.Locked) throw new InvalidOperationException(L.T("запуском управляет политика организации", "startup is controlled by your organization’s policy"));
            if (it.Source == Source.Task) Actions.TaskSet(it.TaskPath, enable);
            else if (it.Source == Source.Service) ServiceSet(it.ServiceName, enable);
            else if (it.Source == Source.Store) Reg.SetDword(RegistryHive.CurrentUser, it.StoreKey, "State", enable ? 2 : 1);
            else
                using (var k = Reg.Root(it.Hive).CreateSubKey(it.Approved, true))
                    k.SetValue(it.ValueName, ApprovedBytes(enable), RegistryValueKind.Binary);
            TweakEngine.LogEntry("startup:" + it.Id, L.T("Автозагрузка: ", "Startup: ") + it.Name, enable ? "startup_on" : "startup_off");
        }

        /// <summary>Перечитать состояние одной записи (после переключения).</summary>
        public static bool IsEnabled(StartupItem it)
        {
            if (it.Source == Source.Task)
            {
                var s = Actions.TaskState(it.TaskPath);
                return s != "disabled" && s != "missing";
            }
            if (it.Source == Source.Service) return Reg.GetInt(RegistryHive.LocalMachine, ServicesKey + it.ServiceName, "Start") == 2;
            if (it.Source == Source.Store) return StoreEnabled(Reg.GetInt(RegistryHive.CurrentUser, it.StoreKey, "State"));
            return ApprovedEnabled(Values(it.Hive, it.Approved).TryGetValue(it.ValueName, out var f) ? f : null);
        }
    }
}
