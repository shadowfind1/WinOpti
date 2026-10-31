using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using WinOpti.Fps;
using WinOpti.Sys;
using WinOpti.Tweaks;

namespace WinOpti.Game
{
    /// <summary>
    /// Авто-игровой режим. Отдельный тихий процесс (WinOpti.exe --gamemode) без окна, запускается задачей планировщика
    /// при входе в Windows. Раз в 2 секунды смотрит, не открыта ли игра; пока игра идёт — максимальная производительность,
    /// пауза индексации поиска и пониженный приоритет фоновых программ. После игры всё возвращает как было.
    /// Ничего не закрывает: автоматически закрывать чужие программы опасно (несохранённое, звонок в Discord).
    /// </summary>
    public static class GameMode
    {
        public const string TaskName = @"WinOpti\GameMode";
        const string MutexName = @"Local\WinOpti.GameMode";
        const string StopEventName = @"Local\WinOpti.GameMode.Stop";
        const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        static string BinDir => Path.Combine(App.DataDir, "bin");
        public static string InstalledExe => Path.Combine(BinDir, "WinOpti.exe");
        static string LogFile => Path.Combine(App.DataDir, "gamemode.log");
        /// <summary>Что поменяно в текущей игре — на случай, если процесс убьют посреди игры: при следующем запуске вернём.</summary>
        static string StateFile => Path.Combine(App.DataDir, "gamemode_state.txt");

        // программы, которым в игре точно не нужен приоритет; Discord не трогаем — в нём часто голосовой чат
        static readonly string[] AlwaysLower = { "chrome", "msedge", "firefox", "opera", "browser", "brave", "OneDrive", "Teams", "ms-teams",
                                                 "EpicGamesLauncher", "Spotify", "SearchProtocolHost", "SearchFilterHost", "CompatTelRunner" };

        // ---------- включение из окна WinOpti ----------

        public static bool Enabled => SysInfo.Run("schtasks.exe", $"/Query /TN \"{TaskName}\"").code == 0;

        public static bool Running
        {
            get
            {
                try { using (Mutex.OpenExisting(MutexName)) return true; }
                catch { return false; }
            }
        }

        /// <summary>Копируем WinOpti в свою папку (чтобы задача не сломалась, если exe перенесут) и создаём задачу при входе.</summary>
        public static void Enable()
        {
            StopRunning();
            Directory.CreateDirectory(BinDir);
            var self = Process.GetCurrentProcess().MainModule.FileName;
            if (!self.Equals(InstalledExe, StringComparison.OrdinalIgnoreCase)) File.Copy(self, InstalledExe, true);
            var (code, output) = SysInfo.Run("schtasks.exe",
                $"/Create /TN \"{TaskName}\" /TR \"\\\"{InstalledExe}\\\" --gamemode\" /SC ONLOGON /RL HIGHEST /F");
            if (code != 0) throw new InvalidOperationException(L.T("Не удалось создать задачу: ", "Couldn’t create the task: ") + output.Trim());
            SysInfo.Run("schtasks.exe", $"/Run /TN \"{TaskName}\"");
            TweakEngine.LogEntry("gamemode", L.T("Авто-игровой режим включён", "Auto game mode turned on"), "on");
        }

        /// <summary>После обновления WinOpti: заменить копию в своей папке на новую и перезапустить режим. Если режим не включён — ничего.</summary>
        public static void RefreshCopy()
        {
            var self = Process.GetCurrentProcess().MainModule.FileName;
            if (!File.Exists(InstalledExe) || self.Equals(InstalledExe, StringComparison.OrdinalIgnoreCase)) return;
            if (SysInfo.Run("schtasks.exe", $"/Query /TN \"{TaskName}\"").code != 0) return;
            StopRunning();
            File.Copy(self, InstalledExe, true);
            SysInfo.Run("schtasks.exe", $"/Run /TN \"{TaskName}\"");
        }

        public static void Disable()
        {
            StopRunning();
            SysInfo.Run("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F");
            TweakEngine.LogEntry("gamemode", L.T("Авто-игровой режим выключен", "Auto game mode turned off"), "off");
        }

        /// <summary>Попросить работающий режим вернуть всё и выйти.</summary>
        static void StopRunning()
        {
            try
            {
                using (var ev = EventWaitHandle.OpenExisting(StopEventName)) ev.Set();
                for (int i = 0; i < 30 && Running; i++) Thread.Sleep(100);
            }
            catch (WaitHandleCannotBeOpenedException) { }
        }

        // ---------- сам режим (процесс --gamemode) ----------

        public static void Run()
        {
            using (var mutex = new Mutex(true, MutexName, out bool first))
            {
                if (!first) return; // уже работает
                using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopEventName))
                {
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
                    RestoreLeftovers();
                    Session session = null;
                    var known = KnownGames();
                    var knownAt = DateTime.UtcNow;
                    while (!stop.WaitOne(2000))
                    {
                        try
                        {
                            if (DateTime.UtcNow - knownAt > TimeSpan.FromMinutes(30)) { known = KnownGames(); knownAt = DateTime.UtcNow; }
                            var game = DetectGame(known);
                            if (session == null && game != null) session = Session.Begin(game);
                            else if (session != null)
                            {
                                if (!Alive(session.Game)) { session.End(); session = null; }
                                else session.Tick();
                            }
                        }
                        catch (Exception e) { Log("ошибка: " + e.Message); }
                    }
                    session?.End();
                }
            }
        }

        /// <summary>Игры, которые точно игры: найденные в Steam, Epic и Riot, и те, что уже замеряли.</summary>
        static HashSet<string> KnownGames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (var g in Tests.GpuCheck.Games()) set.Add(Path.GetFileNameWithoutExtension(g.Exe)); } catch { }
            try { foreach (var r in FpsStore.Load()) set.Add(r.Game); } catch { }
            return set;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        /// <summary>Игра — известная игра на переднем плане или любая не-системная программа на весь экран (неизвестные игры).</summary>
        static string DetectGame(HashSet<string> known)
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            try
            {
                using (var p = Process.GetProcessById((int)pid))
                    if (known.Contains(p.ProcessName)) return p.ProcessName;
            }
            catch { return null; }
            return ForegroundGame.Detect();
        }

        static void RestoreLeftovers()
        {
            try
            {
                if (!File.Exists(StateFile)) return;
                foreach (var line in File.ReadAllLines(StateFile))
                {
                    if (line.StartsWith("scheme=")) SysInfo.Run("powercfg", "/setactive " + line.Substring(7));
                    if (line == "search=stopped") SysInfo.Run("sc.exe", "start WSearch");
                }
                File.Delete(StateFile);
                Log("вернули настройки после прошлой игры (режим был прерван)");
            }
            catch { }
        }

        static bool Alive(string name)
        {
            var ps = Process.GetProcessesByName(name);
            foreach (var p in ps) p.Dispose();
            return ps.Length > 0;
        }

        static void Log(string text)
        {
            try
            {
                Directory.CreateDirectory(App.DataDir);
                if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 256 * 1024) File.Delete(LogFile);
                File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n");
            }
            catch { }
        }

        /// <summary>Одна игровая сессия: что поменяли, чтобы вернуть.</summary>
        sealed class Session
        {
            public string Game;
            DateTime _started;
            string _oldScheme;
            bool _searchStopped;
            readonly Dictionary<int, ProcessPriorityClass> _lowered = new Dictionary<int, ProcessPriorityClass>();
            HashSet<string> _toLower;
            int _ticks;

            public static Session Begin(string game)
            {
                var s = new Session { Game = game, _started = DateTime.UtcNow };
                // максимальная производительность, если сейчас другой план
                var active = SysInfo.ActivePowerPlan().guid;
                if (active != null && !active.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase) && !SysInfo.OnBattery)
                {
                    if (SysInfo.Run("powercfg", "/setactive " + HighPerformance).code == 0) s._oldScheme = active;
                }
                // индексация поиска на паузу — она будит диск и процессор
                if (Regex.IsMatch(SysInfo.Run("sc.exe", "query WSearch").output, @"STATE\s*:\s*4\b") && SysInfo.Run("sc.exe", "stop WSearch").code == 0)
                    s._searchStopped = true;
                // фоновые программы: постоянный список + «воры» из прошлых замеров
                s._toLower = new HashSet<string>(AlwaysLower, StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var t in FpsStore.Load().Where(r => r.Thieves != null).SelectMany(r => r.Thieves))
                        if (!t.Hardware && t.Process != null && t.Action != null && (t.Action.StartsWith("close:") || t.Action.StartsWith("startup:")) && !t.Process.Equals("Discord", StringComparison.OrdinalIgnoreCase))
                            s._toLower.Add(t.Process);
                }
                catch { }
                s._toLower.Remove(game);
                try { File.WriteAllLines(StateFile, new[] { s._oldScheme != null ? "scheme=" + s._oldScheme : "", s._searchStopped ? "search=stopped" : "" }); } catch { }
                s.LowerPriorities();
                Log($"игра {game}: план питания {(s._oldScheme != null ? "→ максимальная производительность" : "без изменений")}, индексация {(s._searchStopped ? "на паузе" : "не работала")}, понижен приоритет {s._lowered.Count} процессов");
                return s;
            }

            /// <summary>Каждые ~10 секунд: новые вкладки браузера и т. п. тоже понижаем.</summary>
            public void Tick()
            {
                if (++_ticks % 5 == 0) LowerPriorities();
            }

            void LowerPriorities()
            {
                foreach (var name in _toLower)
                    foreach (var p in Process.GetProcessesByName(name))
                        try
                        {
                            if (!_lowered.ContainsKey(p.Id) && (p.PriorityClass == ProcessPriorityClass.Normal || p.PriorityClass == ProcessPriorityClass.AboveNormal))
                            {
                                _lowered[p.Id] = p.PriorityClass;
                                p.PriorityClass = ProcessPriorityClass.BelowNormal;
                            }
                        }
                        catch { } // защищённый или уже закрылся
                        finally { p.Dispose(); }
            }

            public void End()
            {
                if (_oldScheme != null) SysInfo.Run("powercfg", "/setactive " + _oldScheme);
                if (_searchStopped) SysInfo.Run("sc.exe", "start WSearch");
                foreach (var kv in _lowered)
                    try { using (var p = Process.GetProcessById(kv.Key)) p.PriorityClass = kv.Value; } catch { }
                try { File.Delete(StateFile); } catch { }
                var minutes = (DateTime.UtcNow - _started).TotalMinutes;
                Log($"игра {Game} закрыта через {minutes:0} мин, всё возвращено");
                if (minutes >= 1) TweakEngine.LogEntry("gamemode:" + Game, L.T($"Игровой режим: {Game}, {minutes:0} мин", $"Game mode: {Game}, {minutes:0} min"), "gamemode");
            }
        }
    }
}
